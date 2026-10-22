import type { Web3 } from 'web3';
import type { IEthereumProvider } from '../Models.js';
import { Web3Error } from '../Web3Exception.js';
import * as Configuration from './Configuration.js';
import * as Integers from './Integers.js';
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
    const session = await this.#open();
    try {
      return await operation(session);
    } catch (cause) {
      throw cause instanceof Web3Error ? cause : new Web3Error('RPC_ERROR', 'The blockchain operation failed.', { cause });
    } finally {
      session.dispose();
    }
  }

  async #open(): Promise<RpcSession> {
    const failures: unknown[] = [];
    for (const uri of this.#endpoints) {
      try {
        return await this.#openVerified(uri);
      } catch (error) {
        failures.push(error);
      }
    }
    throw this.#selectionError(failures);
  }

  async #openVerified(uri: string): Promise<RpcSession> {
    const session = RpcSession.open(uri, this.#timeoutMs, this.#web3Factory);
    try {
      await this.#verifyChainId(session);
    } catch (error) {
      session.dispose();
      throw error;
    }
    return session;
  }

  async #verifyChainId(session: RpcSession): Promise<void> {
    const actual = Integers.toHex(await session.quantity(session.web3.eth.getChainId()));
    if (actual !== this.#chainId) throw new Web3Error('CHAIN_ID_MISMATCH', `Expected chain ${this.#chainId}, received ${actual}.`);
  }

  /** An endpoint on the wrong chain is more useful to report than an unreachable one. */
  #selectionError(failures: unknown[]): Web3Error {
    const mismatch = failures.filter(isChainMismatch).at(-1);
    const message = `No RPC endpoint is available for chain ${this.#chainId}.`;
    return mismatch ?? new Web3Error('PROVIDER_UNAVAILABLE', message, { cause: failures.at(-1) });
  }
}

function isChainMismatch(error: unknown): error is Web3Error {
  return error instanceof Web3Error && error.code === 'CHAIN_ID_MISMATCH';
}
