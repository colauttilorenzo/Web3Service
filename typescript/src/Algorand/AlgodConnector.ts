import type { IAlgorandNetwork } from '../Models.js';
import * as Configuration from '../Internal/Configuration.js';
import * as Endpoints from '../Internal/Endpoints.js';
import { AlgodSession } from './AlgodSession.js';

/** Opens one verified algod session per operation and falls back to the next node when one is down or on another network. */
export class AlgodConnector {
  readonly #network: Readonly<IAlgorandNetwork>;
  readonly #endpoints: readonly string[];
  readonly #timeoutMs: number;

  constructor(network: Readonly<IAlgorandNetwork>, timeoutMs: number) {
    this.#network = network;
    this.#endpoints = Configuration.endpoints(network.rpcURI, network.rpcPublicURI);
    this.#timeoutMs = timeoutMs;
  }

  /** Runs an operation on a verified session. Unexpected failures become RPC_ERROR. */
  async use<T>(operation: (session: AlgodSession) => Promise<T>): Promise<T> {
    return Endpoints.run(await Endpoints.openFirst(this.#endpoints, this.#network.chainId, uri => this.#openVerified(uri)), operation);
  }

  #openVerified(uri: string): Promise<AlgodSession> {
    return Endpoints.verified(AlgodSession.open(uri, this.#network, this.#timeoutMs), session => this.#verifyGenesis(session));
  }

  /** The genesis ID identifies the network, as the chain ID does on EVM networks. */
  async #verifyGenesis(session: AlgodSession): Promise<void> {
    const actual = await session.load();
    if (actual !== this.#network.chainId) throw Endpoints.chainMismatch(this.#network.chainId, actual);
  }
}
