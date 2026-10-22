// ABI lookups and conversion of contract outputs to JSON-safe values.
import { eth } from 'web3';
import type { ContractAbi, Web3 } from 'web3';
import type { ContractResult, IDecodedCall } from '../Models.js';
import { Web3Error } from '../Web3Exception.js';
import * as AbiMethods from './AbiMethods.js';
import type { AbiFunction } from './AbiMethods.js';

/** A contract method as web3.js exposes it: called with arguments, it returns an encodable, callable object. */
export type ContractFunction = (...args: unknown[]) => {
  encodeABI(): string;
  estimateGas(options: { from: string; value?: string }): Promise<bigint>;
  call(): Promise<unknown>;
};

/** web3.js returns several outputs, and tuples, as an object with numeric keys and __length__. */
type DecodedTuple = { __length__: number; [index: number]: unknown };

/** Looks a function up in the ABI by name or signature, and fails with METHOD_NOT_FOUND when it is missing. */
export function getFunction(web3: Web3, abi: ContractAbi, address: string, method: string, missingMessage?: string): ContractFunction {
  const contract = new web3.eth.Contract(abi, address);
  const fn = contract.methods[method] as ContractFunction | undefined;
  if (!fn) throw new Web3Error('METHOD_NOT_FOUND', missingMessage ?? `Contract method ${method} is missing from the ABI.`);
  return fn;
}

/** A single output is returned as its value, several outputs as a JSON array. */
export function outputsToJson(outputs: unknown): ContractResult {
  return toJson(outputs);
}

/** The call data of a function call: its selector followed by the encoded arguments. */
export function encode(fn: AbiFunction, args: readonly unknown[]): string {
  return eth.abi.encodeFunctionCall({ ...fn.fragment, type: 'function' } as never, [...args] as never);
}

/** The function a transaction input calls, with its arguments; undefined when no function of the ABI has its selector. */
export function decodeCall(functions: readonly AbiFunction[], input: string): IDecodedCall | undefined {
  const fn = functions.find(candidate => input.toLowerCase().startsWith(candidate.selector));
  if (!fn) return undefined;
  const inputs = AbiMethods.inputsOf(fn.fragment);
  const values = eth.abi.decodeParameters([...inputs] as never, `0x${input.slice(fn.selector.length)}`);
  const parameters = Object.fromEntries(inputs.map((parameter, index) => [AbiMethods.parameterName(parameter, index), toJson(values[index])]));
  return { name: fn.method.name, signature: fn.method.signature, parameters };
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
