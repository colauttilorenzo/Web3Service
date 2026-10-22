// Parsing and formatting of the non-negative integers used by JSON-RPC. Arithmetic uses big.js.
import Big from 'big.js';
import type { ChainId } from '../Models.js';
import { invalidArgument } from './Errors.js';

const INTEGER_PATTERN = /^(?:0x[0-9a-f]+|[0-9]+)$/i;
const HEX_PREFIX = /^0x/i;

/** Parses a decimal ("1337") or 0x hex ("0x539") string, a bigint or a safe integer number. */
export function parse(value: unknown, name: string): Big {
  if (!isIntegerInput(value)) throw invalidArgument(`${name} must be a non-negative integer; use strings for large values.`);
  return requireNonNegative(new Big(isHex(value) ? BigInt(value).toString() : value.toString()), name);
}

export function requireNonNegative(value: Big, name: string): Big {
  if (value.lt(0)) throw invalidArgument(`${name} must be a non-negative integer.`);
  return value;
}

export function toDecimalString(value: unknown, name = 'value'): string {
  return (value instanceof Big ? value : parse(value, name)).toFixed(0);
}

export function toOptionalDecimalString(value: unknown): string | undefined {
  return value === undefined || value === null ? undefined : toDecimalString(value);
}

/** Lowercase 0x hex without leading zeros, e.g. 0x539. BigInt is used only for the encoding. */
export function toHex(value: Big): string {
  return `0x${BigInt(value.toFixed(0)).toString(16)}`;
}

export function normalizeChainId(chainId: ChainId): string {
  return toHex(parse(chainId, 'chainId'));
}

function isIntegerInput(value: unknown): value is ChainId {
  switch (typeof value) {
    case 'bigint': return true;
    case 'number': return Number.isSafeInteger(value);
    case 'string': return INTEGER_PATTERN.test(value);
    default: return false;
  }
}

function isHex(value: ChainId): value is string {
  return typeof value === 'string' && HEX_PREFIX.test(value);
}
