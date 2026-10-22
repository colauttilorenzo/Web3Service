// Converts JSON values to ARC-4 ABI values and back: integers as decimal strings, byte arrays as 0x hex, tuples as arrays.
import {
  ABIAddressType, ABIArrayDynamicType, ABIArrayStaticType, ABIBoolType, ABIByteType, ABITupleType, ABIUfixedType, ABIUintType, Address, bytesToHex,
  hexToBytes, isValidAddress,
} from 'algosdk';
import type { ABIType, ABIValue } from 'algosdk';
import type { ContractResult } from '../Models.js';
import { invalidArgument } from '../Internal/Errors.js';
import * as Integers from '../Internal/Integers.js';

const HEX_BYTES = /^0x(?:[0-9a-f]{2})*$/i;
const BYTE_BITS = 8;

/** The JSON type a caller passes: number, boolean, string or array. Byte arrays are 0x hex strings. */
export function jsonType(type: ABIType): string {
  if (isInteger(type)) return 'number';
  if (type instanceof ABIBoolType) return 'boolean';
  if (isByteArray(type) || type instanceof ABIAddressType) return 'string';
  return isArray(type) || type instanceof ABITupleType ? 'array' : 'string';
}

/** A JSON value as the ABI value algosdk encodes. Values of the wrong type fail with INVALID_ARGUMENT. */
export function toAbiValue(type: ABIType, value: unknown, name: string): ABIValue {
  if (isInteger(type)) return toInteger(value, integerBits(type), name);
  if (type instanceof ABIBoolType) return toBoolean(value, name);
  if (isByteArray(type)) return toBytes(type, value, name);
  if (isArray(type)) return toArray(value, name, staticLength(type)).map(item => toAbiValue(type.childType, item, name));
  if (type instanceof ABITupleType) return toArray(value, name, type.childTypes.length).map((item, index) => toAbiValue(type.childTypes[index]!, item, name));
  return toStringValue(type, value, name);
}

/** A decoded ABI value as JSON: integers become decimal strings, byte arrays 0x hex. */
export function toJson(type: ABIType, value: ABIValue): ContractResult {
  if (isByteArray(type)) return `0x${bytesToHex(Uint8Array.from(value as ArrayLike<number>))}`;
  if (isArray(type)) return (value as ABIValue[]).map(item => toJson(type.childType, item));
  if (type instanceof ABITupleType) return (value as ABIValue[]).map((item, index) => toJson(type.childTypes[index]!, item));
  if (typeof value === 'bigint' || typeof value === 'number') return value.toString();
  return value instanceof Address ? value.toString() : value as string | boolean;
}

/** UTF-8 text of a byte array, without the trailing zero bytes that pad fixed-size values such as arc200_name. */
export function bytesToText(bytesHex: string): string {
  return new TextDecoder().decode(hexToBytes(bytesHex.slice(2))).replace(/\0+$/, '');
}

function toInteger(value: unknown, bits: number, name: string): bigint {
  const integer = BigInt(Integers.parse(value, name).toFixed(0));
  if (integer >= 1n << BigInt(bits)) throw invalidArgument(`${name} must be below 2^${bits}.`);
  return integer;
}

function toBoolean(value: unknown, name: string): boolean {
  if (typeof value !== 'boolean') throw invalidArgument(`${name} must be true or false.`);
  return value;
}

function toBytes(type: ABIArrayStaticType | ABIArrayDynamicType, value: unknown, name: string): Uint8Array {
  if (typeof value !== 'string' || !HEX_BYTES.test(value)) throw invalidArgument(`${name} must be 0x-prefixed hex bytes.`);
  const bytes = hexToBytes(value.slice(2));
  const length = staticLength(type);
  if (length !== undefined && bytes.length !== length) throw invalidArgument(`${name} must be ${length} bytes.`);
  return bytes;
}

function toArray(value: unknown, name: string, length?: number): unknown[] {
  if (!Array.isArray(value)) throw invalidArgument(`${name} must be an array.`);
  if (length !== undefined && value.length !== length) throw invalidArgument(`${name} must have ${length} items.`);
  return value;
}

/** Strings, and addresses: 58-character Algorand addresses. */
function toStringValue(type: ABIType, value: unknown, name: string): string {
  if (typeof value !== 'string') throw invalidArgument(`${name} must be a string.`);
  if (type instanceof ABIAddressType && !isValidAddress(value)) throw invalidArgument(`${name} must be an Algorand address.`);
  return value;
}

function isInteger(type: ABIType): type is ABIUintType | ABIUfixedType | ABIByteType {
  return type instanceof ABIUintType || type instanceof ABIUfixedType || type instanceof ABIByteType;
}

function integerBits(type: ABIUintType | ABIUfixedType | ABIByteType): number {
  return type instanceof ABIByteType ? BYTE_BITS : type.bitSize;
}

function isArray(type: ABIType): type is ABIArrayStaticType | ABIArrayDynamicType {
  return type instanceof ABIArrayStaticType || type instanceof ABIArrayDynamicType;
}

function isByteArray(type: ABIType): type is ABIArrayStaticType | ABIArrayDynamicType {
  return isArray(type) && type.childType instanceof ABIByteType;
}

function staticLength(type: ABIArrayStaticType | ABIArrayDynamicType): number | undefined {
  return type instanceof ABIArrayStaticType ? type.staticLength : undefined;
}
