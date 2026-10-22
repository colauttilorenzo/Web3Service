// Receipt lookups through the configured RPC node, so receipts are read the same way whoever signed.
import { TransactionNotFound } from 'web3';
import type { TransactionReceipt } from 'web3';
import { submissionFailed } from './Errors.js';
import type { RpcSession } from './RpcSession.js';

/** The receipt, or undefined while the transaction is unknown or pending. */
export async function get(session: RpcSession, transactionHash: string): Promise<TransactionReceipt | undefined> {
  try {
    return (await session.withTimeout(session.web3.eth.getTransactionReceipt(transactionHash))) ?? undefined;
  } catch (error) {
    if (error instanceof TransactionNotFound) return undefined;
    throw error;
  }
}

/** Like get, but an RPC failure also counts as "not mined yet". */
export async function tryGet(session: RpcSession, transactionHash: string): Promise<TransactionReceipt | undefined> {
  try {
    return await get(session, transactionHash);
  } catch {
    return undefined;
  }
}

/** Polls until the transaction is mined or the timeout expires. */
export async function wait(session: RpcSession, transactionHash: string, timeoutMs: number, pollingIntervalMs: number): Promise<TransactionReceipt> {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const receipt = await tryGet(session, transactionHash);
    if (receipt) return receipt;
    if (Date.now() >= deadline) throw submissionFailed(transactionHash);
    await new Promise(resolve => setTimeout(resolve, pollingIntervalMs));
  }
}
