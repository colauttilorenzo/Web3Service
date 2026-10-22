// Transactions read by hash: the RPC object as JSON-safe ITransactionDetails, with the called function decoded from the ABI.
import type { ITransactionDetails } from '../Models.js';
import type { AbiFunction } from './AbiMethods.js';
import * as ContractCalls from './ContractCalls.js';
import { transactionNotFound } from './Errors.js';
import * as Integers from './Integers.js';
import type { RpcSession } from './RpcSession.js';

/** eth_getTransactionByHash result; quantities are hex strings, block fields are null while pending. */
interface RpcTransaction {
  hash: string; from: string; to?: string | null; value: string; nonce: string; gas: string; gasPrice?: string;
  maxFeePerGas?: string; maxPriorityFeePerGas?: string; input: string; type?: string;
  blockHash?: string | null; blockNumber?: string | null; transactionIndex?: string | null;
}

export async function get(session: RpcSession, transactionHash: string): Promise<RpcTransaction> {
  const request = session.web3.requestManager.send({ method: 'eth_getTransactionByHash', params: [transactionHash] });
  const transaction = await session.withTimeout(request) as RpcTransaction | null;
  if (!transaction) throw transactionNotFound(transactionHash);
  return transaction;
}

export function fromRpc(transaction: RpcTransaction, functions: readonly AbiFunction[]): ITransactionDetails {
  const optional = Integers.toOptionalDecimalString;
  return {
    hash: transaction.hash, from: transaction.from, to: transaction.to ?? undefined, value: Integers.toDecimalString(transaction.value),
    nonce: Integers.toDecimalString(transaction.nonce), gas: Integers.toDecimalString(transaction.gas), gasPrice: optional(transaction.gasPrice),
    maxFeePerGas: optional(transaction.maxFeePerGas), maxPriorityFeePerGas: optional(transaction.maxPriorityFeePerGas),
    input: transaction.input, type: optional(transaction.type), blockHash: transaction.blockHash ?? undefined,
    blockNumber: optional(transaction.blockNumber), transactionIndex: optional(transaction.transactionIndex),
    pending: !transaction.blockNumber, method: ContractCalls.decodeCall(functions, transaction.input),
  };
}
