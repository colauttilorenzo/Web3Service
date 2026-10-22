import type { Mnemonic } from 'ethers';
import type { TransactionReceipt } from 'web3';
import * as AbiMethods from './Internal/AbiMethods.js';
import * as Configuration from './Internal/Configuration.js';
import type { ResolvedOptions } from './Internal/Configuration.js';
import * as ContractCalls from './Internal/ContractCalls.js';
import { invalidArgument, receiptDetailsFailed, transactionNotFound, transactionReverted } from './Internal/Errors.js';
import * as HdWallets from './Internal/HdWallets.js';
import * as Integers from './Internal/Integers.js';
import { KeySigner } from './Internal/KeySigner.js';
import * as Receipts from './Internal/Receipts.js';
import { RpcConnector } from './Internal/RpcConnector.js';
import * as ErcStandards from './Internal/ErcStandards.js';
import type { RpcSession } from './Internal/RpcSession.js';
import { TransactionRequest } from './Internal/TransactionRequest.js';
import type { TransactionSigner } from './Internal/TransactionRequest.js';
import * as TransactionResults from './Internal/TransactionResults.js';
import * as Transactions from './Internal/Transactions.js';
import { WalletSigner } from './Internal/WalletSigner.js';
import { WriteQueue } from './Internal/WriteQueue.js';
import type {
  Abi, ChainId, ContractResult, IEthereumProvider, IEvmProvider, ITransaction, ITransactionDetails, IWallet, NotarizationDocument,
  TransactionOptions, Web3ServiceOptions,
} from './Models.js';
import { Web3Contract } from './Web3Contract.js';
import type { IContractRunner } from './Web3Contract.js';

const MINT_SIGNATURE = 'mint(address,string,string,string)';
const BYTECODE_PATTERN = /^0x(?:[0-9a-f]{2})+$/i;

/**
 * Deploys contracts, notarizes document hashes with `mint`, and reads contracts and receipts on EVM networks.
 * Mirrors Certifie.Web3.Web3Service in .NET: same defaults, error codes and JSON output,
 * with numeric values returned as decimal integer strings. Arithmetic uses big.js.
 */
export class Web3Service {
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

  static shrinkAddress(address: string): string {
    return address.length > 10 ? `${address.slice(0, 6)}...${address.slice(-4)}` : address;
  }

  static normalizeChainId(chainId?: ChainId): string | undefined {
    return chainId === undefined ? undefined : Integers.normalizeChainId(chainId);
  }

  /** A new 12-word English BIP-39 phrase. */
  static async createMnemonic(): Promise<Mnemonic> {
    return HdWallets.createMnemonic();
  }

  static async getMnemonic(phrase: string, password = ''): Promise<Mnemonic> {
    return HdWallets.getMnemonic(phrase, password);
  }

  /** Derives the wallet at m/44'/60'/0'/0/{index}; the default index 1 matches the original service. */
  static async getWallet(mnemonic: Mnemonic, index = 1): Promise<IWallet> {
    return HdWallets.derive(mnemonic, index);
  }

  static async getPrivateKey(mnemonic: Mnemonic, index = 1): Promise<string> {
    return (await Web3Service.getWallet(mnemonic, index)).privateKey;
  }

  /** ERC standards recognized from the functions of an ABI, e.g. ['ERC-20'] or ['ERC-721', 'ERC-721 Metadata', 'ERC-165']. */
  static detectStandards(abi: Abi): string[] {
    return ErcStandards.detect(AbiMethods.describe(abi).map(fn => fn.method.signature));
  }

  /** A contract at an address: lists its functions and calls them with named parameters, e.g. contract.call('transfer', { to, amount }). */
  getContract(abi: Abi, address: string): Web3Contract {
    return new Web3Contract(abi, address, this.#runner());
  }

  async check(): Promise<{ blockNumber: string; gasPrice: string }> {
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
      const fn = ContractCalls.getFunction(session.web3, abi, address, method);
      const outputs = await session.withTimeout(fn(...parameters).call());
      return ContractCalls.outputsToJson(outputs) as T;
    });
  }

  /** Notarizes a document hash by calling mint(address,string,string,string) on the contract. */
  async mint(
    abi: Abi, contractAddress: string, document: NotarizationDocument, onChainEvent?: ITransaction, options?: TransactionOptions,
  ): Promise<ITransaction> {
    validateDocument(document);
    const args = [contractAddress, document.hash.data, document.hash.algorithm, document.metadata ?? ''];
    return this.#write(named(onChainEvent, 'mint'), session => {
      const call = ContractCalls.getFunction(session.web3, abi, contractAddress, 'mint', `The ABI must contain ${MINT_SIGNATURE}.`)(...args);
      return new TransactionRequest(call.encodeABI(), contractAddress,
        (from, value) => call.estimateGas({ from, value: value.toFixed(0) }), options);
    });
  }

  async deploy(
    abi: Abi, bytecode: string, constructorArguments: readonly unknown[] = [], onChainEvent?: ITransaction, options?: TransactionOptions,
  ): Promise<ITransaction> {
    validateBytecode(bytecode);
    return this.#write(named(onChainEvent, 'deploy'), session => {
      const deployment = new session.web3.eth.Contract(abi).deploy({ data: bytecode, arguments: [...constructorArguments] as never });
      return new TransactionRequest(deployment.encodeABI(), undefined,
        (from, value) => deployment.estimateGas({ from, value: value.toFixed(0) }), options);
    });
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
      const details = Transactions.fromRpc(await Transactions.get(session, transactionHash), abi ? AbiMethods.describe(abi) : []);
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
    abi: Abi, address: string, signature: string, args: readonly unknown[], onChainEvent: ITransaction, options?: TransactionOptions,
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
    onChainEvent.requestedAt = TransactionResults.requestedAt(onChainEvent.requestedAt);
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

function named(onChainEvent: ITransaction | undefined, name: string): ITransaction {
  return { ...onChainEvent, name: onChainEvent?.name ?? name };
}

function validateDocument(document: NotarizationDocument | undefined): void {
  if (!document?.hash?.data || !document.hash.algorithm) throw invalidArgument('A document hash and algorithm are required.');
}

function validateBytecode(bytecode: string | undefined): void {
  if (bytecode === undefined || !BYTECODE_PATTERN.test(bytecode)) throw invalidArgument('bytecode must be a non-empty 0x-prefixed hex string.');
}
