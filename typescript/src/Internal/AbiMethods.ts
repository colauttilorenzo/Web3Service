// Describes the functions of an ABI, and turns a JSON object of named parameters into validated arguments in ABI order.
import { eth } from 'web3';
import type { Abi, ContractParameters, IContractMethod, MethodKind, StateMutability } from '../Models.js';
import { invalidArgument, methodNotFound } from './Errors.js';
import * as Integers from './Integers.js';

/** A parameter of an ABI function; tuples list their fields in components. */
export interface AbiParameter {
  readonly name?: string;
  readonly type: string;
  readonly components?: readonly AbiParameter[];
}

/** An ABI entry. Old compilers set constant and payable instead of stateMutability. */
export interface AbiFragment {
  readonly type?: string;
  readonly name?: string;
  readonly inputs?: readonly AbiParameter[];
  readonly outputs?: readonly AbiParameter[];
  readonly stateMutability?: StateMutability;
  readonly constant?: boolean;
  readonly payable?: boolean;
}

/** A function of the ABI: its public description, its 4-byte selector and its ABI entry. */
export interface AbiFunction {
  readonly method: IContractMethod;
  readonly selector: string;
  readonly fragment: AbiFragment;
}

const KINDS: Readonly<Record<StateMutability, MethodKind>> = { pure: 'read', view: 'read', nonpayable: 'write', payable: 'payable' };
const ARRAY_SUFFIX = /\[\d*\]$/;
const INTEGER_TYPE = /^u?int\d*$/;
const INTEGER_ARGUMENT = /^-?(?:0x[0-9a-f]+|[0-9]+)$/i;
const ADDRESS = /^0x[0-9a-f]{40}$/i;
const HEX_BYTES = /^0x(?:[0-9a-f]{2})*$/i;

export function describe(abi: Abi): AbiFunction[] {
  const fragments = abi as unknown as readonly AbiFragment[];
  return fragments.filter(fragment => (fragment.type ?? 'function') === 'function').map(toFunction);
}

/** Finds a function by name or signature; a name shared by several functions is resolved by the parameter names. */
export function find(functions: readonly AbiFunction[], method: string, parameters: ContractParameters): AbiFunction {
  const candidates = functions.filter(fn => fn.method.signature === method || fn.method.name === method);
  if (candidates.length === 0) throw methodNotFound(method);
  const matching = candidates.length === 1 ? candidates : candidates.filter(fn => hasParameters(fn, parameters));
  if (matching.length === 1) return matching[0]!;
  throw invalidArgument(`Several functions are named ${method}; call one of ${signatures(candidates)}.`);
}

/** True when the parameter names are exactly those of the function. */
export function hasParameters(fn: AbiFunction, parameters: ContractParameters): boolean {
  const names = Object.keys(fn.method.parameters);
  const given = Object.keys(parameters);
  return names.length === given.length && given.every(name => names.includes(name));
}

export function signatures(functions: readonly AbiFunction[]): string {
  return functions.map(fn => fn.method.signature).join(', ');
}

/** The arguments in ABI order. Unknown or missing parameters and values of the wrong type fail with INVALID_ARGUMENT. */
export function toArguments(fn: AbiFunction, parameters: ContractParameters): unknown[] {
  const unknown = Object.keys(parameters).find(name => !hasOwn(fn.method.parameters, name));
  if (unknown !== undefined) throw invalidArgument(`${fn.method.signature} has no parameter ${unknown}.`);
  return inputsOf(fn.fragment).map((input, index) => convert(input, valueOf(parameters, parameterName(input, index))));
}

/** The name of a parameter, or arg0, arg1, … (result0, result1, … for outputs) when the ABI leaves it unnamed. */
export function parameterName(parameter: AbiParameter, index: number, prefix = 'arg'): string {
  return parameter.name || `${prefix}${index}`;
}

export function inputsOf(fragment: AbiFragment): readonly AbiParameter[] {
  return fragment.inputs ?? [];
}

function toFunction(fragment: AbiFragment): AbiFunction {
  const signature = `${fragment.name ?? ''}(${inputsOf(fragment).map(canonicalType).join(',')})`;
  return { method: describeMethod(fragment, signature), selector: eth.abi.encodeFunctionSignature(signature), fragment };
}

function describeMethod(fragment: AbiFragment, signature: string): IContractMethod {
  const stateMutability = stateMutabilityOf(fragment);
  return {
    name: fragment.name ?? '', signature, kind: KINDS[stateMutability], stateMutability,
    parameters: namedTypes(inputsOf(fragment), 'arg', parameter => jsonType(parameter.type)),
    inputs: namedTypes(inputsOf(fragment), 'arg', canonicalType),
    outputs: namedTypes(fragment.outputs ?? [], 'result', canonicalType),
  };
}

function stateMutabilityOf(fragment: AbiFragment): StateMutability {
  if (fragment.stateMutability) return fragment.stateMutability;
  if (fragment.constant) return 'view';
  return fragment.payable ? 'payable' : 'nonpayable';
}

function namedTypes(parameters: readonly AbiParameter[], prefix: string, typeOf: (parameter: AbiParameter) => string): Record<string, string> {
  return Object.fromEntries(parameters.map((parameter, index) => [parameterName(parameter, index, prefix), typeOf(parameter)]));
}

/** The type as signatures spell it: a tuple becomes (type1,type2), followed by its array suffix. */
function canonicalType(parameter: AbiParameter): string {
  if (!parameter.type.startsWith('tuple')) return parameter.type;
  return `(${(parameter.components ?? []).map(canonicalType).join(',')})${parameter.type.slice('tuple'.length)}`;
}

/** The JSON type a caller passes: number, boolean, string, array or object. */
function jsonType(type: string): string {
  if (ARRAY_SUFFIX.test(type)) return 'array';
  if (type === 'tuple') return 'object';
  if (type === 'bool') return 'boolean';
  return INTEGER_TYPE.test(type) ? 'number' : 'string';
}

function convert(parameter: AbiParameter, value: unknown, name = parameter.name || 'value'): unknown {
  if (ARRAY_SUFFIX.test(parameter.type)) return convertArray(parameter, value, name);
  if (parameter.type === 'tuple') return convertTuple(parameter, value, name);
  if (INTEGER_TYPE.test(parameter.type)) return convertInteger(parameter.type, value, name);
  if (parameter.type !== 'bool') return convertText(parameter.type, value, name);
  if (typeof value !== 'boolean') throw invalidArgument(`${name} must be true or false.`);
  return value;
}

function convertArray(parameter: AbiParameter, value: unknown, name: string): unknown[] {
  if (!Array.isArray(value)) throw invalidArgument(`${name} must be an array.`);
  const element = { ...parameter, type: parameter.type.replace(ARRAY_SUFFIX, '') };
  return value.map(item => convert(element, item, name));
}

/** Tuples are passed as objects with one property per field. */
function convertTuple(parameter: AbiParameter, value: unknown, name: string): unknown[] {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) throw invalidArgument(`${name} must be an object.`);
  return (parameter.components ?? []).map((component, index) => {
    const field = parameterName(component, index);
    return convert(component, valueOf(value as ContractParameters, field, `${name}.${field}`), `${name}.${field}`);
  });
}

/** A decimal string; negative values only for signed types. */
function convertInteger(type: string, value: unknown, name: string): string {
  const text = typeof value === 'bigint' || (typeof value === 'number' && Number.isSafeInteger(value)) ? String(value) : value;
  if (typeof text !== 'string' || !INTEGER_ARGUMENT.test(text)) throw invalidArgument(`${name} must be an integer; use a string for large values.`);
  if (!text.startsWith('-')) return Integers.toDecimalString(text, name);
  if (type.startsWith('u')) throw invalidArgument(`${name} must not be negative.`);
  return `-${Integers.toDecimalString(text.slice(1), name)}`;
}

/** Strings, addresses and bytes. Addresses are lowercased, so any checksum casing is accepted. */
function convertText(type: string, value: unknown, name: string): string {
  if (typeof value !== 'string') throw invalidArgument(`${name} must be a string.`);
  if (type === 'address' && !ADDRESS.test(value)) throw invalidArgument(`${name} must be an address: 0x followed by 40 hex digits.`);
  if (type.startsWith('bytes') && !HEX_BYTES.test(value)) throw invalidArgument(`${name} must be 0x-prefixed hex bytes.`);
  return type === 'address' ? value.toLowerCase() : value;
}

function valueOf(values: ContractParameters, name: string, label = name): unknown {
  if (!hasOwn(values, name) || values[name] === undefined) throw invalidArgument(`The parameter ${label} is required.`);
  return values[name];
}

function hasOwn(values: object, name: string): boolean {
  return Object.prototype.hasOwnProperty.call(values, name);
}
