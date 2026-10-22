// Recognizes ARC standards from the method names of an ARC-4 contract.
import * as Standards from '../Internal/Standards.js';
import type { Standard } from '../Internal/Standards.js';

/** The token standards of Algorand applications. */
export type ArcTokenStandard = 'ARC-200' | 'ARC-72';

const STANDARDS: readonly Standard[] = [
  ['ARC-200', ['arc200_name', 'arc200_symbol', 'arc200_decimals', 'arc200_totalSupply', 'arc200_balanceOf', 'arc200_transfer',
    'arc200_transferFrom', 'arc200_approve', 'arc200_allowance']],
  ['ARC-72', ['arc72_ownerOf', 'arc72_transferFrom', 'arc72_tokenURI']],
  ['ARC-72 Enumeration', ['arc72_balanceOf', 'arc72_totalSupply', 'arc72_tokenByIndex'], 'ARC-72'],
  ['ARC-72 Management', ['arc72_approve', 'arc72_setApprovalForAll', 'arc72_getApproved', 'arc72_isApprovedForAll'], 'ARC-72'],
  ['ARC-73', ['supportsInterface']],
];

const TOKEN_STANDARDS: readonly ArcTokenStandard[] = ['ARC-200', 'ARC-72'];

/** The standards whose methods all appear among the names, e.g. ['ARC-72', 'ARC-73']. */
export function detect(names: readonly string[]): string[] {
  return Standards.detect(STANDARDS, names);
}

/** The token standard among the detected ones. */
export function tokenStandard(standards: readonly string[]): ArcTokenStandard | undefined {
  return Standards.tokenStandard(TOKEN_STANDARDS, standards);
}
