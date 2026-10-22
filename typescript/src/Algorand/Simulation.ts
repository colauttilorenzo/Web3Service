// Simulates a group before it is signed: finds the resources an application reads, prices its inner transactions,
// and rejects calls that would fail, so nothing is sent or paid for.
import { assignGroupID, base64ToBytes, encodeUnsignedSimulateTransaction, msgpackRawDecode, msgpackRawEncode } from 'algosdk';
import type { SuggestedParams, Transaction } from 'algosdk';
import type { TransactionOptions } from '../Models.js';
import { Web3Error } from '../Web3Exception.js';
import * as Integers from '../Internal/Integers.js';
import type { AlgodSession } from './AlgodSession.js';
import type { AvmRequest, Resources } from './AvmRequest.js';
import type { PendingTransaction } from './Transactions.js';

/** Inner transactions an application may send in one group; the simulation fee covers them. */
const MAX_INNER_TRANSACTIONS = 256n;

/** POST /v2/transactions/simulate?format=json, for a single group. */
interface SimulateResponse {
  readonly 'txn-groups': readonly [{
    readonly 'txn-results': readonly { readonly 'txn-result': PendingTransaction; readonly 'unnamed-resources-accessed'?: UnnamedResources }[];
    readonly 'failure-message'?: string;
    readonly 'unnamed-resources-accessed'?: UnnamedResources;
  }];
}

/** Resources an application read without the call naming them; box names are base64. */
interface UnnamedResources {
  readonly accounts?: readonly string[];
  readonly apps?: readonly bigint[];
  readonly assets?: readonly bigint[];
  readonly boxes?: readonly { readonly app: bigint; readonly name: string }[];
  readonly 'extra-box-refs'?: bigint;
  readonly 'app-locals'?: readonly { readonly account: string; readonly app: bigint }[];
  readonly 'asset-holdings'?: readonly { readonly account: string; readonly asset: bigint }[];
}

/** The outcome of a simulated group: the result of its last transaction and what the group needs to succeed. */
export interface Simulated {
  readonly result: PendingTransaction;
  readonly resources: Resources;
  readonly innerTransactions: bigint;
  /** Why the group would fail. */
  readonly failure?: string;
  /**
   * True when a transaction would fail: an application rejects it or its sender cannot pay. False when only the group fee is short,
   * as for reads, which simulate without a fee because they are never sent; algod names the transaction in every transaction failure.
   */
  readonly transactionFailed: boolean;
}

/** The group ready to sign: with the resources found by simulation, the exact fee, and a group ID. */
export async function plan(session: AlgodSession, request: AvmRequest, sender: string): Promise<Transaction[]> {
  if (!request.callsApplication) return assignGroupID(request.build(sender, session.params, { fee: feeOf(request.options, session.params, 0n) }));
  const draft = request.build(sender, session.params, { fee: await simulationFee(session, sender) });
  const simulated = await run(session, draft);
  if (simulated.failure) throw rejected(draft.at(-1)!.txID(), request.explain(simulated.failure));
  const fee = feeOf(request.options, session.params, simulated.innerTransactions);
  return assignGroupID(request.build(sender, session.params, { fee, resources: simulated.resources }));
}

/** Runs a group without signatures, allowing the resources the application reads to be left out. Assigns the group ID. */
export async function run(session: AlgodSession, transactions: Transaction[]): Promise<Simulated> {
  const response = await session.post<SimulateResponse>('v2/transactions/simulate?format=json', requestOf(transactions), 'application/msgpack');
  const group = response['txn-groups'][0];
  const last = group['txn-results'].at(-1)!;
  const failure = group['failure-message'] || undefined;
  return {
    result: last['txn-result'], resources: resourcesOf(group['unnamed-resources-accessed'], last['unnamed-resources-accessed']),
    innerTransactions: countInner(last['txn-result']), failure, transactionFailed: !!failure && transactions.some(txn => failure.includes(txn.txID())),
  };
}

/** The application rejected the call: it is never sent, so it costs nothing. */
export function rejected(transactionHash: string, failure: string): Web3Error {
  return new Web3Error('TRANSACTION_REVERTED', `The transaction would be rejected: ${failure}`, { transactionHash });
}

/** options.gasPrice is the explicit fee; otherwise the minimum fee for the transaction and each inner transaction. */
function feeOf(options: TransactionOptions, params: SuggestedParams, innerTransactions: bigint): bigint {
  if (options.gasPrice !== undefined) return BigInt(Integers.parse(options.gasPrice, 'gasPrice').toFixed(0));
  return BigInt(params.minFee) * (1n + innerTransactions);
}

/** Enough fee for any inner transaction, without exceeding what the sender can spend: an overspent simulation never runs the application. */
async function simulationFee(session: AlgodSession, sender: string): Promise<bigint> {
  const account = await session.get<{ amount: bigint; 'min-balance': bigint }>(`v2/accounts/${sender}?exclude=all&format=json`);
  const minFee = BigInt(session.params.minFee);
  const spendable = account.amount - account['min-balance'] - minFee;
  return spendable < minFee ? minFee : bigintMin(minFee * (1n + MAX_INNER_TRANSACTIONS), spendable);
}

/** The simulate request in msgpack: the group, without signatures, with the group ID assigned. */
function requestOf(transactions: Transaction[]): Uint8Array {
  const txns = assignGroupID(transactions).map(txn => msgpackRawDecode(encodeUnsignedSimulateTransaction(txn)));
  return msgpackRawEncode({ 'txn-groups': [{ txns }], 'allow-empty-signatures': true, 'allow-unnamed-resources': true });
}

function countInner(result: PendingTransaction): bigint {
  return (result['inner-txns'] ?? []).reduce((total, inner) => total + 1n + countInner(inner), 0n);
}

/** The group's and the transaction's unnamed resources; local states and holdings need their account and application or asset. */
function resourcesOf(...found: (UnnamedResources | undefined)[]): Resources {
  const all = found.filter((item): item is UnnamedResources => !!item);
  return {
    accounts: all.flatMap(item => [...item.accounts ?? [], ...(item['app-locals'] ?? []).map(local => local.account),
      ...(item['asset-holdings'] ?? []).map(holding => holding.account)]),
    apps: all.flatMap(item => [...item.apps ?? [], ...(item['app-locals'] ?? []).map(local => local.app)]),
    assets: all.flatMap(item => [...item.assets ?? [], ...(item['asset-holdings'] ?? []).map(holding => holding.asset)]),
    boxes: all.flatMap(item => [...(item.boxes ?? []).map(box => ({ appIndex: box.app, name: base64ToBytes(box.name) })),
      ...Array.from({ length: Number(item['extra-box-refs'] ?? 0n) }, () => ({ appIndex: 0, name: new Uint8Array() }))]),
  };
}

function bigintMin(a: bigint, b: bigint): bigint {
  return a < b ? a : b;
}
