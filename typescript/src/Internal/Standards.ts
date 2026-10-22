// Recognizes token and contract standards from the functions of a contract. A standard matches when the contract has all of its functions.
import type { TokenStandard } from '../Models.js';

/** Standard, the functions it requires (signatures on EVM, method names on Algorand), and the standard it extends. */
export type Standard = readonly [id: string, functions: readonly string[], extended?: string];

/** The standards whose functions all appear among the keys, in table order. */
export function detect(table: readonly Standard[], keys: readonly string[]): string[] {
  const found: string[] = [];
  for (const [id, functions, extended] of table) {
    if (functions.every(fn => keys.includes(fn)) && (!extended || found.includes(extended))) found.push(id);
  }
  return found;
}

/** The first of the token standards among the detected ones. */
export function tokenStandard<T extends TokenStandard>(tokenStandards: readonly T[], standards: readonly string[]): T | undefined {
  return tokenStandards.find(standard => standards.includes(standard));
}
