import type {
  Abi, Bytecode, ChainId, ChainKind, ContractResult, ICheckResult, ITokenCreation, ITokenDefinition, ITokenInfo, ITransaction, ITransactionDetails,
  NotarizationDocument, ResolvedProvider, TransactionOptions,
} from './Models.js';
import type { Web3Contract } from './Web3Contract.js';

/**
 * What every blockchain offers through Web3Service. The provider passed to Web3Service selects the implementation:
 * IEvmProvider → EVM networks, IAvmProvider → Algorand, IBitcoinProvider → Bitcoin.
 * An operation the blockchain does not have fails with NotImplementedError (code NOT_IMPLEMENTED).
 */
export interface IWeb3Service {
  readonly kind: ChainKind;
  /** The provider configuration with the defaults applied. */
  readonly provider: Readonly<ResolvedProvider>;
  /** Hex chain ID on EVM networks (e.g. 0x539), genesis ID on Algorand (e.g. mainnet-v1.0), network name on Bitcoin. */
  readonly chainId: string;

  /** The latest block and the current fee. */
  check(): Promise<ICheckResult>;

  /** A contract at an address (an application ID on Algorand): lists its functions and calls them with named parameters. */
  getContract(abi: Abi, address: string): Web3Contract;

  /** Calls a read-only method. A single output is returned as its value, several outputs as an array. Integers become decimal strings. */
  callContractMethod<T = ContractResult>(method: string, abi: Abi, address: string, parameters?: readonly unknown[]): Promise<T>;

  /** Notarizes a document hash by calling mint(address,string,string,string) on the contract. */
  mint(abi: Abi, contractAddress: string, document: NotarizationDocument, onChainEvent?: ITransaction, options?: TransactionOptions): Promise<ITransaction>;

  /** Deploys a contract; tx.contractAddress is its address (its application ID on Algorand). */
  deploy(
    abi: Abi, bytecode: Bytecode, constructorArguments?: readonly unknown[], onChainEvent?: ITransaction, options?: TransactionOptions,
  ): Promise<ITransaction>;

  /** Creates a token without a contract, such as an Algorand Standard Asset; EVM tokens are contracts, deployed with deploy(). */
  createToken(token: ITokenDefinition, onChainEvent?: ITransaction, options?: TransactionOptions): Promise<ITokenCreation>;

  /** Data of a token created with createToken, e.g. an Algorand Standard Asset by its ID. */
  getTokenInfo(tokenId: ChainId): Promise<ITokenInfo>;

  /** The receipt of a confirmed transaction, with its block time and exact fee. */
  getTransactionReceipt(transactionHash: string, onChainEvent?: ITransaction): Promise<ITransaction>;

  /** A transaction by hash, pending or confirmed. With the contract ABI, the called function and its arguments are decoded too. */
  getTransaction(transactionHash: string, abi?: Abi): Promise<ITransactionDetails>;
}
