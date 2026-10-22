import type { Web3 } from 'web3';
import type { IEthereumProvider } from '../Models.js';
import * as Configuration from '../Internal/Configuration.js';
import * as Endpoints from '../Internal/Endpoints.js';
import * as Integers from '../Internal/Integers.js';
import { RpcSession } from './RpcSession.js';

/** Opens one verified session per operation and falls back to the next endpoint when one is down or on the wrong chain. */
export class RpcConnector {
  readonly #chainId: string;
  readonly #endpoints: readonly string[];
  readonly #timeoutMs: number;
  readonly #web3Factory?: (rpcURI: string) => Web3;

  constructor(provider: Readonly<IEthereumProvider>, timeoutMs: number, web3Factory?: (rpcURI: string) => Web3) {
    this.#chainId = String(provider.chainId);
    this.#endpoints = Configuration.endpoints(provider.rpcURI, provider.rpcPublicURI);
    this.#timeoutMs = timeoutMs;
    this.#web3Factory = web3Factory;
  }

  /** Runs an operation on a verified session and always closes it. Unexpected failures become RPC_ERROR. */
  async use<T>(operation: (session: RpcSession) => Promise<T>): Promise<T> {
    return Endpoints.run(await Endpoints.openFirst(this.#endpoints, this.#chainId, uri => this.#openVerified(uri)), operation);
  }

  #openVerified(uri: string): Promise<RpcSession> {
    return Endpoints.verified(RpcSession.open(uri, this.#timeoutMs, this.#web3Factory), session => this.#verifyChainId(session));
  }

  async #verifyChainId(session: RpcSession): Promise<void> {
    const actual = Integers.toHex(await session.quantity(session.web3.eth.getChainId()));
    if (actual !== this.#chainId) throw Endpoints.chainMismatch(this.#chainId, actual);
  }
}
