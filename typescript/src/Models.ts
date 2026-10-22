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
