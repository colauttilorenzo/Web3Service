import { HttpProvider } from 'web3';
import type { EthExecutionAPI, JsonRpcResponseWithResult, Web3APIMethod, Web3APIPayload, Web3APIReturnType } from 'web3';

/** An HTTP provider whose requests time out. Each request gets a fresh signal: a shared one would also expire receipt polling. */
export class TimeoutHttpProvider extends HttpProvider {
  readonly #timeoutMs: number;

  constructor(rpcURI: string, timeoutMs: number) {
    super(rpcURI, { providerOptions: { keepalive: false } });
    this.#timeoutMs = timeoutMs;
  }

  override request<Method extends Web3APIMethod<EthExecutionAPI>, ResultType = Web3APIReturnType<EthExecutionAPI, Method>>(
    payload: Web3APIPayload<EthExecutionAPI, Method>,
    requestOptions?: RequestInit,
  ): Promise<JsonRpcResponseWithResult<ResultType>> {
    const signal = requestOptions?.signal ?? AbortSignal.timeout(this.#timeoutMs);
    return super.request<Method, ResultType>(payload, { ...requestOptions, signal });
  }
}
