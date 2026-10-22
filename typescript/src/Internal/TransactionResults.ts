// Builds the JSON-safe ITransaction of a mined transaction.
import type Big from 'big.js';
import type { Log, Numbers, TransactionReceipt } from 'web3';
import type { IEthereumProvider, ITransaction, ITransactionDetails, ITransactionLog, ITransactionReceipt } from '../Models.js';
import { transactionReverted } from './Errors.js';
import * as Integers from './Integers.js';
import * as Receipts from './Receipts.js';
import type { RpcSession } from './RpcSession.js';

const MILLISECONDS_PER_SECOND = 1000;

/** The caller's requestedAt as a decimal string, or the current time in milliseconds. */
export function requestedAt(value: string | undefined): string {
  return value === undefined ? String(Date.now()) : Integers.toDecimalString(value, 'requestedAt');
}

/** Fills result with the receipt, the outcome at block time and the exact fee. */
export async function fromReceipt(
  session: RpcSession, receipt: TransactionReceipt, result: ITransaction, provider: Readonly<IEthereumProvider>,
): Promise<ITransaction> {
  const tx = normalizeReceipt(receipt);
  const timestamp = await blockTimestamp(session, receipt.blockNumber);
  result.requestedAt = requestedAt(result.requestedAt);
  result.tx = tx;
  applyOutcome(result, tx, timestamp);
  result.nativeFee = await nativeFee(session, receipt, provider);
  return result;
}

/** Adds the receipt, the block time and the exact fee of a mined transaction. */
export async function addMinedDetails(
  session: RpcSession, details: ITransactionDetails, provider: Readonly<IEthereumProvider>,
): Promise<ITransactionDetails> {
  const receipt = details.pending ? undefined : await Receipts.get(session, details.hash);
  if (!receipt) return details;
  details.receipt = normalizeReceipt(receipt);
  details.timestamp = await blockTimestamp(session, receipt.blockNumber);
  details.nativeFee = await nativeFee(session, receipt, provider);
  return details;
}

/** Every field is set, so stale values from the caller's event are overwritten. */
function applyOutcome(result: ITransaction, tx: ITransactionReceipt, timestamp: string): void {
  const reverted = tx.status === '0';
  result.completedAt = reverted ? undefined : timestamp;
  result.rejectedAt = reverted ? timestamp : undefined;
  result.error = reverted ? transactionReverted(tx.transactionHash).toJSON() : undefined;
}

/** Block time in milliseconds, as a decimal string. */
async function blockTimestamp(session: RpcSession, blockNumber: Numbers): Promise<string> {
  const block = await session.withTimeout(session.web3.eth.getBlock(blockNumber));
  return Integers.toDecimalString(Integers.parse(block.timestamp, 'timestamp').times(MILLISECONDS_PER_SECOND));
}

/** Exact gas cost in the smallest unit. Nodes without effectiveGasPrice fall back to the transaction's gas price. */
async function nativeFee(session: RpcSession, receipt: TransactionReceipt, provider: Readonly<IEthereumProvider>): Promise<ITransaction['nativeFee']> {
  const gasPrice = optionalInteger(receipt.effectiveGasPrice) ?? await transactionGasPrice(session, String(receipt.transactionHash));
  if (gasPrice === undefined) return undefined;
  const amount = Integers.toDecimalString(Integers.parse(receipt.gasUsed, 'gasUsed').times(gasPrice));
  return { amount, symbol: provider.nativeCurrency.symbol, decimals: String(provider.nativeCurrency.decimals) };
}

async function transactionGasPrice(session: RpcSession, transactionHash: string): Promise<Big | undefined> {
  const transaction = await session.withTimeout(session.web3.eth.getTransaction(transactionHash));
  return optionalInteger(transaction?.gasPrice);
}

function normalizeReceipt(receipt: TransactionReceipt): ITransactionReceipt {
  return {
    transactionHash: String(receipt.transactionHash), blockHash: String(receipt.blockHash), from: String(receipt.from), to: receipt.to,
    blockNumber: Integers.toDecimalString(receipt.blockNumber), transactionIndex: Integers.toDecimalString(receipt.transactionIndex),
    contractAddress: receipt.contractAddress, type: Integers.toOptionalDecimalString(receipt.type),
    status: Integers.toDecimalString(receipt.status ?? 1),
    cumulativeGasUsed: Integers.toDecimalString(receipt.cumulativeGasUsed), gasUsed: Integers.toDecimalString(receipt.gasUsed),
    effectiveGasPrice: Integers.toOptionalDecimalString(receipt.effectiveGasPrice), logsBloom: String(receipt.logsBloom),
    logs: (receipt.logs ?? []).map(normalizeLog),
  };
}

function normalizeLog(log: Log): ITransactionLog {
  return {
    address: log.address, data: optionalString(log.data), topics: log.topics?.map(String),
    logIndex: Integers.toOptionalDecimalString(log.logIndex), transactionIndex: Integers.toOptionalDecimalString(log.transactionIndex),
    transactionHash: optionalString(log.transactionHash), blockHash: optionalString(log.blockHash),
    blockNumber: Integers.toOptionalDecimalString(log.blockNumber), removed: log.removed,
  };
}

function optionalInteger(value: unknown): Big | undefined {
  return value === undefined || value === null ? undefined : Integers.parse(value, 'gasPrice');
}

function optionalString(value: unknown): string | undefined {
  return value === undefined ? undefined : String(value);
}
