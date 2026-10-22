// Describes the methods of an ARC-4 contract, and turns a JSON object of named parameters into ARC-4 arguments in method order.
import { ABIMethod, ABIReferenceType, ABITransactionType, ABITupleType, ABIType, bytesToHex } from 'algosdk';
import type { ABIArgumentType, ABIValue } from 'algosdk';
import type { Abi, ContractParameters, ContractResult, IArc4Contract, IArc4Method, IContractMethod, IDecodedCall, MethodKind, StateMutability } from '../Models.js';
import * as ContractMethods from '../Internal/ContractMethods.js';
import type { DescribedMethod } from '../Internal/ContractMethods.js';
import { invalidArgument, notSupported } from '../Internal/Errors.js';
import * as Integers from '../Internal/Integers.js';
import * as Arc4Values from './Arc4Values.js';
import * as ProgramErrors from './ProgramErrors.js';
import type { ErrorMessages } from './ProgramErrors.js';

/** A method of the contract: its public description, its ARC-4 definition, its 4-byte selector as 0x hex and the contract's error messages. */
export interface Arc4Function extends DescribedMethod {
  readonly abiMethod: ABIMethod;
  readonly selector: string;
  readonly spec: IArc4Method;
  readonly errors: ErrorMessages;
}

/** A method argument ready to encode: an ABI value, a reference to an account, asset or application, or the payment of a payable method. */
export type CallArgument =
  | { readonly kind: 'value'; readonly type: ABIType; readonly value: ABIValue }
  | { readonly kind: 'account'; readonly value: string }
  | { readonly kind: 'asset' | 'application'; readonly value: bigint }
  | { readonly kind: 'payment' };

const STATE_MUTABILITY: Readonly<Record<MethodKind, StateMutability>> = { read: 'view', write: 'nonpayable', payable: 'payable' };
/** ARC-4 passes at most 15 arguments; from the 15th on they are packed into one tuple. */
const MAX_APP_ARGUMENTS = 15;
const RETURN_PREFIX = '151f7c75';

/** The methods of an ARC-4 contract description; ARC-4 and ARC-56 list them at the top level, ARC-32 under contract. */
export function requireArc4(abi: Abi): readonly IArc4Method[] {
  if (Array.isArray(abi)) throw invalidArgument('An Algorand contract must be described by ARC-4, ARC-32 or ARC-56 JSON, not by an EVM ABI.');
  const contract = abi as IArc4Contract;
  const methods = contract?.methods ?? contract?.contract?.methods;
  if (!Array.isArray(methods)) throw invalidArgument('The contract description has no methods.');
  return methods;
}

export function describe(abi: Abi): Arc4Function[] {
  const errors = ProgramErrors.of(abi as IArc4Contract);
  return requireArc4(abi).map(spec => toFunction(spec, errors));
}

/** The arguments in method order. Unknown or missing parameters and values of the wrong type fail with INVALID_ARGUMENT. */
export function toArguments(fn: Arc4Function, parameters: ContractParameters): CallArgument[] {
  ContractMethods.rejectUnknown(fn, parameters);
  return fn.abiMethod.args.map((arg, index) => toArgument(arg.type, parameters, argumentName(arg.name, index)));
}

/** Positional arguments, as callContractMethod and deploy receive them, mapped to the method's parameter names. */
export function toParameters(fn: Arc4Function, values: readonly unknown[]): ContractParameters {
  const names = Object.keys(fn.method.parameters);
  if (values.length !== names.length) throw invalidArgument(`${fn.method.signature} expects ${names.length} arguments.`);
  return Object.fromEntries(names.map((name, index) => [name, values[index]]));
}

/** A typed value to encode as an application argument; references are passed as uint8 indexes into the foreign arrays. */
export type EncodedValue = readonly [type: ABIType, value: ABIValue];

/** The application arguments: the selector, then each value; from the 15th on the values are packed into one tuple. */
export function encode(fn: Arc4Function, values: readonly EncodedValue[]): Uint8Array[] {
  const selector = fn.abiMethod.getSelector();
  if (values.length <= MAX_APP_ARGUMENTS) return [selector, ...values.map(([type, value]) => type.encode(value))];
  const [single, packed] = [values.slice(0, MAX_APP_ARGUMENTS - 1), values.slice(MAX_APP_ARGUMENTS - 1)];
  const tuple = new ABITupleType(packed.map(([type]) => type)).encode(packed.map(([, value]) => value));
  return [selector, ...single.map(([type, value]) => type.encode(value)), tuple];
}

/** Application arguments concatenated as 0x hex: the method selector followed by the encoded arguments. */
export function toHex(appArgs: readonly Uint8Array[]): string {
  return `0x${appArgs.map(bytesToHex).join('')}`;
}

/** The return value, read from the last log that starts with the ARC-4 return prefix; null for void methods. */
export function returnValue(fn: Arc4Function, logs: readonly Uint8Array[]): ContractResult {
  if (fn.abiMethod.returns.type === 'void') return null;
  const log = [...logs].reverse().find(entry => bytesToHex(entry).startsWith(RETURN_PREFIX));
  if (!log) throw notSupported(`${fn.method.signature} did not log a return value.`);
  return Arc4Values.toJson(fn.abiMethod.returns.type, fn.abiMethod.returns.type.decode(log.slice(RETURN_PREFIX.length / 2)));
}

/** The method a transaction called, with its arguments; undefined when no method of the contract has its selector. */
export function decodeCall(functions: readonly Arc4Function[], appArgs: readonly Uint8Array[]): IDecodedCall | undefined {
  const selector = appArgs[0] ? `0x${bytesToHex(appArgs[0])}` : '';
  const fn = functions.find(candidate => candidate.selector === selector);
  if (!fn) return undefined;
  const types = valueTypes(fn);
  const values = unpack(types, appArgs.slice(1));
  const parameters = Object.fromEntries(types.map(([name, type], index) => [name, Arc4Values.toJson(type, type.decode(values[index]!))]));
  return { name: fn.method.name, signature: fn.method.signature, parameters };
}

function toFunction(spec: IArc4Method, errors: ErrorMessages): Arc4Function {
  const abiMethod = new ABIMethod({ name: spec.name, desc: spec.desc, args: [...spec.args], returns: spec.returns, readonly: spec.readonly });
  return { method: describeMethod(spec, abiMethod), abiMethod, selector: `0x${bytesToHex(abiMethod.getSelector())}`, spec, errors };
}

function describeMethod(spec: IArc4Method, abiMethod: ABIMethod): IContractMethod {
  const kind = methodKind(abiMethod);
  const args = abiMethod.args.map((arg, index) => [argumentName(arg.name, index), arg.type] as const);
  return {
    name: spec.name, signature: abiMethod.getSignature(), kind, stateMutability: STATE_MUTABILITY[kind],
    parameters: Object.fromEntries(args.filter(([, type]) => !isTransaction(type)).map(([name, type]) => [name, jsonType(type)])),
    inputs: Object.fromEntries(args.map(([name, type]) => [name, type.toString()])),
    outputs: abiMethod.returns.type === 'void' ? {} : { result0: abiMethod.returns.type.toString() },
  };
}

/** Read-only methods (ARC-22) are reads; a method that takes a payment is payable. */
function methodKind(abiMethod: ABIMethod): MethodKind {
  if (abiMethod.readonly) return 'read';
  return abiMethod.args.some(arg => arg.type === ABITransactionType.pay) ? 'payable' : 'write';
}

function jsonType(type: ABIArgumentType): string {
  if (type === ABIReferenceType.account) return 'string';
  if (type === ABIReferenceType.asset || type === ABIReferenceType.application) return 'number';
  return Arc4Values.jsonType(type as ABIType);
}

function toArgument(type: ABIArgumentType, parameters: ContractParameters, name: string): CallArgument {
  if (type === ABITransactionType.pay) return { kind: 'payment' };
  if (isTransaction(type)) throw notSupported(`Methods that take a ${type} transaction cannot be called; only pay is supported.`);
  const value = ContractMethods.valueOf(parameters, name);
  if (type === ABIReferenceType.account) return { kind: 'account', value: String(Arc4Values.toAbiValue(ABIType.from('address'), value, name)) };
  if (type === ABIReferenceType.asset || type === ABIReferenceType.application) {
    return { kind: type, value: BigInt(Integers.parse(value, name).toFixed(0)) };
  }
  return { kind: 'value', type: type as ABIType, value: Arc4Values.toAbiValue(type as ABIType, value, name) };
}

/** The encoded types of the application arguments after the selector: references are uint8 indexes, transactions are not passed. */
function valueTypes(fn: Arc4Function): Array<readonly [string, ABIType]> {
  return fn.abiMethod.args.flatMap((arg, index) => {
    if (isTransaction(arg.type)) return [];
    const type = typeof arg.type === 'string' ? ABIType.from('uint8') : arg.type;
    return [[argumentName(arg.name, index), type] as const];
  });
}

/** The encoded value of each argument; from the 15th on they are fields of the last application argument. */
function unpack(types: ReadonlyArray<readonly [string, ABIType]>, appArgs: readonly Uint8Array[]): Uint8Array[] {
  if (types.length <= MAX_APP_ARGUMENTS) return [...appArgs];
  const packed = new ABITupleType(types.slice(MAX_APP_ARGUMENTS - 1).map(([, type]) => type));
  const rest = packed.decode(appArgs[MAX_APP_ARGUMENTS - 1]!).map((value, index) => packed.childTypes[index]!.encode(value));
  return [...appArgs.slice(0, MAX_APP_ARGUMENTS - 1), ...rest];
}

function argumentName(name: string | undefined, index: number): string {
  return name || `arg${index}`;
}

function isTransaction(type: ABIArgumentType): boolean {
  return typeof type === 'string' && Object.values(ABITransactionType).includes(type as ABITransactionType);
}
