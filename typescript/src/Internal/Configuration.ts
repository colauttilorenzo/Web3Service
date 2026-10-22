// Applies the Ethereum defaults to the service configuration and validates it once, at construction.
import type { Web3 } from 'web3';
import type Big from 'big.js';
import type { IEthereumProvider, IEvmProvider, NativeCurrency, SignerOptions, Web3ServiceOptions } from '../Models.js';
import { invalidArgument } from './Errors.js';
import * as Integers from './Integers.js';

const DEFAULT_CHAIN_ID = '0x1';
const DEFAULT_CHAIN_NAME = 'Ethereum';
const DEFAULT_CURRENCY_NAME = 'Ether';
const DEFAULT_CURRENCY_SYMBOL = 'ETH';
const DEFAULT_CURRENCY_DECIMALS = 18;
const MAX_CURRENCY_DECIMALS = 255;
const DEFAULT_TIMEOUT_MS = 30_000;
const DEFAULT_TRANSACTION_TIMEOUT_MS = 120_000;
const DEFAULT_POLLING_INTERVAL_MS = 1_000;
const DEFAULT_GAS_BUFFER = 10_000;
/** Largest delay accepted by setTimeout, the same limit as int.MaxValue in .NET. */
const MAX_TIMEOUT_MS = 2_147_483_647;
const RPC_SCHEMES = ['http:', 'https:', 'ws:', 'wss:'];

/** Service options with the defaults applied. */
export interface ResolvedOptions {
  readonly signer?: SignerOptions;
  readonly timeoutMs: number;
  readonly transactionTimeoutMs: number;
  readonly pollingIntervalMs: number;
  readonly gasBuffer: Big;
  readonly web3Factory?: (rpcURI: string) => Web3;
}

export function resolveProvider(input: IEvmProvider | string): Readonly<IEthereumProvider> {
  const config: IEvmProvider = typeof input === 'string' ? { rpcURI: input } : input;
  if (!config?.rpcURI) throw invalidArgument('An rpcURI is required.');
  const nativeCurrency = resolveCurrency(config.nativeCurrency);
  endpoints(config.rpcURI, config.rpcPublicURI).forEach(validateEndpoint);
  const chainId = Integers.normalizeChainId(config.chainId ?? DEFAULT_CHAIN_ID);
  return Object.freeze({ ...config, chainId, chainName: config.chainName ?? DEFAULT_CHAIN_NAME, nativeCurrency });
}

/** A validated copy, so later changes to the caller's object cannot bypass validation. */
export function resolveOptions(options: Web3ServiceOptions = {}): ResolvedOptions {
  return {
    signer: options.signer ? { ...options.signer } : undefined,
    timeoutMs: requireTimeout(options.timeoutMs ?? DEFAULT_TIMEOUT_MS, 'timeoutMs'),
    transactionTimeoutMs: requireTimeout(options.transactionTimeoutMs ?? DEFAULT_TRANSACTION_TIMEOUT_MS, 'transactionTimeoutMs'),
    pollingIntervalMs: requireTimeout(options.pollingIntervalMs ?? DEFAULT_POLLING_INTERVAL_MS, 'pollingIntervalMs'),
    gasBuffer: Integers.parse(options.gasBuffer ?? DEFAULT_GAS_BUFFER, 'gasBuffer'),
    web3Factory: options.web3Factory,
  };
}

/** The configured RPC endpoints without duplicates: the primary first, then the public fallback. */
export function endpoints(rpcURI: string, rpcPublicURI?: string): string[] {
  return [...new Set([rpcURI, rpcPublicURI].filter((uri): uri is string => !!uri))];
}

function resolveCurrency(currency: Partial<NativeCurrency> | undefined): Readonly<Required<NativeCurrency>> {
  const symbol = currency?.symbol ?? DEFAULT_CURRENCY_SYMBOL;
  const decimals = currency?.decimals ?? DEFAULT_CURRENCY_DECIMALS;
  if (symbol.length === 0) throw invalidArgument('nativeCurrency.symbol must not be empty.');
  if (!Number.isInteger(decimals) || decimals < 0 || decimals > MAX_CURRENCY_DECIMALS) {
    throw invalidArgument(`nativeCurrency.decimals must be an integer between 0 and ${MAX_CURRENCY_DECIMALS}.`);
  }
  return Object.freeze({ name: currency?.name ?? DEFAULT_CURRENCY_NAME, symbol, decimals });
}

function validateEndpoint(uri: string): void {
  const scheme = schemeOf(uri);
  if (scheme === undefined) throw invalidArgument('RPC endpoints must be valid HTTP(S) or WS(S) URLs.');
  if (!RPC_SCHEMES.includes(scheme)) throw invalidArgument('RPC endpoints must use HTTP(S) or WS(S).');
}

function schemeOf(uri: string): string | undefined {
  try {
    return new URL(uri).protocol;
  } catch {
    return undefined;
  }
}

function requireTimeout(value: number, name: string): number {
  if (!Number.isSafeInteger(value) || value <= 0 || value > MAX_TIMEOUT_MS) {
    throw invalidArgument(`${name} must be positive and at most ${MAX_TIMEOUT_MS} ms.`);
  }
  return value;
}
