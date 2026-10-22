import type {
  Abi, ChainId, ContractParameters, ContractResult, IContractMethod, INftMint, ITokenInfo, ITransaction, ITransactionDetails, TransactionOptions,
} from './Models.js';

/**
 * A contract at an address, described by its ABI. Lists its functions and calls them with a JSON object of named parameters,
 * so no ABI encoding is needed. Created by Web3Service.getContract; each blockchain provides its own implementation.
 */
export abstract class Web3Contract {
  readonly abi: Abi;
  /** Contract address on EVM networks, application ID on Algorand. */
  readonly address: string;

  /** @internal Use Web3Service.getContract. */
  protected constructor(abi: Abi, address: string) {
    this.abi = abi;
    this.address = address;
  }

  /** Every function of the ABI: name, kind (read, write, payable) and the parameters call() expects. */
  abstract readonly methods: readonly IContractMethod[];

  /** Standards recognized from the ABI, e.g. ['ERC-721', 'ERC-721 Metadata', 'ERC-165'] or ['ARC-200']. */
  abstract readonly standards: readonly string[];

  /** The owner() of the contract (Ownable, ERC-173); undefined when the ABI has no owner() function. */
  abstract owner(): Promise<string | undefined>;

  /**
   * Calls a function by name (or signature) with named parameters, e.g. call('transfer', { to, amount: '1000' }).
   * Read functions return their value; write and payable functions send a transaction and return it once confirmed.
   */
  abstract call<T = ContractResult | ITransaction>(method: string, parameters?: ContractParameters, options?: TransactionOptions): Promise<T>;

  /** Returns the outputs without a transaction; on a write function it is a free simulation. Integers become decimal strings. */
  abstract read<T = ContractResult>(method: string, parameters?: ContractParameters): Promise<T>;

  /** Sends a transaction, signed by the service's signer, and waits until it is confirmed. Coins for payable functions go in options.value. */
  abstract send(method: string, parameters?: ContractParameters, options?: TransactionOptions): Promise<ITransaction>;

  /** The call data of a function call, e.g. to have it signed elsewhere. */
  abstract encode(method: string, parameters?: ContractParameters): string;

  /**
   * Mints an NFT with the safeMint, mint or mintTo function of the contract, chosen by the parameter names,
   * e.g. mintNft({ to, uri: 'ipfs://…' }). Returns the IDs of the new tokens.
   */
  abstract mintNft(parameters: ContractParameters, options?: TransactionOptions): Promise<INftMint>;

  /** Name, symbol, decimals and supply of a token; with the ID of an NFT, also its owner and URI. */
  abstract getTokenInfo(tokenId?: ChainId): Promise<ITokenInfo>;

  /** A transaction by hash, with the function it called on this contract and its arguments. */
  abstract getTransaction(transactionHash: string): Promise<ITransactionDetails>;
}
