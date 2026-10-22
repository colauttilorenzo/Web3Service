import { getApplicationAddress } from 'algosdk';
import type {
  Abi, Bytecode, ChainId, ContractResult, IAlgorandNetwork, IAvmProvider, ICheckResult, ITokenCreation, ITokenDefinition, ITokenInfo, ITransaction,
  ITransactionDetails, TransactionOptions, Web3ServiceOptions,
} from '../Models.js';
import type { IWeb3Service } from '../IWeb3Service.js';
import * as Configuration from '../Internal/Configuration.js';
import type { NetworkDefaults, ResolvedOptions } from '../Internal/Configuration.js';
import * as ContractMethods from '../Internal/ContractMethods.js';
import { receiptDetailsFailed, transactionNotFound } from '../Internal/Errors.js';
import * as Integers from '../Internal/Integers.js';
import * as ChainEvents from '../Internal/ChainEvents.js';
import * as SharedResults from '../Internal/SharedResults.js';
import { WriteQueue } from '../Internal/WriteQueue.js';
import { AlgodConnector } from './AlgodConnector.js';
import type { AlgodSession } from './AlgodSession.js';
import * as AlgorandSigner from './AlgorandSigner.js';
import type { AvmSigner } from './AlgorandSigner.js';
import { AlgorandContract, applicationId } from './AlgorandContract.js';
import type { IAppRunner, Sent } from './AlgorandContract.js';
import * as AppCalls from './AppCalls.js';
import * as AppPrograms from './AppPrograms.js';
import * as Arc4Methods from './Arc4Methods.js';
import type { Arc4Function, CallArgument } from './Arc4Methods.js';
import * as Assets from './Assets.js';
import type { AvmRequest } from './AvmRequest.js';
import * as Results from './Results.js';
import * as Simulation from './Simulation.js';
import * as Submission from './Submission.js';
import * as Transactions from './Transactions.js';

const DEFAULTS: NetworkDefaults = { chainId: 'mainnet-v1.0', chainName: 'Algorand', currency: { name: 'Algo', symbol: 'ALGO', decimals: 6 } };

/**
 * Algorand through algod: deploys and calls ARC-4 applications, creates Algorand Standard Assets and reads transactions.
 * Every write is simulated first, so the resources the application reads and the fees of its inner transactions are filled in,
 * and a call the application would reject is never sent. Mirrors AlgorandChain in .NET with the same JSON output.
 */
export class AlgorandChain implements IWeb3Service {
  readonly kind = 'avm';
  readonly provider: Readonly<IAlgorandNetwork>;
  readonly #options: ResolvedOptions;
  readonly #algod: AlgodConnector;
  readonly #writes = new WriteQueue();

  constructor(input: IAvmProvider, options?: Web3ServiceOptions) {
    const network = Configuration.resolveNetwork(input, DEFAULTS);
    this.provider = Object.freeze({ ...network, kind: 'avm', chainId: Configuration.requireChainName(input.chainId, DEFAULTS.chainId) });
    this.#options = Configuration.resolveOptions(options);
    this.#algod = new AlgodConnector(this.provider, this.#options.timeoutMs);
  }

  /** The genesis ID of the network, e.g. mainnet-v1.0. */
  get chainId(): string {
    return this.provider.chainId;
  }

  /** An application by its ID: lists its ARC-4 methods and calls them with named parameters. */
  getContract(abi: Abi, address: string): AlgorandContract {
    return new AlgorandContract(abi, address, this.#runner());
  }

  /** The last round and the minimum fee per transaction in microAlgos. */
  async check(): Promise<ICheckResult> {
    return this.#algod.use(async session => ({ blockNumber: String(session.params.firstValid), gasPrice: String(session.params.minFee) }));
  }

  async callContractMethod<T = ContractResult>(method: string, abi: Abi, address: string, parameters: readonly unknown[] = []): Promise<T> {
    const fn = ContractMethods.find(Arc4Methods.describe(abi), method, {});
    return this.#read(applicationId(address), fn, Arc4Methods.toArguments(fn, Arc4Methods.toParameters(fn, parameters))) as Promise<T>;
  }

  /** Creates the application; options.value then funds its account, e.g. for the boxes it stores. */
  async deploy(
    abi: Abi, bytecode: Bytecode, constructorArguments: readonly unknown[] = [], onChainEvent?: ITransaction, options: TransactionOptions = {},
  ): Promise<ITransaction> {
    const { value, ...callOptions } = options;
    const prepare = async (session: AlgodSession) => AppCalls.request(await AppPrograms.creationCall(session, abi, bytecode, constructorArguments), callOptions);
    const deployed = await this.#write(ChainEvents.named(onChainEvent, 'deploy'), prepare);
    if (value !== undefined && Integers.parse(value, 'value').gt(0)) await this.#fund(deployed.transaction.createdAppId!, value);
    return deployed.result;
  }

  /** Creates an Algorand Standard Asset; an NFT has totalSupply 1 and decimals 0. */
  async createToken(token: ITokenDefinition, onChainEvent?: ITransaction, options?: TransactionOptions): Promise<ITokenCreation> {
    const { result, transaction } = await this.#write(ChainEvents.named(onChainEvent, 'createToken'), () => Assets.creation(token, options));
    return { standard: 'ASA', tokenId: String(transaction.createdAssetId), transaction: result };
  }

  async getTokenInfo(tokenId: ChainId): Promise<ITokenInfo> {
    return this.#algod.use(session => Assets.info(session, tokenId));
  }

  async getTransactionReceipt(transactionHash: string, onChainEvent?: ITransaction): Promise<ITransaction> {
    return this.#algod.use(async session => {
      const transaction = await Transactions.get(session, transactionHash);
      if (!transaction.confirmedRound) throw transactionNotFound(transactionHash);
      return Results.fromConfirmed(session, transaction, { ...onChainEvent }, this.provider);
    });
  }

  async getTransaction(transactionHash: string, abi?: Abi): Promise<ITransactionDetails> {
    const functions = abi ? Arc4Methods.describe(abi) : [];
    return this.#algod.use(async session => Results.details(session, await Transactions.get(session, transactionHash), functions, this.provider));
  }

  #runner(): IAppRunner {
    return {
      read: (appId, fn, args) => this.#read(appId, fn, args),
      send: (appId, fn, args, name, options) => this.#write({ name }, () => AppCalls.request({ appId, fn, args }, options)),
      getTransaction: (transactionHash, abi) => this.getTransaction(transactionHash, abi),
    };
  }

  /**
   * Reads are simulated without a fee, from the signer's account when there is one, so they are free and need no funded account.
   * Inner transactions of a read are paid by the application account.
   */
  #read(appId: bigint, fn: Arc4Function, args: readonly CallArgument[]): Promise<ContractResult> {
    return this.#algod.use(async session => {
      const request = AppCalls.request({ appId, fn, args });
      const group = request.build(this.#readSender(appId), session.params, { fee: 0n });
      const simulated = await Simulation.run(session, group);
      if (simulated.transactionFailed) throw Simulation.rejected(group.at(-1)!.txID(), request.explain(simulated.failure!));
      return Arc4Methods.returnValue(fn, Transactions.bytesOf(simulated.result.logs));
    });
  }

  #readSender(appId: bigint): string {
    try {
      return this.#signer().address;
    } catch {
      return getApplicationAddress(appId).toString();
    }
  }

  #fund(appId: bigint, value: ChainId): Promise<Sent> {
    return this.#write({ name: 'fund' }, () => AppCalls.fund(appId, BigInt(Integers.parse(value, 'value').toFixed(0))));
  }

  /** Writes are queued per instance, so the groups of concurrent calls never share validity windows and signer state. */
  #write(onChainEvent: ITransaction, prepare: (session: AlgodSession) => AvmRequest | Promise<AvmRequest>): Promise<Sent> {
    return this.#writes.run(() => this.#algod.use(async session => this.#send(session, await prepare(session), onChainEvent)));
  }

  /** Simulates, signs and sends the group, waits until it is confirmed and describes the result. */
  async #send(session: AlgodSession, request: AvmRequest, onChainEvent: ITransaction): Promise<Sent> {
    onChainEvent.requestedAt = SharedResults.requestedAt(onChainEvent.requestedAt);
    const signer = this.#signer();
    const group = await Simulation.plan(session, request, signer.address);
    const transactionId = await Submission.send(session, group, signer);
    const confirmed = await Submission.wait(session, transactionId, this.#options.transactionTimeoutMs, this.#options.pollingIntervalMs);
    return this.#describeConfirmed(session, Transactions.fromPending(transactionId, confirmed), onChainEvent);
  }

  #signer(): AvmSigner {
    return AlgorandSigner.create(this.#options.signer);
  }

  /** The transaction is already confirmed, so a failure here must still hand back its ID. */
  async #describeConfirmed(session: AlgodSession, transaction: Transactions.AvmTransaction, onChainEvent: ITransaction): Promise<Sent> {
    try {
      return { result: await Results.fromConfirmed(session, transaction, onChainEvent, this.provider), transaction };
    } catch (cause) {
      throw receiptDetailsFailed(transaction.id, cause);
    }
  }
}
