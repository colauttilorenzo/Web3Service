// Algorand Standard Assets: tokens and NFTs created without a contract.
import { hexToBytes, makeAssetCreateTxnWithSuggestedParamsFromObject } from 'algosdk';
import type { ChainId, ITokenDefinition, ITokenInfo, TransactionOptions } from '../Models.js';
import { invalidArgument, tokenNotFound } from '../Internal/Errors.js';
import * as Integers from '../Internal/Integers.js';
import type { AlgodSession } from './AlgodSession.js';
import type { AvmRequest } from './AvmRequest.js';

/** GET /v2/assets/:id. */
interface AssetParams {
  readonly creator: string;
  readonly name?: string;
  readonly 'unit-name'?: string;
  readonly decimals: bigint;
  readonly total: bigint;
  readonly url?: string;
}

/** The asset parameters the token definition sets. */
interface AssetFields {
  readonly assetName: string;
  readonly unitName: string;
  readonly total: bigint;
  readonly decimals: number;
  readonly assetURL?: string;
  readonly assetMetadataHash?: Uint8Array;
}

const MAX_UNIT_NAME_BYTES = 8;
const MAX_TOTAL = (1n << 64n) - 1n;
const MAX_ASSET_NAME_BYTES = 32;
const MAX_URL_BYTES = 96;
const MAX_DECIMALS = 19;
const METADATA_HASH = /^0x[0-9a-f]{64}$/i;

/** The asset creation; the creator manages the asset and may later reconfigure it. */
export function creation(token: ITokenDefinition, options: TransactionOptions = {}): AvmRequest {
  const definition = validate(token);
  return {
    options, callsApplication: false, explain: failure => failure,
    build: (sender, params, plan) => [makeAssetCreateTxnWithSuggestedParamsFromObject({
      sender, ...definition, defaultFrozen: false, manager: sender, suggestedParams: { ...params, fee: plan.fee, flatFee: true },
    })],
  };
}

/** Name, unit name, decimals, total supply and URL of an asset, with its creator. */
export async function info(session: AlgodSession, tokenId: ChainId): Promise<ITokenInfo> {
  const id = Integers.parse(tokenId, 'tokenId').toFixed(0);
  const params = (await session.optional<{ params: AssetParams }>(`v2/assets/${id}`))?.params;
  if (!params) throw tokenNotFound(id);
  return {
    standard: 'ASA', address: id, name: params.name, symbol: params['unit-name'], decimals: String(params.decimals),
    totalSupply: params.total.toString(), tokenURI: params.url, creator: params.creator,
  };
}

function validate(token: ITokenDefinition): AssetFields {
  const decimals = token?.decimals ?? 0;
  if (!Number.isInteger(decimals) || decimals < 0 || decimals > MAX_DECIMALS) throw invalidArgument(`decimals must be an integer between 0 and ${MAX_DECIMALS}.`);
  return {
    assetName: text(token.name, 'name', MAX_ASSET_NAME_BYTES), unitName: text(token.symbol, 'symbol', MAX_UNIT_NAME_BYTES),
    total: total(token.totalSupply), decimals,
    assetURL: token.uri === undefined ? undefined : text(token.uri, 'uri', MAX_URL_BYTES), assetMetadataHash: metadataHash(token.metadataHash),
  };
}

function total(value: ChainId): bigint {
  const supply = BigInt(Integers.parse(value, 'totalSupply').toFixed(0));
  if (supply === 0n || supply > MAX_TOTAL) throw invalidArgument('totalSupply must be between 1 and 2^64 - 1.');
  return supply;
}

function text(value: string | undefined, name: string, maxBytes: number): string {
  if (typeof value !== 'string' || value.length === 0) throw invalidArgument(`${name} is required.`);
  if (new TextEncoder().encode(value).length > maxBytes) throw invalidArgument(`${name} must be at most ${maxBytes} bytes.`);
  return value;
}

function metadataHash(value: string | undefined): Uint8Array | undefined {
  if (value === undefined) return undefined;
  if (!METADATA_HASH.test(value)) throw invalidArgument('metadataHash must be 32 bytes of 0x hex.');
  return hexToBytes(value.slice(2));
}
