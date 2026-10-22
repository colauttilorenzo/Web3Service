// ABI lookups and conversion of contract outputs to JSON-safe values.
import type { Web3 } from 'web3';
import type { Abi, ContractResult } from '../Models.js';
import { Web3Error } from '../Web3Exception.js';

/** A contract method as web3.js exposes it: called with arguments, it returns an encodable, callable object. */
export type ContractFunction = (...args: unknown[]) => {
  encodeABI(): string;
  estimateGas(options: { from: string; value?: string }): Promise<bigint>;
  call(): Promise<unknown>;
};

/** web3.js returns several outputs, and tuples, as an object with numeric keys and __length__. */
type DecodedTuple = { __length__: number; [index: number]: unknown };

/** Looks a function up in the ABI and fails with METHOD_NOT_FOUND when it is missing. */
export function getFunction(web3: Web3, abi: Abi, address: string, method: string, missingMessage?: string): ContractFunction {
  const contract = new web3.eth.Contract(abi, address);
  const fn = contract.methods[method] as ContractFunction | undefined;
  if (!fn) throw new Web3Error('METHOD_NOT_FOUND', missingMessage ?? `Contract method ${method} is missing from the ABI.`);
  return fn;
}

/** A single output is returned as its value, several outputs as a JSON array. */
export function outputsToJson(outputs: unknown): ContractResult {
  return toJson(outputs);
}

/** Converts decoded values at any depth. Tuples are matched before the generic object case. */
function toJson(value: unknown): ContractResult {
  if (value === null || value === undefined) return null;
  if (typeof value === 'string' || typeof value === 'boolean') return value;
  if (Array.isArray(value)) return toJsonArray(value);
  if (isTuple(value)) return toJsonArray(Array.from({ length: value.__length__ }, (_, index) => value[index]));
  if (typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, toJson(item)]));
  return scalarToJson(value);
}

/** Integers become decimal strings, so values beyond 2^53 survive JSON. */
function scalarToJson(value: unknown): ContractResult {
  if (typeof value === 'bigint') return value.toString();
  if (typeof value === 'number' && Number.isSafeInteger(value)) return String(value);
  if (typeof value === 'number') throw new Web3Error('UNSAFE_INTEGER', 'The provider returned an unsafe numeric value.');
  return String(value);
}

function toJsonArray(values: readonly unknown[]): ContractResult[] {
  return values.map(toJson);
}

function isTuple(value: unknown): value is DecodedTuple {
  return typeof value === 'object' && value !== null && typeof (value as { __length__?: unknown }).__length__ === 'number';
}
