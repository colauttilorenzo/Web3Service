// Recognizes ERC standards from the functions of an ABI. A standard matches when the ABI has all of its functions.
import type { TokenStandard } from '../Models.js';

/** Standard, the function signatures it requires, and the standard it extends. */
type Standard = readonly [id: string, functions: readonly string[], extended?: string];

const STANDARDS: readonly Standard[] = [
  ['ERC-20', ['totalSupply()', 'balanceOf(address)', 'transfer(address,uint256)', 'transferFrom(address,address,uint256)',
    'approve(address,uint256)', 'allowance(address,address)']],
  ['ERC-721', ['balanceOf(address)', 'ownerOf(uint256)', 'safeTransferFrom(address,address,uint256)',
    'safeTransferFrom(address,address,uint256,bytes)', 'transferFrom(address,address,uint256)', 'approve(address,uint256)',
    'setApprovalForAll(address,bool)', 'getApproved(uint256)', 'isApprovedForAll(address,address)']],
  ['ERC-721 Metadata', ['name()', 'symbol()', 'tokenURI(uint256)'], 'ERC-721'],
  ['ERC-721 Enumerable', ['totalSupply()', 'tokenOfOwnerByIndex(address,uint256)', 'tokenByIndex(uint256)'], 'ERC-721'],
  ['ERC-1155', ['balanceOf(address,uint256)', 'balanceOfBatch(address[],uint256[])', 'setApprovalForAll(address,bool)',
    'isApprovedForAll(address,address)', 'safeTransferFrom(address,address,uint256,uint256,bytes)',
    'safeBatchTransferFrom(address,address,uint256[],uint256[],bytes)']],
  ['ERC-1155 Metadata URI', ['uri(uint256)'], 'ERC-1155'],
  ['ERC-165', ['supportsInterface(bytes4)']],
  ['ERC-173', ['owner()', 'transferOwnership(address)']],
  ['ERC-2981', ['royaltyInfo(uint256,uint256)']],
];

const TOKEN_STANDARDS: readonly TokenStandard[] = ['ERC-20', 'ERC-721', 'ERC-1155'];

/** The standards whose functions all appear among the signatures, e.g. ['ERC-721', 'ERC-721 Metadata', 'ERC-165']. */
export function detect(signatures: readonly string[]): string[] {
  const found: string[] = [];
  for (const [id, functions, extended] of STANDARDS) {
    if (functions.every(fn => signatures.includes(fn)) && (!extended || found.includes(extended))) found.push(id);
  }
  return found;
}

/** The token standard among the detected ones. */
export function tokenStandard(standards: readonly string[]): TokenStandard | undefined {
  return TOKEN_STANDARDS.find(standard => standards.includes(standard));
}
