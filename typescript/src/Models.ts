import type { Mnemonic } from 'ethers';
import type { ContractAbi, Web3 } from 'web3';
import type { Web3ErrorJSON } from './Web3Exception.js';

/** An EVM JSON ABI, or an Algorand ARC-4 contract description (ARC-4, ARC-32 or ARC-56 JSON). */
export type Abi = ContractAbi | IArc4Contract;
export type ChainId = string | number | bigint;

/** The virtual machine family of a network: EVM (Ethereum, Polygon, …), AVM (Algorand) or Bitcoin. */
export type ChainKind = 'evm' | 'avm' | 'bitcoin';
export type ContractResult = string | boolean | null | ContractResult[] | { [key: string]: ContractResult };

/** Gas token of the network. Decimals are informational: fees are always returned in the smallest unit. */
export interface NativeCurrency {
  name?: string;
  symbol: string;
  decimals: number;
}

/** Provider configuration; Web3Service.provider exposes it with the defaults applied and the chain ID normalized to lowercase hex. */
export interface IEthereumProvider {
  kind?: 'evm';
  chainId: ChainId;
  rpcURI: string;
  /** Fallback endpoint, used when the primary is unavailable or on the wrong chain. */
  rpcPublicURI?: string;
  nativeCurrency: NativeCurrency;
  chainName?: string;
  blockExplorerURI?: string;
}

/** Initialization input. Only the RPC URL is required; omitted chain fields use Ethereum defaults. */
export interface IEvmProvider extends Omit<IEthereumProvider, 'chainId' | 'nativeCurrency'> {
  chainId?: ChainId;
  nativeCurrency?: Partial<NativeCurrency>;
}

/** Compatibility alias for the initialization configuration. */
export type EthereumProviderConfig = IEvmProvider;

/** Algorand (AVM) initialization input. Only the algod URL is required; omitted fields use Algorand MainNet defaults. */
export interface IAvmProvider {
  kind: 'avm';
  /** algod node URL, e.g. https://mainnet-api.algonode.cloud. */
  rpcURI: string;
  /** Fallback algod node, used when the primary is unavailable or on the wrong network. */
  rpcPublicURI?: string;
  /** algod API token, sent as X-Algo-API-Token; public nodes need none. */
  token?: string;
  /** Indexer URL, used to read transactions that algod no longer keeps. */
  indexerURI?: string;
  indexerToken?: string;
  /** Genesis ID of the network, e.g. mainnet-v1.0 or testnet-v1.0. Default: mainnet-v1.0. */
  chainId?: string;
  chainName?: string;
  nativeCurrency?: Partial<NativeCurrency>;
  blockExplorerURI?: string;
}

/** Algorand configuration with the defaults applied, as Web3Service.provider exposes it. */
export interface IAlgorandNetwork extends Omit<IAvmProvider, 'chainId' | 'chainName' | 'nativeCurrency'> {
  chainId: string;
  chainName: string;
  nativeCurrency: NativeCurrency;
}

/** Bitcoin initialization input: an Esplora REST API, such as https://mempool.space/api. Omitted fields use Bitcoin mainnet defaults. */
export interface IBitcoinProvider {
  kind: 'bitcoin';
  rpcURI: string;
  /** Fallback API, used when the primary is unavailable or on the wrong network. */
  rpcPublicURI?: string;
  /** mainnet, testnet, testnet4, signet or regtest. Default: mainnet. */
  chainId?: string;
  chainName?: string;
  nativeCurrency?: Partial<NativeCurrency>;
  blockExplorerURI?: string;
}

/** Bitcoin configuration with the defaults applied, as Web3Service.provider exposes it. */
export interface IBitcoinNetwork extends Omit<IBitcoinProvider, 'chainId' | 'chainName' | 'nativeCurrency'> {
  chainId: string;
  chainName: string;
  nativeCurrency: NativeCurrency;
}

/** What Web3Service accepts: the provider type selects the blockchain. A string is the URL of an EVM node. */
export type ProviderInput = IEvmProvider | IAvmProvider | IBitcoinProvider | string;

/** A provider configuration with the defaults applied. */
export type ResolvedProvider = IEthereumProvider | IAlgorandNetwork | IBitcoinNetwork;

/** The resolved configuration of a provider input. */
export type NetworkOf<P> = P extends IAvmProvider ? IAlgorandNetwork : P extends IBitcoinProvider ? IBitcoinNetwork : IEthereumProvider;

/** Any EIP-1193 wallet, such as MetaMask's `window.ethereum`. */
export interface Eip1193Provider {
  request(args: { method: string; params?: readonly unknown[] | object }): Promise<unknown>;
}

/**
 * An Algorand wallet, e.g. the active account of @txnlab/use-wallet: { address: activeAddress, signTransactions }.
 * It receives msgpack-encoded unsigned transactions and returns them signed.
 */
export interface IAvmWallet {
  address: string;
  signTransactions(transactions: Uint8Array[]): Promise<(Uint8Array | null)[]>;
}

/**
 * Decryption and secret storage belong to the application using this package.
 * In the browser, pass `wallet` so the user's wallet signs and the private key never reaches the page.
 * On Algorand the mnemonic is the 25-word account phrase and the private key is the 32-byte seed (0x hex) or the 64-byte secret key (base64).
 */
export type SignerOptions =
  | { privateKey: string; mnemonic?: never; walletIndex?: never; wallet?: never; switchChain?: never }
  | { mnemonic: string | Pick<Mnemonic, 'phrase' | 'password'>; walletIndex?: number; privateKey?: never; wallet?: never; switchChain?: never }
  | {
    /** An EIP-1193 wallet on EVM networks, an IAvmWallet on Algorand. */
    wallet: Eip1193Provider | IAvmWallet;
    /** Ask the wallet to switch to (or add) the configured chain. Default: true. */
    switchChain?: boolean;
    privateKey?: never; mnemonic?: never; walletIndex?: never;
  };

export interface IWallet {
  address: string;
  publicKey: string;
  privateKey: string;
  path: string | null;
  index: string;
}

export interface NotarizationDocument {
  hash: { data: string; algorithm: string };
  metadata?: string;
}

export interface ITransactionLog {
  address?: string;
  data?: string;
  topics?: string[];
  logIndex?: string;
  transactionIndex?: string;
  transactionHash?: string;
  blockHash?: string;
  blockNumber?: string;
  removed?: boolean;
}

/** All numeric RPC fields are decimal strings, including zero. */
export interface ITransactionReceipt {
  transactionHash: string;
  blockHash: string;
  blockNumber: string;
  transactionIndex: string;
  from: string;
  to?: string | null;
  contractAddress?: string | null;
  type?: string;
  status: string;
  cumulativeGasUsed: string;
  gasUsed: string;
  effectiveGasPrice?: string;
  logsBloom: string;
  logs: ITransactionLog[];
}

export interface ITransaction {
  name?: 'mint' | 'deploy' | string;
  requestedAt?: string;
  completedAt?: string;
  rejectedAt?: string;
  tx?: ITransactionReceipt;
  error?: Web3ErrorJSON;
  /** Exact gas cost in the native token's smallest unit (wei on Ethereum). */
  nativeFee?: { amount: string; symbol: string; decimals: string };
}

/** On Algorand gasPrice is the flat fee of the transaction in microAlgos, value the microAlgos paid to the application; gas and nonce do not apply. */
export interface TransactionOptions {
  gas?: ChainId;
  gasPrice?: ChainId;
  nonce?: ChainId;
  value?: ChainId;
}

/** Latest block and current fee: the gas price in wei on EVM networks, the minimum fee in microAlgos on Algorand, sat/vB on Bitcoin. */
export interface ICheckResult {
  blockNumber: string;
  gasPrice: string;
}

/** Compiled Algorand programs as base64 or 0x hex, or TEAL source compiled by the node. */
export interface IAvmPrograms {
  approval: string;
  clear: string;
}

/** EVM creation bytecode (0x hex), or the programs of an Algorand application; on Algorand '' takes them from the ARC-56 description. */
export type Bytecode = string | IAvmPrograms;

/** An ARC-4 method argument or return value. */
export interface IArc4Argument {
  type: string;
  name?: string;
  desc?: string;
}

/** A method of an ARC-4 contract; ARC-56 adds the on-completion actions. */
export interface IArc4Method {
  name: string;
  args: readonly IArc4Argument[];
  returns: IArc4Argument;
  desc?: string;
  readonly?: boolean;
  actions?: { create?: readonly string[]; call?: readonly string[] };
}

/** An Algorand contract description: ARC-4 ({ name, methods }), ARC-56, or ARC-32 (methods under contract). */
export interface IArc4Contract {
  name?: string;
  methods?: readonly IArc4Method[];
  contract?: { name?: string; methods: readonly IArc4Method[] };
  [key: string]: unknown;
}

export interface Web3ServiceOptions {
  signer?: SignerOptions;
  /** Timeout for each HTTP or WebSocket request, including provider selection. Default: 30 seconds. */
  timeoutMs?: number;
  /** Time to wait for mining after broadcast. Default: 120 seconds. */
  transactionTimeoutMs?: number;
  /** Receipt polling interval. Default: 1 second. */
  pollingIntervalMs?: number;
  /** Added to the estimated gas limit. Default: 10000. */
  gasBuffer?: ChainId;
  /** Supply an injected provider or custom Web3 configuration. Called for each operation/endpoint. */
  web3Factory?: (rpcURI: string) => Web3;
}

/** Solidity state mutability of a contract function. */
export type StateMutability = 'pure' | 'view' | 'nonpayable' | 'payable';

/** read: returns data, free, no transaction. write: changes the contract with a transaction. payable: a write that can also receive coins. */
export type MethodKind = 'read' | 'write' | 'payable';

/** A contract function as Web3Contract.methods lists it. */
export interface IContractMethod {
  name: string;
  /** Canonical signature, e.g. transfer(address,uint256); pass it to call() to pick one of several functions with the same name. */
  signature: string;
  kind: MethodKind;
  stateMutability: StateMutability;
  /** Parameter name → JSON type to pass to call(): string, number, boolean, array or object. Unnamed parameters are arg0, arg1, … */
  parameters: Record<string, string>;
  /** Parameter name → Solidity type, e.g. address or uint256. */
  inputs: Record<string, string>;
  /** Output name → Solidity type; unnamed outputs are result0, result1, … */
  outputs: Record<string, string>;
}

/**
 * Named parameters of a contract function, e.g. { to: '0x…', amount: '1000' }.
 * Integers can be numbers or decimal strings (strings for values beyond 2^53), addresses and bytes are 0x strings.
 */
export type ContractParameters = Record<string, unknown>;

/** Fungible: ERC-20, ARC-200. NFT: ERC-721, ERC-1155, ARC-72. ASA: an Algorand Standard Asset, fungible or not. */
export type TokenStandard = 'ERC-20' | 'ERC-721' | 'ERC-1155' | 'ARC-200' | 'ARC-72' | 'ASA';

/** Token data read from the contract. Only the fields whose functions exist in the ABI are set. */
export interface ITokenInfo {
  standard: TokenStandard;
  /** Contract address (EVM), application ID (Algorand contracts) or asset ID (ASA). */
  address: string;
  name?: string;
  symbol?: string;
  decimals?: string;
  totalSupply?: string;
  /** NFTs only, when a token ID is passed to getTokenInfo. */
  tokenId?: string;
  owner?: string;
  tokenURI?: string;
  /** ASA only: the account that created the asset. */
  creator?: string;
}

export interface INftMint {
  standard: Exclude<TokenStandard, 'ERC-20' | 'ARC-200'>;
  /**
   * IDs of the created tokens: from the Transfer, TransferSingle and TransferBatch events on EVM networks,
   * from the arc72_Transfer events and the assets created by the application on Algorand.
   */
  tokenIds: string[];
  transaction: ITransaction;
}

/** A token created without a contract: an Algorand Standard Asset. An NFT has totalSupply 1 and decimals 0. */
export interface ITokenDefinition {
  name: string;
  /** Unit name, at most 8 bytes. */
  symbol: string;
  totalSupply: ChainId;
  /** Default: 0. */
  decimals?: number;
  /** Metadata URL, e.g. ipfs://…#arc3 (ARC-3). */
  uri?: string;
  /** SHA-256 of the metadata, 0x hex (32 bytes). */
  metadataHash?: string;
}

export interface ITokenCreation {
  standard: 'ASA';
  tokenId: string;
  transaction: ITransaction;
}

/** The contract function a transaction called, with its arguments as JSON-safe values. */
export interface IDecodedCall {
  name: string;
  signature: string;
  parameters: Record<string, ContractResult>;
}

/** A transaction read by hash. Numeric fields are decimal strings. */
export interface ITransactionDetails {
  hash: string;
  from: string;
  /** Undefined for contract deployments. */
  to?: string;
  /** Native currency sent, in the smallest unit (wei on Ethereum). */
  value: string;
  nonce: string;
  gas: string;
  gasPrice?: string;
  maxFeePerGas?: string;
  maxPriorityFeePerGas?: string;
  input: string;
  type?: string;
  blockHash?: string;
  blockNumber?: string;
  transactionIndex?: string;
  /** True while the transaction waits to be mined. */
  pending: boolean;
  /** Block time in milliseconds, once mined. */
  timestamp?: string;
  receipt?: ITransactionReceipt;
  nativeFee?: ITransaction['nativeFee'];
  /** The called function and its arguments, when the ABI passed to getTransaction contains it. */
  method?: IDecodedCall;
}
