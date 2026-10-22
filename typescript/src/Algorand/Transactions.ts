// Algorand transactions read by ID, from algod (pending and recently confirmed) or from the indexer, as one model.
import { base64ToBytes, getApplicationAddress } from 'algosdk';
import { transactionNotFound } from '../Internal/Errors.js';
import type { AlgodSession } from './AlgodSession.js';

/** The fields of a transaction the service reports, whichever API returned it. */
export interface AvmTransaction {
  readonly id: string;
  readonly type: string;
  readonly sender: string;
  /** Receiver of a payment or asset transfer, account of a called application; undefined for creations. */
  readonly receiver?: string;
  readonly amount: bigint;
  readonly fee: bigint;
  readonly appId: bigint;
  readonly appArgs: readonly Uint8Array[];
  readonly createdAppId?: bigint;
  readonly createdAssetId?: bigint;
  /** Assets created by the inner transactions of an application call. */
  readonly innerCreatedAssets: readonly bigint[];
  readonly confirmedRound?: bigint;
  readonly logs: readonly Uint8Array[];
}

/** GET /v2/transactions/pending/:txid on algod, and the result of a simulated transaction. Byte values are base64. */
export interface PendingTransaction {
  readonly txn: { readonly txn: EncodedTransaction };
  readonly 'confirmed-round'?: bigint;
  readonly 'pool-error'?: string;
  readonly 'application-index'?: bigint;
  readonly 'asset-index'?: bigint;
  readonly 'inner-txns'?: readonly PendingTransaction[];
  readonly logs?: readonly string[];
}

/** A transaction with the short field names of its msgpack encoding. */
interface EncodedTransaction {
  readonly type: string;
  readonly snd: string;
  readonly fee?: bigint;
  readonly rcv?: string;
  readonly amt?: bigint;
  readonly arcv?: string;
  readonly aamt?: bigint;
  readonly apid?: bigint;
  readonly apaa?: readonly string[];
}

/** GET /v2/transactions/:txid on the indexer. */
interface IndexedTransaction {
  readonly id: string;
  readonly 'tx-type': string;
  readonly sender: string;
  readonly fee: bigint;
  readonly 'payment-transaction'?: { readonly receiver: string; readonly amount: bigint };
  readonly 'asset-transfer-transaction'?: { readonly receiver: string; readonly amount: bigint };
  readonly 'application-transaction'?: { readonly 'application-id': bigint; readonly 'application-args'?: readonly string[] };
  readonly 'created-application-index'?: bigint;
  readonly 'created-asset-index'?: bigint;
  readonly 'confirmed-round'?: bigint;
  readonly 'inner-txns'?: readonly IndexedTransaction[];
  readonly logs?: readonly string[];
}

/** The transaction from algod, which keeps pending and recent ones, else from the indexer when one is configured. */
export async function get(session: AlgodSession, transactionId: string): Promise<AvmTransaction> {
  const pending = await getPending(session, transactionId);
  if (pending) return fromPending(transactionId, pending);
  const indexed = await session.optionalIndexed<{ transaction: IndexedTransaction }>(`v2/transactions/${encodeURIComponent(transactionId)}`);
  if (indexed) return fromIndexer(indexed.transaction);
  throw transactionNotFound(transactionId);
}

/** Like getPending, but an RPC failure also counts as "unknown". */
export async function tryPending(session: AlgodSession, transactionId: string): Promise<PendingTransaction | undefined> {
  try {
    return await getPending(session, transactionId);
  } catch {
    return undefined;
  }
}

export function fromPending(id: string, info: PendingTransaction): AvmTransaction {
  const { txn } = info.txn;
  const appId = txn.apid ?? 0n;
  return {
    id, type: txn.type, sender: txn.snd, receiver: receiverOf(txn.type, appId, txn.rcv ?? txn.arcv), amount: txn.amt ?? txn.aamt ?? 0n,
    fee: txn.fee ?? 0n, appId, appArgs: bytesOf(txn.apaa), createdAppId: info['application-index'], createdAssetId: info['asset-index'],
    confirmedRound: info['confirmed-round'] || undefined, logs: bytesOf(info.logs),
    innerCreatedAssets: (info['inner-txns'] ?? []).flatMap(inner => inner['asset-index'] ? [inner['asset-index']] : []),
  };
}

function fromIndexer(transaction: IndexedTransaction): AvmTransaction {
  const app = transaction['application-transaction'];
  const transfer = transaction['payment-transaction'] ?? transaction['asset-transfer-transaction'];
  const appId = app?.['application-id'] ?? 0n;
  return {
    id: transaction.id, type: transaction['tx-type'], sender: transaction.sender, receiver: receiverOf(transaction['tx-type'], appId, transfer?.receiver),
    amount: transfer?.amount ?? 0n, fee: transaction.fee, appId, appArgs: bytesOf(app?.['application-args']),
    createdAppId: transaction['created-application-index'], createdAssetId: transaction['created-asset-index'],
    confirmedRound: transaction['confirmed-round'] || undefined, logs: bytesOf(transaction.logs),
    innerCreatedAssets: (transaction['inner-txns'] ?? []).flatMap(inner => inner['created-asset-index'] ? [inner['created-asset-index']] : []),
  };
}

function getPending(session: AlgodSession, transactionId: string): Promise<PendingTransaction | undefined> {
  return session.optional<PendingTransaction>(`v2/transactions/pending/${encodeURIComponent(transactionId)}?format=json`);
}

/** A called application receives through its account; a creation has no receiver. */
function receiverOf(type: string, appId: bigint, receiver: string | undefined): string | undefined {
  if (type === 'appl') return appId === 0n ? undefined : getApplicationAddress(appId).toString();
  return receiver;
}

export function bytesOf(values: readonly string[] | undefined): Uint8Array[] {
  return (values ?? []).map(base64ToBytes);
}
