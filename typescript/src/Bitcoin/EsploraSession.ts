import type { IBitcoinNetwork } from '../Models.js';
import { Web3Error } from '../Web3Exception.js';
import * as Configuration from '../Internal/Configuration.js';
import * as Endpoints from '../Internal/Endpoints.js';
import { HttpStatusError, RestClient } from '../Internal/RestClient.js';

/** Genesis block hashes of the public networks; another network name skips the check. */
const GENESIS: Readonly<Record<string, string>> = {
  mainnet: '000000000019d6689c085ae165831e934ff763ae46a2a6c172b3f1b60a8ce26f',
  testnet: '000000000933ea01ad0ee984209779baaef3ee6c8af2eac1fd6a6b17ed2e4943',
  testnet4: '00000000da84f2bafbbc53dee25a72ae507ff4914b867c565be350b0da8bf043',
  signet: '00000008819873e925422c1ff0f99f7cc9bbb232af63a077a480a3633bee1ef6',
  regtest: '0f9188f13cb7b2c71f2a335e3a4fc328bf5beb436012afca590b1a11466e2206',
};
const NOT_FOUND = 404;

/** An Esplora REST API (Blockstream, mempool.space or a self-hosted electrs), bounded by the configured timeout. */
export class EsploraSession implements Endpoints.Session {
  readonly #api: RestClient;

  constructor(uri: string, timeoutMs: number) {
    this.#api = new RestClient(uri, timeoutMs);
  }

  /** A JSON resource; undefined when the API answers 404. */
  json<T>(path: string): Promise<T | undefined> {
    return this.#api.optionalJson<T>(path);
  }

  async text(path: string): Promise<string> {
    try {
      return (await this.#api.getText(path)).trim();
    } catch (error) {
      if (error instanceof HttpStatusError && error.status === NOT_FOUND) throw new Web3Error('RPC_ERROR', `${path} was not found.`);
      throw error;
    }
  }

  dispose(): void {
    // HTTP requests hold no connection to close.
  }
}

/** Opens one verified session per operation and falls back to the next API when one is down or on another network. */
export class EsploraConnector {
  readonly #network: Readonly<IBitcoinNetwork>;
  readonly #endpoints: readonly string[];
  readonly #timeoutMs: number;

  constructor(network: Readonly<IBitcoinNetwork>, timeoutMs: number) {
    this.#network = network;
    this.#endpoints = Configuration.endpoints(network.rpcURI, network.rpcPublicURI);
    this.#timeoutMs = timeoutMs;
  }

  async use<T>(operation: (session: EsploraSession) => Promise<T>): Promise<T> {
    return Endpoints.run(await Endpoints.openFirst(this.#endpoints, this.#network.chainId, uri => this.#openVerified(uri)), operation);
  }

  #openVerified(uri: string): Promise<EsploraSession> {
    return Endpoints.verified(new EsploraSession(uri, this.#timeoutMs), session => this.#verifyGenesis(session));
  }

  /** The genesis block identifies the network, as the chain ID does on EVM networks. */
  async #verifyGenesis(session: EsploraSession): Promise<void> {
    const expected = GENESIS[this.#network.chainId];
    if (expected === undefined) return;
    const actual = await session.text('/block-height/0');
    if (actual !== expected) throw Endpoints.chainMismatch(this.#network.chainId, actual);
  }
}
