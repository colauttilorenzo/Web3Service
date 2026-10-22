import type Big from 'big.js';
import { Web3 } from 'web3';
import * as Endpoints from '../Internal/Endpoints.js';
import * as Integers from '../Internal/Integers.js';
import { TimeoutHttpProvider } from './TimeoutHttpProvider.js';
import { TimeoutWebSocketProvider } from './TimeoutWebSocketProvider.js';

const WEBSOCKET_URI = /^wss?:/i;

/**
 * A connection to one RPC endpoint, owned by a single service operation.
 * Every request is bounded by the configured timeout.
 */
export class RpcSession implements Endpoints.Session {
  readonly web3: Web3;
  readonly #timeoutMs: number;
  readonly #transport?: TimeoutWebSocketProvider;

  private constructor(web3: Web3, timeoutMs: number, transport?: TimeoutWebSocketProvider) {
    this.web3 = web3;
    this.#timeoutMs = timeoutMs;
    this.#transport = transport;
  }

  /** Opens a WebSocket session for ws:// and wss:// URIs, an HTTP session otherwise. A web3Factory replaces both. */
  static open(rpcURI: string, timeoutMs: number, web3Factory?: (rpcURI: string) => Web3): RpcSession {
    if (web3Factory) return new RpcSession(web3Factory(rpcURI), timeoutMs);
    if (WEBSOCKET_URI.test(rpcURI)) {
      const socket = new TimeoutWebSocketProvider(rpcURI, timeoutMs);
      return new RpcSession(new Web3(socket), timeoutMs, socket);
    }
    return new RpcSession(new Web3(new TimeoutHttpProvider(rpcURI, timeoutMs)), timeoutMs);
  }

  /** Awaits a request within the timeout, also when a custom web3Factory provides the transport. */
  withTimeout<T>(request: Promise<T>): Promise<T> {
    return Endpoints.withTimeout(request, this.#timeoutMs);
  }

  /** Awaits a request that returns a JSON-RPC quantity (an integer). */
  async quantity(request: Promise<unknown>): Promise<Big> {
    return Integers.parse(await this.withTimeout(request), 'RPC quantity');
  }

  dispose(): void {
    this.#transport?.dispose();
  }
}
