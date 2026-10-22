import type Big from 'big.js';
import { Web3 } from 'web3';
import * as Integers from './Integers.js';
import { TimeoutHttpProvider } from './TimeoutHttpProvider.js';
import { TimeoutWebSocketProvider } from './TimeoutWebSocketProvider.js';

const WEBSOCKET_URI = /^wss?:/i;

/**
 * A connection to one RPC endpoint, owned by a single service operation.
 * Every request is bounded by the configured timeout.
 */
export class RpcSession {
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
  async withTimeout<T>(request: Promise<T>): Promise<T> {
    let timer: ReturnType<typeof setTimeout> | undefined;
    const timeout = new Promise<never>((_, reject) => {
      timer = setTimeout(() => reject(new Error('The RPC request timed out.')), this.#timeoutMs);
    });
    try {
      return await Promise.race([request, timeout]);
    } finally {
      clearTimeout(timer);
    }
  }

  /** Awaits a request that returns a JSON-RPC quantity (an integer). */
  async quantity(request: Promise<unknown>): Promise<Big> {
    return Integers.parse(await this.withTimeout(request), 'RPC quantity');
  }

  dispose(): void {
    this.#transport?.dispose();
  }
}
