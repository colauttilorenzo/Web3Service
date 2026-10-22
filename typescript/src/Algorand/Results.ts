// Builds the JSON-safe ITransaction, receipt and details of an Algorand transaction, in the same shape as on EVM networks.
import { bytesToHex, getApplicationAddress } from 'algosdk';
import type { IAlgorandNetwork, ITransaction, ITransactionDetails, ITransactionLog, ITransactionReceipt } from '../Models.js';
import * as SharedResults from '../Internal/SharedResults.js';
import type { AlgodSession } from './AlgodSession.js';
import * as Arc4Methods from './Arc4Methods.js';
import type { Arc4Function } from './Arc4Methods.js';
import type { AvmTransaction } from './Transactions.js';

/** Where a confirmed transaction sits: its block hash, block time in milliseconds and position in the block. */
interface BlockPosition {
  readonly round: string;
  readonly hash: string;
  readonly timestamp: string;
  readonly index: string;
}

/** Fills result with the receipt, the confirmation time and the exact fee. Algorand never commits failed transactions. */
export async function fromConfirmed(
  session: AlgodSession, transaction: AvmTransaction, result: ITransaction, network: Readonly<IAlgorandNetwork>,
): Promise<ITransaction> {
  const position = await positionOf(session, transaction);
  result.requestedAt = SharedResults.requestedAt(result.requestedAt);
  result.tx = receipt(transaction, position);
  Object.assign(result, { completedAt: position.timestamp, rejectedAt: undefined, error: undefined });
  result.nativeFee = SharedResults.nativeFee(transaction.fee, network.nativeCurrency);
  return result;
}

/** The transaction with its receipt, block time and fee once confirmed, and the called method when the contract describes it. */
export async function details(
  session: AlgodSession, transaction: AvmTransaction, functions: readonly Arc4Function[], network: Readonly<IAlgorandNetwork>,
): Promise<ITransactionDetails> {
  const position = transaction.confirmedRound ? await positionOf(session, transaction) : undefined;
  return {
    hash: transaction.id, from: transaction.sender, to: transaction.receiver, value: transaction.amount.toString(), nonce: '0', gas: '0',
    input: Arc4Methods.toHex(transaction.appArgs), type: transaction.type, blockHash: position?.hash, blockNumber: position?.round, transactionIndex: position?.index,
    pending: !position, timestamp: position?.timestamp, receipt: position && receipt(transaction, position),
    nativeFee: position && SharedResults.nativeFee(transaction.fee, network.nativeCurrency), method: Arc4Methods.decodeCall(functions, transaction.appArgs),
  };
}

/** Gas fields do not apply to Algorand, so they are zero; a created application's ID is its contract address. */
function receipt(transaction: AvmTransaction, position: BlockPosition): ITransactionReceipt {
  return {
    transactionHash: transaction.id, blockHash: position.hash, blockNumber: position.round, transactionIndex: position.index,
    from: transaction.sender, to: transaction.receiver ?? null, contractAddress: transaction.createdAppId?.toString() ?? null,
    type: transaction.type, status: '1', cumulativeGasUsed: '0', gasUsed: '0', logsBloom: '', logs: logsOf(transaction, position),
  };
}

/** Application logs, as hex data of the application account; ARC-28 events start with their 4-byte selector. */
function logsOf(transaction: AvmTransaction, position: BlockPosition): ITransactionLog[] {
  const address = getApplicationAddress(transaction.createdAppId ?? transaction.appId).toString();
  return transaction.logs.map((log, index) => ({
    address, data: `0x${bytesToHex(log)}`, topics: [], logIndex: String(index), transactionIndex: position.index,
    transactionHash: transaction.id, blockHash: position.hash, blockNumber: position.round, removed: false,
  }));
}

async function positionOf(session: AlgodSession, transaction: AvmTransaction): Promise<BlockPosition> {
  const round = transaction.confirmedRound!.toString();
  const [hash, block, ids] = await Promise.all([
    session.get<{ blockHash: string }>(`v2/blocks/${round}/hash`), session.get<{ block: { ts?: bigint } }>(`v2/blocks/${round}?header-only=true&format=json`),
    session.get<{ blockTxids: string[] }>(`v2/blocks/${round}/txids`),
  ]);
  const timestamp = SharedResults.milliseconds(block.block.ts ?? 0n);
  return { round, hash: hash.blockHash, timestamp, index: String(Math.max(ids.blockTxids.indexOf(transaction.id), 0)) };
}
