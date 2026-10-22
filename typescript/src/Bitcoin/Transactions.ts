// Bitcoin transactions as Esplora returns them, described in the same JSON shape as on the other blockchains.
import type { IBitcoinNetwork, ITransaction, ITransactionDetails, ITransactionReceipt } from '../Models.js';
import { transactionNotFound } from '../Internal/Errors.js';
import * as SharedResults from '../Internal/SharedResults.js';
import type { EsploraSession } from './EsploraSession.js';

/** GET /tx/:txid. Values and fees are in satoshis. */
interface EsploraTransaction {
  txid: string;
  version: number;
  fee: number;
  vin: { prevout?: { scriptpubkey_address?: string } | null }[];
  vout: { scriptpubkey_address?: string; value: number }[];
  status: { confirmed: boolean; block_height?: number; block_hash?: string; block_time?: number };
}

/** Where a confirmed transaction sits: its block and position. */
interface BlockPosition {
  readonly hash: string;
  readonly height: string;
  readonly index: string;
  readonly timestamp: string;
}

export async function details(session: EsploraSession, transactionHash: string, network: Readonly<IBitcoinNetwork>): Promise<ITransactionDetails> {
  const transaction = await get(session, transactionHash);
  const position = await positionOf(session, transaction);
  return {
    hash: transaction.txid, from: senderOf(transaction), to: transaction.vout[0]?.scriptpubkey_address, value: valueOf(transaction),
    nonce: '0', gas: '0', input: '0x', type: String(transaction.version), blockHash: position?.hash, blockNumber: position?.height,
    transactionIndex: position?.index, pending: !position, timestamp: position?.timestamp, receipt: position && receiptOf(transaction, position),
    nativeFee: position && SharedResults.nativeFee(transaction.fee, network.nativeCurrency),
  };
}

/** The confirmed transaction as an ITransaction, like a receipt on EVM networks. */
export async function receipt(
  session: EsploraSession, transactionHash: string, result: ITransaction, network: Readonly<IBitcoinNetwork>,
): Promise<ITransaction> {
  const transaction = await get(session, transactionHash);
  const position = await positionOf(session, transaction);
  if (!position) throw transactionNotFound(transactionHash);
  result.requestedAt = SharedResults.requestedAt(result.requestedAt);
  Object.assign(result, { tx: receiptOf(transaction, position), completedAt: position.timestamp, rejectedAt: undefined, error: undefined });
  result.nativeFee = SharedResults.nativeFee(transaction.fee, network.nativeCurrency);
  return result;
}

async function get(session: EsploraSession, transactionHash: string): Promise<EsploraTransaction> {
  const transaction = await session.json<EsploraTransaction>(`/tx/${encodeURIComponent(transactionHash)}`);
  if (!transaction) throw transactionNotFound(transactionHash);
  return transaction;
}

async function positionOf(session: EsploraSession, transaction: EsploraTransaction): Promise<BlockPosition | undefined> {
  const { confirmed, block_hash: hash, block_height: height, block_time: time } = transaction.status;
  if (!confirmed || !hash) return undefined;
  const ids = await session.json<string[]>(`/block/${hash}/txids`) ?? [];
  return { hash, height: String(height), index: String(Math.max(ids.indexOf(transaction.txid), 0)), timestamp: SharedResults.milliseconds(time ?? 0) };
}

/** Gas and logs do not apply to Bitcoin, so they are zero and empty. */
function receiptOf(transaction: EsploraTransaction, position: BlockPosition): ITransactionReceipt {
  return {
    transactionHash: transaction.txid, blockHash: position.hash, blockNumber: position.height, transactionIndex: position.index,
    from: senderOf(transaction), to: transaction.vout[0]?.scriptpubkey_address ?? null, contractAddress: null, type: String(transaction.version),
    status: '1', cumulativeGasUsed: '0', gasUsed: '0', logsBloom: '', logs: [],
  };
}

/** The address of the first input; empty for coinbase transactions. */
function senderOf(transaction: EsploraTransaction): string {
  return transaction.vin[0]?.prevout?.scriptpubkey_address ?? '';
}

/** Satoshis sent to other addresses: the outputs that do not return change to the sender. */
function valueOf(transaction: EsploraTransaction): string {
  const sender = senderOf(transaction);
  return String(transaction.vout.filter(output => output.scriptpubkey_address !== sender).reduce((total, output) => total + output.value, 0));
}
