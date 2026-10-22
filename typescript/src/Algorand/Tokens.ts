// Algorand application tokens: the IDs of minted NFTs, and which token data an ARC-200 or ARC-72 contract can return.
import { ABIType, bytesToHex, hexToBytes } from 'algosdk';
import type { TokenRead } from '../Internal/TokenMethods.js';
import * as TokenMethods from '../Internal/TokenMethods.js';

/** ARC-28 selector of arc72_Transfer(address,address,uint256): the first 4 bytes of its SHA-512/256 hash. */
const ARC72_TRANSFER = 'd808d4f4';
const ZERO_ADDRESS = '00'.repeat(32);
const TRANSFER_FIELDS = ABIType.from('(address,address,uint256)');
const CONTRACT_READS: readonly TokenRead[] = [
  ['name', 'arc200_name'], ['symbol', 'arc200_symbol'], ['decimals', 'arc200_decimals'], ['totalSupply', 'arc200_totalSupply'],
  ['totalSupply', 'arc72_totalSupply'],
];
const TOKEN_READS: readonly TokenRead[] = [['owner', 'arc72_ownerOf'], ['tokenURI', 'arc72_tokenURI']];

/** IDs of the NFTs the application minted: its arc72_Transfer events from the zero address. */
export function mintedTokenIds(logs: readonly Uint8Array[]): string[] {
  return logs.map(log => bytesToHex(log)).filter(isMint).map(log => {
    const [, , tokenId] = TRANSFER_FIELDS.decode(hexToBytes(log.slice(ARC72_TRANSFER.length))) as [string, string, bigint];
    return tokenId.toString();
  });
}

/** The reads the contract offers: token data, plus the NFT's owner and URI when a token ID is given. */
export function tokenReads(names: readonly string[], withToken: boolean): TokenRead[] {
  return TokenMethods.tokenReads(CONTRACT_READS, TOKEN_READS, names, withToken);
}

function isMint(log: string): boolean {
  return log.startsWith(ARC72_TRANSFER) && log.slice(ARC72_TRANSFER.length, ARC72_TRANSFER.length + ZERO_ADDRESS.length) === ZERO_ADDRESS;
}
