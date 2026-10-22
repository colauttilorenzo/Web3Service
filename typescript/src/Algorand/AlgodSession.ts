import { IntDecoding, base64ToBytes, parseJSON } from 'algosdk';
import type { SuggestedParams } from 'algosdk';
import type { IAlgorandNetwork } from '../Models.js';
import type * as Endpoints from '../Internal/Endpoints.js';
import { RestClient } from '../Internal/RestClient.js';

/** GET /v2/transactions/params. Integers are bigint, as in every algod response. */
interface TransactionParams {
  readonly fee: bigint;
  readonly 'min-fee': bigint;
  readonly 'last-round': bigint;
  readonly 'genesis-id': string;
  readonly 'genesis-hash': string;
}

/** Rounds a transaction stays valid, as algosdk sets it. */
const VALID_ROUNDS = 1000n;

/**
 * A connection to one algod node (and the indexer, when configured), owned by a single service operation. It holds the suggested
 * parameters read when it opened, so every transaction of the operation uses the same fees and validity rounds.
 * Responses are JSON with every integer as bigint, so amounts and asset totals beyond 2^53 stay exact.
 */
export class AlgodSession implements Endpoints.Session {
  readonly #algod: RestClient;
  readonly #indexer?: RestClient;
  #params?: SuggestedParams;

  private constructor(algod: RestClient, indexer?: RestClient) {
    this.#algod = algod;
    this.#indexer = indexer;
  }

  static open(uri: string, network: Readonly<IAlgorandNetwork>, timeoutMs: number): AlgodSession {
    const indexer = network.indexerURI ? client(network.indexerURI, timeoutMs, 'X-Indexer-API-Token', network.indexerToken) : undefined;
    return new AlgodSession(client(uri, timeoutMs, 'X-Algo-API-Token', network.token), indexer);
  }

  /** Fees, validity rounds and genesis of the network, read when the session opened. */
  get params(): SuggestedParams {
    if (!this.#params) throw new Error('The session has not been verified.');
    return this.#params;
  }

  /** Reads the suggested parameters and returns the genesis ID of the node's network. */
  async load(): Promise<string> {
    const params = await this.get<TransactionParams>('v2/transactions/params');
    this.#params = {
      flatFee: false, fee: params.fee, minFee: params['min-fee'], firstValid: params['last-round'], lastValid: params['last-round'] + VALID_ROUNDS,
      genesisID: params['genesis-id'], genesisHash: base64ToBytes(params['genesis-hash']),
    };
    return params['genesis-id'];
  }

  get<T>(path: string): Promise<T> {
    return this.#algod.getJson<T>(path);
  }

  /** The response, or undefined when the node answers 404 Not Found. */
  optional<T>(path: string): Promise<T | undefined> {
    return this.#algod.optionalJson<T>(path);
  }

  /** The indexer's response; undefined when it answers 404 or no indexer is configured. */
  async optionalIndexed<T>(path: string): Promise<T | undefined> {
    return this.#indexer?.optionalJson<T>(path);
  }

  post<T>(path: string, body: Uint8Array, contentType: string): Promise<T> {
    return this.#algod.post<T>(path, body, contentType);
  }

  dispose(): void {
    // HTTP requests hold no connection to close.
  }
}

function client(uri: string, timeoutMs: number, tokenHeader: string, token?: string): RestClient {
  return new RestClient(uri, timeoutMs, { tokenHeader, token, parse: text => parseJSON(text, { intDecoding: IntDecoding.BIGINT }) });
}
