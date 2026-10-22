// Tokens and NFTs: which mint function to call, the IDs of the minted tokens, and which token data the contract can return.
import type { ContractParameters, ITokenInfo, ITransactionLog } from '../Models.js';
import * as AbiMethods from './AbiMethods.js';
import type { AbiFunction } from './AbiMethods.js';
import { invalidArgument, notSupported } from './Errors.js';
import * as Integers from './Integers.js';

/** A token field and the read-only function that returns it. */
export type TokenRead = readonly [field: keyof ITokenInfo, signature: string];

const MINT_FUNCTIONS = ['safeMint', 'mint', 'mintTo'];
const TRANSFER = '0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef';
const TRANSFER_SINGLE = '0xc3d58168c5ae7397731d063d5bbf3d657854427343f4c083240f7aacaa2d0f62';
const TRANSFER_BATCH = '0x4a39dc06d4c0dbc64b70af90fd698a233a518aa5d07e595d983b8c0526c8f7fb';
const ZERO_TOPIC = `0x${'0'.repeat(64)}`;
const WORD_DIGITS = 64;
const WORD_BYTES = 32;
const CONTRACT_READS: readonly TokenRead[] = [['name', 'name()'], ['symbol', 'symbol()'], ['decimals', 'decimals()'], ['totalSupply', 'totalSupply()']];
const TOKEN_READS: readonly TokenRead[] = [['owner', 'ownerOf(uint256)'], ['tokenURI', 'tokenURI(uint256)'], ['tokenURI', 'uri(uint256)']];

/** The safeMint, mint or mintTo function whose parameter names match, in this order of preference. */
export function mintFunction(functions: readonly AbiFunction[], parameters: ContractParameters): AbiFunction {
  const candidates = MINT_FUNCTIONS.flatMap(name => functions.filter(fn => fn.method.name === name && fn.method.kind !== 'read'));
  if (candidates.length === 0) throw notSupported('The contract has no safeMint, mint or mintTo function.');
  const match = candidates.find(fn => AbiMethods.hasParameters(fn, parameters));
  if (!match) throw invalidArgument(`Pass the parameters of one of ${AbiMethods.signatures(candidates)}.`);
  return match;
}

/** IDs of the tokens the contract created: its transfers from the zero address. */
export function mintedTokenIds(logs: readonly ITransactionLog[], address: string): string[] {
  return logs.filter(log => log.address?.toLowerCase() === address.toLowerCase()).flatMap(mintedIds);
}

/** The reads that apply to the contract: contract data, plus the token's owner and URI when a token ID is given. */
export function tokenReads(signatures: readonly string[], withToken: boolean): TokenRead[] {
  const reads = withToken ? [...CONTRACT_READS, ...TOKEN_READS] : CONTRACT_READS;
  return reads.filter(([, signature]) => signatures.includes(signature));
}

export function assign(info: ITokenInfo, field: keyof ITokenInfo, value: string): void {
  (info as unknown as Record<string, string>)[field] = value;
}

/** ERC-1155 URIs may contain {id}, which stands for the token ID as 64 lowercase hex digits. */
export function expandTokenUri(info: ITokenInfo): ITokenInfo {
  if (info.tokenURI === undefined || info.tokenId === undefined) return info;
  info.tokenURI = info.tokenURI.replace('{id}', BigInt(info.tokenId).toString(16).padStart(WORD_DIGITS, '0'));
  return info;
}

function mintedIds(log: ITransactionLog): string[] {
  const [topic, ...indexed] = (log.topics ?? []).map(value => value.toLowerCase());
  if (topic === TRANSFER && indexed.length === 3 && indexed[0] === ZERO_TOPIC) return [Integers.toDecimalString(indexed[2])];
  if (topic === TRANSFER_SINGLE && indexed[1] === ZERO_TOPIC) return [word(log.data ?? '0x', 0)];
  if (topic === TRANSFER_BATCH && indexed[1] === ZERO_TOPIC) return batchIds(log.data ?? '0x');
  return [];
}

/** TransferBatch data starts with the offset of the ids array, which holds its length followed by the ids. */
function batchIds(data: string): string[] {
  const offset = Number(word(data, 0)) / WORD_BYTES;
  const length = Number(word(data, offset));
  return Array.from({ length }, (_, index) => word(data, offset + 1 + index));
}

/** The 32-byte word at the index, as a decimal string. */
function word(data: string, index: number): string {
  const start = 2 + index * WORD_DIGITS;
  return Integers.toDecimalString(`0x${data.slice(start, start + WORD_DIGITS) || '0'}`);
}
