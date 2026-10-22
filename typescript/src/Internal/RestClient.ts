// A JSON REST API (algod, indexer, Esplora) at a base URI, with an optional token header. Every request is bounded by the timeout.

const NOT_FOUND = 404;

/** A REST API answered with an error status; the message is the API's own, when it gives one. */
export class HttpStatusError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(`HTTP ${status}: ${message}`);
    this.name = 'HttpStatusError';
    this.status = status;
  }
}

export interface RestClientOptions {
  /** Header that carries the token, e.g. X-Algo-API-Token. */
  tokenHeader?: string;
  token?: string;
  /** Parses JSON bodies; algod needs one that keeps integers beyond 2^53 exact. Default: JSON.parse. */
  parse?: (text: string) => unknown;
}

export class RestClient {
  readonly #baseUri: string;
  readonly #timeoutMs: number;
  readonly #headers: Record<string, string>;
  readonly #parse: (text: string) => unknown;

  constructor(baseUri: string, timeoutMs: number, options: RestClientOptions = {}) {
    this.#baseUri = baseUri.replace(/\/+$/, '');
    this.#timeoutMs = timeoutMs;
    this.#headers = options.tokenHeader && options.token ? { [options.tokenHeader]: options.token } : {};
    this.#parse = options.parse ?? JSON.parse;
  }

  async getJson<T>(path: string): Promise<T> {
    return this.#parse(await this.getText(path)) as T;
  }

  /** The response, or undefined when the API answers 404 Not Found. */
  async optionalJson<T>(path: string): Promise<T | undefined> {
    try {
      return await this.getJson<T>(path);
    } catch (error) {
      if (error instanceof HttpStatusError && error.status === NOT_FOUND) return undefined;
      throw error;
    }
  }

  getText(path: string): Promise<string> {
    return this.#send(path, { method: 'GET', headers: this.#headers });
  }

  /** The body is never backed by a SharedArrayBuffer, which is all the BodyInit type rules out. */
  async post<T>(path: string, body: Uint8Array, contentType: string): Promise<T> {
    const init: RequestInit = { method: 'POST', body: body as Uint8Array<ArrayBuffer>, headers: { ...this.#headers, 'Content-Type': contentType } };
    return this.#parse(await this.#send(path, init)) as T;
  }

  /** The response body. The timeout aborts the request and the download of the body. */
  async #send(path: string, init: RequestInit): Promise<string> {
    const response = await fetch(`${this.#baseUri}/${path.replace(/^\/+/, '')}`, { ...init, signal: AbortSignal.timeout(this.#timeoutMs) });
    const body = await response.text();
    if (!response.ok) throw new HttpStatusError(response.status, messageOf(body));
    return body;
  }
}

/** The message of a JSON error body, else the body itself. */
function messageOf(body: string): string {
  try {
    return (JSON.parse(body) as { message?: unknown } | null)?.message?.toString() ?? body;
  } catch {
    return body;
  }
}
