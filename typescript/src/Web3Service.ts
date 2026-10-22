import type { Mnemonic } from 'ethers';
import * as Arc4Methods from './Algorand/Arc4Methods.js';
import * as ArcStandards from './Algorand/ArcStandards.js';
import * as Blockchains from './Internal/Blockchains.js';
import * as Integers from './Internal/Integers.js';
import * as AbiMethods from './Evm/AbiMethods.js';
import * as ErcStandards from './Evm/ErcStandards.js';
import * as HdWallets from './Evm/HdWallets.js';
import type { IWeb3Service } from './IWeb3Service.js';
import type {
  Abi, Bytecode, ChainId, ChainKind, ContractResult, ICheckResult, IEvmProvider, ITokenCreation, ITokenDefinition, ITokenInfo, ITransaction,
  ITransactionDetails, IWallet, NetworkOf, ProviderInput, TransactionOptions, Web3ServiceOptions,
} from './Models.js';
import type { Web3Contract } from './Web3Contract.js';

/**
 * Deploys and calls contracts, creates tokens, and reads transactions on any supported blockchain.
 * The provider selects the blockchain: an IEvmProvider (or a node URL) for EVM networks, an IAvmProvider for Algorand,
 * an IBitcoinProvider for Bitcoin. The API and the JSON output are the same everywhere; an operation the blockchain does not have
 * fails with NotImplementedError. Mirrors Certifie.Web3.Web3Service in .NET, with numeric values returned as decimal integer strings.
 */
export class Web3Service<P extends ProviderInput = IEvmProvider> implements IWeb3Service {
  readonly #chain: IWeb3Service;

  /** Initializes from a node URI or a provider configuration; omitted EVM fields default to Ethereum (0x1, Ether, ETH, 18 decimals). */
  constructor(input: P, options?: Web3ServiceOptions) {
    this.#chain = Blockchains.create(input, options);
  }

  get kind(): ChainKind {
    return this.#chain.kind;
  }

  /** The provider configuration with the defaults applied. */
  get provider(): Readonly<NetworkOf<P>> {
    return this.#chain.provider as Readonly<NetworkOf<P>>;
  }

  /** Normalized lowercase hex chain ID on EVM networks, e.g. 0x539; genesis ID on Algorand; network name on Bitcoin. */
  get chainId(): string {
    return this.#chain.chainId;
  }

  static shrinkAddress(address: string): string {
    return address.length > 10 ? `${address.slice(0, 6)}...${address.slice(-4)}` : address;
  }

  /** EVM chain IDs as lowercase hex, e.g. 137 → 0x89. */
  static normalizeChainId(chainId?: ChainId): string | undefined {
    return chainId === undefined ? undefined : Integers.normalizeChainId(chainId);
  }

  /** A new 12-word English BIP-39 phrase, for EVM wallets. Algorand accounts: see AlgorandWallets. */
  static async createMnemonic(): Promise<Mnemonic> {
    return HdWallets.createMnemonic();
  }

  static async getMnemonic(phrase: string, password = ''): Promise<Mnemonic> {
    return HdWallets.getMnemonic(phrase, password);
  }

  /** Derives the EVM wallet at m/44'/60'/0'/0/{index}; the default index 1 matches the original service. */
  static async getWallet(mnemonic: Mnemonic, index = 1): Promise<IWallet> {
    return HdWallets.derive(mnemonic, index);
  }

  static async getPrivateKey(mnemonic: Mnemonic, index = 1): Promise<string> {
    return (await Web3Service.getWallet(mnemonic, index)).privateKey;
  }

  /** Standards recognized from the functions of an ABI, e.g. ['ERC-20'], ['ERC-721', 'ERC-721 Metadata', 'ERC-165'] or ['ARC-200']. */
  static detectStandards(abi: Abi): string[] {
    if (!Array.isArray(abi)) return ArcStandards.detect(Arc4Methods.describe(abi).map(fn => fn.method.name));
    return ErcStandards.detect(AbiMethods.describe(abi).map(fn => fn.method.signature));
  }

  /** A contract at an address (an application ID on Algorand): lists its functions and calls them with named parameters. */
  getContract(abi: Abi, address: string): Web3Contract {
    return this.#chain.getContract(abi, address);
  }

  /** The latest block and the current fee. */
  check(): Promise<ICheckResult> {
    return this.#chain.check();
  }

  /** Calls a read-only method. A single output is returned as its value, several outputs as an array. Integers become decimal strings. */
  callContractMethod<T = ContractResult>(method: string, abi: Abi, address: string, parameters: readonly unknown[] = []): Promise<T> {
    return this.#chain.callContractMethod<T>(method, abi, address, parameters);
  }

  /** Deploys a contract; tx.contractAddress is its address (its application ID on Algorand). */
  deploy(
    abi: Abi, bytecode: Bytecode, constructorArguments: readonly unknown[] = [], onChainEvent?: ITransaction, options?: TransactionOptions,
  ): Promise<ITransaction> {
    return this.#chain.deploy(abi, bytecode, constructorArguments, onChainEvent, options);
  }

  /** Creates a token without a contract: an Algorand Standard Asset. Not available on EVM networks, where tokens are contracts. */
  createToken(token: ITokenDefinition, onChainEvent?: ITransaction, options?: TransactionOptions): Promise<ITokenCreation> {
    return this.#chain.createToken(token, onChainEvent, options);
  }

  /** Data of a token created with createToken, e.g. an Algorand Standard Asset by its ID. */
  getTokenInfo(tokenId: ChainId): Promise<ITokenInfo> {
    return this.#chain.getTokenInfo(tokenId);
  }

  /** The receipt of a confirmed transaction, with its block time and exact fee. */
  getTransactionReceipt(transactionHash: string, onChainEvent?: ITransaction): Promise<ITransaction> {
    return this.#chain.getTransactionReceipt(transactionHash, onChainEvent);
  }

  /** A transaction by hash, pending or confirmed. With the contract ABI, the called function and its arguments are decoded too. */
  getTransaction(transactionHash: string, abi?: Abi): Promise<ITransactionDetails> {
    return this.#chain.getTransaction(transactionHash, abi);
  }
}
