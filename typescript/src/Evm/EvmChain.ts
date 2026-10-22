import type { ContractAbi, TransactionReceipt } from 'web3';
import * as AbiMethods from './AbiMethods.js';
import * as Configuration from '../Internal/Configuration.js';
import type { ResolvedOptions } from '../Internal/Configuration.js';
import * as ContractCalls from './ContractCalls.js';
import { invalidArgument, notImplemented, receiptDetailsFailed, transactionNotFound, transactionReverted } from '../Internal/Errors.js';
import * as Integers from '../Internal/Integers.js';
import * as ChainEvents from '../Internal/ChainEvents.js';
import * as SharedResults from '../Internal/SharedResults.js';
import type { IWeb3Service } from '../IWeb3Service.js';
import { KeySigner } from './KeySigner.js';
import * as Receipts from './Receipts.js';
import { RpcConnector } from './RpcConnector.js';
import type { RpcSession } from './RpcSession.js';
import { TransactionRequest } from './TransactionRequest.js';
import type { TransactionSigner } from './TransactionRequest.js';
import * as TransactionResults from './TransactionResults.js';
import * as Transactions from './Transactions.js';
import { WalletSigner } from './WalletSigner.js';
import { WriteQueue } from '../Internal/WriteQueue.js';
import type {
  Abi, Bytecode, ContractResult, ICheckResult, IEthereumProvider, IEvmProvider, ITokenCreation, ITokenInfo, ITransaction, ITransactionDetails,
  TransactionOptions, Web3ServiceOptions,
} from '../Models.js';
import { EvmContract } from './EvmContract.js';
import type { IContractRunner } from './EvmContract.js';

const BYTECODE_PATTERN = /^0x(?:[0-9a-f]{2})+$/i;
const TOKEN_HINT = 'EVM tokens are contracts: deploy one with deploy() and use getContract().mintNft() and getTokenInfo().';

/**
 * Deploys and calls contracts, and reads transactions and receipts on EVM networks.
 * Mirrors EvmChain in .NET: same defaults, error codes and JSON output,
 * with numeric values returned as decimal integer strings. Arithmetic uses big.js.
 */
export class EvmChain implements IWeb3Service {
  readonly kind = 'evm';
  readonly provider: Readonly<IEthereumProvider>;
  readonly #options: ResolvedOptions;
  readonly #rpc: RpcConnector;
  readonly #writes = new WriteQueue();

  /** Initializes from a node URI or a provider configuration; omitted fields default to Ethereum (0x1, Ether, ETH, 18 decimals). */
  constructor(input: IEvmProvider | string, options?: Web3ServiceOptions) {
    this.provider = Configuration.resolveProvider(input);
    this.#options = Configuration.resolveOptions(options);
    this.#rpc = new RpcConnector(this.provider, this.#options.timeoutMs, this.#options.web3Factory);
  }

  /** Normalized lowercase hex chain ID, e.g. 0x539. */
  get chainId(): string {
    return String(this.provider.chainId);
  }

  /** A contract at an address: lists its functions and calls them with named parameters, e.g. contract.call('transfer', { to, amount }). */
  getContract(abi: Abi, address: string): EvmContract {
    return new EvmContract(AbiMethods.requireEvmAbi(abi), address, this.#runner());
  }

  async check(): Promise<ICheckResult> {
    return this.#rpc.use(async session => {
      const blockNumber = await session.quantity(session.web3.eth.getBlockNumber());
      const gasPrice = await session.quantity(session.web3.eth.getGasPrice());
      return { blockNumber: Integers.toDecimalString(blockNumber), gasPrice: Integers.toDecimalString(gasPrice) };
    });
  }

  /**
   * Calls a read-only method. A single output is returned as its value, several outputs as an array.
   * Integers become decimal strings.
   */
  async callContractMethod<T = ContractResult>(method: string, abi: Abi, address: string, parameters: readonly unknown[] = []): Promise<T> {
    return this.#rpc.use(async session => {
      const fn = ContractCalls.getFunction(session.web3, AbiMethods.requireEvmAbi(abi), address, method);
      const outputs = await session.withTimeout(fn(...parameters).call());
      return ContractCalls.outputsToJson(outputs) as T;
    });
  }

  async deploy(
    abi: Abi, bytecode: Bytecode, constructorArguments: readonly unknown[] = [], onChainEvent?: ITransaction, options?: TransactionOptions,
  ): Promise<ITransaction> {
    const data = validateBytecode(bytecode);
    const evmAbi = AbiMethods.requireEvmAbi(abi);
    return this.#write(ChainEvents.named(onChainEvent, 'deploy'), session => {
      const deployment = new session.web3.eth.Contract(evmAbi).deploy({ data, arguments: [...constructorArguments] as never });
      return new TransactionRequest(deployment.encodeABI(), undefined,
        (from, value) => deployment.estimateGas({ from, value: value.toFixed(0) }), options);
    });
  }

  async createToken(): Promise<ITokenCreation> {
    throw notImplemented('createToken', 'EVM networks', TOKEN_HINT);
  }

  async getTokenInfo(): Promise<ITokenInfo> {
    throw notImplemented('getTokenInfo', 'EVM networks', TOKEN_HINT);
  }

  async getTransactionReceipt(transactionHash: string, onChainEvent?: ITransaction): Promise<ITransaction> {
    return this.#rpc.use(async session => {
      const receipt = await Receipts.get(session, transactionHash);
      if (!receipt) throw transactionNotFound(transactionHash);
      return TransactionResults.fromReceipt(session, receipt, { ...onChainEvent }, this.provider);
    });
  }

  /** A transaction by hash, pending or mined. With the contract ABI, the called function and its arguments are decoded too. */
  async getTransaction(transactionHash: string, abi?: Abi): Promise<ITransactionDetails> {
    return this.#rpc.use(async session => {
      const functions = abi ? AbiMethods.describe(AbiMethods.requireEvmAbi(abi)) : [];
      const details = Transactions.fromRpc(await Transactions.get(session, transactionHash), functions);
      return TransactionResults.addMinedDetails(session, details, this.provider);
    });
  }

  #runner(): IContractRunner {
    return {
      read: (abi, address, signature, args) => this.callContractMethod(signature, abi, address, args),
      send: (abi, address, signature, args, name, options) => this.#transact(abi, address, signature, args, { name }, options),
      getTransaction: (transactionHash, abi) => this.getTransaction(transactionHash, abi),
    };
  }

  /** Sends a call to any state-changing contract function. */
  #transact(
    abi: ContractAbi, address: string, signature: string, args: readonly unknown[], onChainEvent: ITransaction, options?: TransactionOptions,
  ): Promise<ITransaction> {
    return this.#write(onChainEvent, session => {
      const call = ContractCalls.getFunction(session.web3, abi, address, signature)(...args);
      return new TransactionRequest(call.encodeABI(), address, (from, value) => call.estimateGas({ from, value: value.toFixed(0) }), options);
    });
  }

  /** Writes are queued per instance, so concurrent calls get consecutive nonces. */
  #write(onChainEvent: ITransaction, prepare: (session: RpcSession) => TransactionRequest): Promise<ITransaction> {
    return this.#writes.run(() => this.#rpc.use(session => this.#send(session, prepare(session), onChainEvent)));
  }

  /** Signs and broadcasts the request, waits until it is mined and describes the result. */
  async #send(session: RpcSession, request: TransactionRequest, onChainEvent: ITransaction): Promise<ITransaction> {
    onChainEvent.requestedAt = SharedResults.requestedAt(onChainEvent.requestedAt);
    const transactionHash = await this.#signer().send(session, request);
    const receipt = await Receipts.wait(session, transactionHash, this.#options.transactionTimeoutMs, this.#options.pollingIntervalMs);
    if (Integers.toDecimalString(receipt.status ?? 1) === '0') throw transactionReverted(transactionHash);
    return this.#describeMined(session, receipt, onChainEvent);
  }

  /** The user's wallet signs in the browser; otherwise a private key or mnemonic signs locally. */
  #signer(): TransactionSigner {
    return this.#options.signer?.wallet
      ? WalletSigner.create(this.#options, this.provider)
      : KeySigner.create(this.#options, this.chainId);
  }

  /** The transaction is already mined, so a failure here must still hand back its hash. */
  async #describeMined(session: RpcSession, receipt: TransactionReceipt, onChainEvent: ITransaction): Promise<ITransaction> {
    try {
      return await TransactionResults.fromReceipt(session, receipt, onChainEvent, this.provider);
    } catch (cause) {
      throw receiptDetailsFailed(String(receipt.transactionHash), cause);
    }
  }
}

function validateBytecode(bytecode: Bytecode | undefined): string {
  if (typeof bytecode !== 'string' || !BYTECODE_PATTERN.test(bytecode)) throw invalidArgument('bytecode must be a non-empty 0x-prefixed hex string.');
  return bytecode;
}
