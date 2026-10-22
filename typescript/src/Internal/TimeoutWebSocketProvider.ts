import { WebSocketProvider } from 'web3';
import type { EthExecutionAPI, JsonRpcResponseWithResult, Web3APIMethod, Web3APIPayload, Web3APIReturnType } from 'web3';

const CLOSED_MESSAGE = 'The WebSocket connection is closed.';
const TIMEOUT_MESSAGE = 'The WebSocket RPC request timed out.';

type Cancel = (error: Error) => void;

/** Rejects on timeout or cancellation until it is released. */
interface RequestGuard {
  readonly rejection: Promise<never>;
  release(): void;
}

/** A socket owned by one service operation, with bounded RPC requests and no automatic replay. */
export class TimeoutWebSocketProvider extends WebSocketProvider {
  readonly #timeoutMs: number;
  readonly #pending = new Set<Cancel>();
  #disposed = false;

  constructor(rpcURI: string, timeoutMs: number) {
    super(rpcURI, { handshakeTimeout: timeoutMs }, { autoReconnect: false });
    this.#timeoutMs = timeoutMs;
  }

  /** Service operations poll receipts over either transport; they do not expose subscriptions. */
  override supportsSubscriptions(): boolean {
    return false;
  }

  override async request<Method extends Web3APIMethod<EthExecutionAPI>, ResultType = Web3APIReturnType<EthExecutionAPI, Method>>(
    payload: Web3APIPayload<EthExecutionAPI, Method>,
  ): Promise<JsonRpcResponseWithResult<ResultType>> {
    if (this.#disposed) throw new Error(CLOSED_MESSAGE);
    const guard = this.#guardRequest();
    try {
      return await Promise.race([super.request<Method, ResultType>(payload), guard.rejection]);
    } finally {
      guard.release();
    }
  }

  dispose(): void {
    if (this.#disposed) return;
    this.#disposed = true;
    for (const cancel of this.#pending) cancel(new Error(CLOSED_MESSAGE));
    this.#pending.clear();
    this.clearQueues();
    this.disconnect();
    // Terminate locally without waiting for the remote peer's close handshake (Node only; browsers just close).
    (this._socketConnection as { terminate?: () => void } | undefined)?.terminate?.();
  }

  /** A timed-out request also closes the socket, because its late response must not be read as another one's. */
  #guardRequest(): RequestGuard {
    let cancel!: Cancel;
    const rejection = new Promise<never>((_, reject) => { cancel = reject; });
    const timer = setTimeout(() => { cancel(new Error(TIMEOUT_MESSAGE)); this.dispose(); }, this.#timeoutMs);
    this.#pending.add(cancel);
    return { rejection, release: () => { clearTimeout(timer); this.#pending.delete(cancel); } };
  }
}
