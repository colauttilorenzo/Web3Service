import type { Mnemonic } from 'ethers';
import type { ContractAbi, Web3 } from 'web3';
import type { Web3ErrorJSON } from './Web3Exception.js';

export type Abi = ContractAbi;
export type ChainId = string | number | bigint;
export type ContractResult = string | boolean | null | ContractResult[] | { [key: string]: ContractResult };

/** Gas token of the network. Decimals are informational: fees are always returned in the smallest unit. */
export interface NativeCurrency {
  name?: string;
  symbol: string;
  decimals: number;
}

/** Provider configuration; Web3Service.provider exposes it with the defaults applied and the chain ID normalized to lowercase hex. */
export interface IEthereumProvider {
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

/** Any EIP-1193 wallet, such as MetaMask's `window.ethereum`. */
export interface Eip1193Provider {
  request(args: { method: string; params?: readonly unknown[] | object }): Promise<unknown>;
}

/**
 * Decryption and secret storage belong to the application using this package.
 * In the browser, pass `wallet` so the user's wallet signs and the private key never reaches the page.
 */
export type SignerOptions =
  | { privateKey: string; mnemonic?: never; walletIndex?: never; wallet?: never; switchChain?: never }
  | { mnemonic: string | Pick<Mnemonic, 'phrase' | 'password'>; walletIndex?: number; privateKey?: never; wallet?: never; switchChain?: never }
  | {
    wallet: Eip1193Provider;
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

export interface TransactionOptions {
  gas?: ChainId;
  gasPrice?: ChainId;
  nonce?: ChainId;
  value?: ChainId;
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

export type TokenStandard = 'ERC-20' | 'ERC-721' | 'ERC-1155';

/** Token data read from the contract. Only the fields whose functions exist in the ABI are set. */
export interface ITokenInfo {
  standard: TokenStandard;
  address: string;
  name?: string;
  symbol?: string;
  decimals?: string;
  totalSupply?: string;
  /** NFTs only, when a token ID is passed to getTokenInfo. */
  tokenId?: string;
  owner?: string;
  tokenURI?: string;
}

export interface INftMint {
  standard: Exclude<TokenStandard, 'ERC-20'>;
  /** IDs of the created tokens, read from the Transfer, TransferSingle and TransferBatch events of the contract. */
  tokenIds: string[];
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
