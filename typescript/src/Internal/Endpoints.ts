// Endpoint selection shared by every blockchain: one verified session per operation, with fallback to the next endpoint.
import { Web3Error } from '../Web3Exception.js';

/** A connection to one endpoint, closed when the operation ends. */
export interface Session {
  dispose(): void;
}

/** Opens the first endpoint that answers on the expected chain; an endpoint on the wrong chain is reported before an unreachable one. */
export async function openFirst<S>(endpoints: readonly string[], chainId: string, open: (uri: string) => Promise<S>): Promise<S> {
  const failures: unknown[] = [];
  for (const uri of endpoints) {
    try {
      return await open(uri);
    } catch (error) {
      failures.push(error);
    }
  }
  const mismatch = failures.filter(isChainMismatch).at(-1);
  throw mismatch ?? new Web3Error('PROVIDER_UNAVAILABLE', `No RPC endpoint is available for chain ${chainId}.`, { cause: failures.at(-1) });
}

/** Verifies a new session and closes it when the verification fails. */
export async function verified<S extends Session>(session: S, verify: (session: S) => Promise<void>): Promise<S> {
  try {
    await verify(session);
  } catch (error) {
    session.dispose();
    throw error;
  }
  return session;
}

/** Runs an operation on a session and always closes it. Unexpected failures become RPC_ERROR. */
export async function run<S extends Session, T>(session: S, operation: (session: S) => Promise<T>): Promise<T> {
  try {
    return await operation(session);
  } catch (cause) {
    throw cause instanceof Web3Error ? cause : new Web3Error('RPC_ERROR', 'The blockchain operation failed.', { cause });
  } finally {
    session.dispose();
  }
}

export function chainMismatch(expected: string, actual: string): Web3Error {
  return new Web3Error('CHAIN_ID_MISMATCH', `Expected chain ${expected}, received ${actual}.`);
}

/** Awaits a request within the timeout. */
export async function withTimeout<T>(request: Promise<T>, timeoutMs: number): Promise<T> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  const timeout = new Promise<never>((_, reject) => {
    timer = setTimeout(() => reject(new Error('The RPC request timed out.')), timeoutMs);
  });
  try {
    return await Promise.race([request, timeout]);
  } finally {
    clearTimeout(timer);
  }
}

function isChainMismatch(error: unknown): error is Web3Error {
  return error instanceof Web3Error && error.code === 'CHAIN_ID_MISMATCH';
}
