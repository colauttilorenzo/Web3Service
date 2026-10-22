// Token functions shared by every blockchain: which mint function to call and which token data the contract can return.
import type { ContractParameters, ITokenInfo } from '../Models.js';
import * as ContractMethods from './ContractMethods.js';
import type { DescribedMethod } from './ContractMethods.js';
import { invalidArgument, notSupported } from './Errors.js';

/** A token field and the read-only function that returns it (a signature on EVM, a method name on Algorand). */
export type TokenRead = readonly [field: keyof ITokenInfo, method: string];

const MINT_FUNCTIONS = ['safeMint', 'mint', 'mintTo'];

/** The safeMint, mint or mintTo function whose parameter names match, in this order of preference; extra names are tried last. */
export function mintFunction<T extends DescribedMethod>(methods: readonly T[], parameters: ContractParameters, extraNames: readonly string[] = []): T {
  const names = [...MINT_FUNCTIONS, ...extraNames];
  const candidates = names.flatMap(name => methods.filter(fn => fn.method.name === name && fn.method.kind !== 'read'));
  if (candidates.length === 0) throw notSupported(`The contract has no ${names.slice(0, -1).join(', ')} or ${names.at(-1)} function.`);
  const match = candidates.find(fn => ContractMethods.hasParameters(fn, parameters));
  if (!match) throw invalidArgument(`Pass the parameters of one of ${ContractMethods.signatures(candidates)}.`);
  return match;
}

/** The reads the contract offers: contract data, plus the token's owner and URI when a token ID is given. */
export function tokenReads(contractReads: readonly TokenRead[], tokenReads: readonly TokenRead[], keys: readonly string[], withToken: boolean): TokenRead[] {
  const reads = withToken ? [...contractReads, ...tokenReads] : contractReads;
  return reads.filter(([, method]) => keys.includes(method));
}

export function assign(info: ITokenInfo, field: keyof ITokenInfo, value: string): void {
  (info as unknown as Record<string, string>)[field] = value;
}
