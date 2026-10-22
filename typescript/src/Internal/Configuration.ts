// Applies the defaults of each blockchain to the service configuration and validates it once, at construction.
import type { Web3 } from 'web3';
import type Big from 'big.js';
import type { IEthereumProvider, IEvmProvider, NativeCurrency, SignerOptions, Web3ServiceOptions } from '../Models.js';
import { invalidArgument } from './Errors.js';
import * as Integers from './Integers.js';

/** Chain ID, chain name and native currency used when the provider omits them. */
export interface NetworkDefaults {
  readonly chainId: string;
  readonly chainName: string;
  readonly currency: Readonly<Required<NativeCurrency>>;
}

/** The fields every provider input shares. */
interface ProviderFields {
  rpcURI: string;
  rpcPublicURI?: string;
  chainId?: unknown;
  chainName?: string;
  nativeCurrency?: Partial<NativeCurrency>;
}

/** A provider input with the shared defaults applied. */
type WithDefaults<T> = T & { chainName: string; nativeCurrency: Readonly<Required<NativeCurrency>> };

const EVM_DEFAULTS: NetworkDefaults = { chainId: '0x1', chainName: 'Ethereum', currency: { name: 'Ether', symbol: 'ETH', decimals: 18 } };
const EVM_SCHEMES = ['http:', 'https:', 'ws:', 'wss:'];
const HTTP_SCHEMES = ['http:', 'https:'];
const MAX_CURRENCY_DECIMALS = 255;
const DEFAULT_TIMEOUT_MS = 30_000;
const DEFAULT_TRANSACTION_TIMEOUT_MS = 120_000;
const DEFAULT_POLLING_INTERVAL_MS = 1_000;
const DEFAULT_GAS_BUFFER = 10_000;
/** Largest delay accepted by setTimeout, the same limit as int.MaxValue in .NET. */
const MAX_TIMEOUT_MS = 2_147_483_647;

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
  const config = resolveNetwork(typeof input === 'string' ? { rpcURI: input } : input, EVM_DEFAULTS, EVM_SCHEMES);
  return Object.freeze({ ...config, kind: 'evm', chainId: Integers.normalizeChainId(config.chainId ?? EVM_DEFAULTS.chainId) });
}

/** Validates the endpoints and the native currency, and applies the chain name and currency defaults. HTTP(S) only, unless schemes says otherwise. */
export function resolveNetwork<T extends ProviderFields>(config: T, defaults: NetworkDefaults, schemes = HTTP_SCHEMES): WithDefaults<T> {
  if (!config?.rpcURI) throw invalidArgument('An rpcURI is required.');
  const nativeCurrency = resolveCurrency(config.nativeCurrency, defaults.currency);
  endpoints(config.rpcURI, config.rpcPublicURI).forEach(uri => validateEndpoint(uri, schemes));
  return { ...config, chainName: config.chainName ?? defaults.chainName, nativeCurrency };
}

/** A network name or genesis ID: the configured one, or the default. */
export function requireChainName(chainId: unknown, fallback: string): string {
  const value = chainId ?? fallback;
  if (typeof value !== 'string' || value.trim() === '') throw invalidArgument('chainId must be a non-empty string.');
  return value;
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

function resolveCurrency(currency: Partial<NativeCurrency> | undefined, defaults: Readonly<Required<NativeCurrency>>): Readonly<Required<NativeCurrency>> {
  const symbol = currency?.symbol ?? defaults.symbol;
  const decimals = currency?.decimals ?? defaults.decimals;
  if (symbol.length === 0) throw invalidArgument('nativeCurrency.symbol must not be empty.');
  if (!Number.isInteger(decimals) || decimals < 0 || decimals > MAX_CURRENCY_DECIMALS) {
    throw invalidArgument(`nativeCurrency.decimals must be an integer between 0 and ${MAX_CURRENCY_DECIMALS}.`);
  }
  return Object.freeze({ name: currency?.name ?? defaults.name, symbol, decimals });
}

function validateEndpoint(uri: string, schemes: readonly string[]): void {
  const allowed = schemes.includes('ws:') ? 'HTTP(S) or WS(S)' : 'HTTP(S)';
  const scheme = schemeOf(uri);
  if (scheme === undefined) throw invalidArgument(`RPC endpoints must be valid ${allowed} URLs.`);
  if (!schemes.includes(scheme)) throw invalidArgument(`RPC endpoints must use ${allowed}.`);
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
