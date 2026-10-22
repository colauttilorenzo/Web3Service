// Finds a contract method by name or signature, whatever the blockchain: EVM ABI functions and ARC-4 methods alike.
import type { ContractParameters, IContractMethod } from '../Models.js';
import { invalidArgument, methodNotFound } from './Errors.js';

/** A contract method with its public description. */
export interface DescribedMethod {
  readonly method: IContractMethod;
}

/** Finds a method by name or signature; a name shared by several methods is resolved by the parameter names. */
export function find<T extends DescribedMethod>(methods: readonly T[], method: string, parameters: ContractParameters): T {
  const candidates = methods.filter(fn => fn.method.signature === method || fn.method.name === method);
  if (candidates.length === 0) throw methodNotFound(method);
  const matching = candidates.length === 1 ? candidates : candidates.filter(fn => hasParameters(fn, parameters));
  if (matching.length === 1) return matching[0]!;
  throw invalidArgument(`Several functions are named ${method}; call one of ${signatures(candidates)}.`);
}

/** True when the parameter names are exactly those of the method. */
export function hasParameters(fn: DescribedMethod, parameters: ContractParameters): boolean {
  const names = Object.keys(fn.method.parameters);
  const given = Object.keys(parameters);
  return names.length === given.length && given.every(name => names.includes(name));
}

export function signatures(methods: readonly DescribedMethod[]): string {
  return methods.map(fn => fn.method.signature).join(', ');
}

/** Fails when a parameter is not one of the method's. */
export function rejectUnknown(fn: DescribedMethod, parameters: ContractParameters): void {
  const unknown = Object.keys(parameters).find(name => !hasOwn(fn.method.parameters, name));
  if (unknown !== undefined) throw invalidArgument(`${fn.method.signature} has no parameter ${unknown}.`);
}

/** The value of a required parameter. */
export function valueOf(values: ContractParameters, name: string, label = name): unknown {
  if (!hasOwn(values, name) || values[name] === undefined) throw invalidArgument(`The parameter ${label} is required.`);
  return values[name];
}

export function hasOwn(values: object, name: string): boolean {
  return Object.prototype.hasOwnProperty.call(values, name);
}
