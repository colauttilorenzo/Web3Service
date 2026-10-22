// Signs and sends a group, then waits until its last transaction is confirmed.
import type { Transaction } from 'algosdk';
import { Web3Error } from '../Web3Exception.js';
import { submissionFailed } from '../Internal/Errors.js';
import type { AlgodSession } from './AlgodSession.js';
import type { AvmSigner } from './AlgorandSigner.js';
import * as Transactions from './Transactions.js';
import type { PendingTransaction } from './Transactions.js';

/** Signs and broadcasts the group, returning the ID of its last transaction once the node has accepted it. */
export async function send(session: AlgodSession, group: readonly Transaction[], signer: AvmSigner): Promise<string> {
  const signed = await signer.sign(group);
  const transactionId = group.at(-1)!.txID();
  try {
    await session.post('v2/transactions', concat(signed), 'application/x-binary');
  } catch (error) {
    if (await Transactions.tryPending(session, transactionId) === undefined) throw submissionFailed(transactionId, error);
  }
  return transactionId;
}

/** Polls until the transaction is confirmed, the node drops it, or the timeout expires. */
export async function wait(
  session: AlgodSession, transactionId: string, timeoutMs: number, pollingIntervalMs: number,
): Promise<PendingTransaction> {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const pending = await Transactions.tryPending(session, transactionId);
    if (pending?.['confirmed-round']) return pending;
    const poolError = pending?.['pool-error'];
    if (poolError) throw new Web3Error('TRANSACTION_REVERTED', `The node rejected the transaction: ${poolError}`, { transactionHash: transactionId });
    if (Date.now() >= deadline) throw submissionFailed(transactionId);
    await new Promise(resolve => setTimeout(resolve, pollingIntervalMs));
  }
}

/** The signed transactions of a group, one after the other, as algod receives them. */
function concat(parts: readonly Uint8Array[]): Uint8Array {
  const bytes = new Uint8Array(parts.reduce((total, part) => total + part.length, 0));
  let offset = 0;
  for (const part of parts) {
    bytes.set(part, offset);
    offset += part.length;
  }
  return bytes;
}
