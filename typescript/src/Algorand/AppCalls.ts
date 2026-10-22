// Application calls and creations as transaction groups: the payment of a payable method first, then the call.
import {
  ABIType, OnApplicationComplete, getApplicationAddress, makeApplicationCallTxnFromObject, makePaymentTxnWithSuggestedParamsFromObject,
} from 'algosdk';
import type { SuggestedParams, Transaction } from 'algosdk';
import type { TransactionOptions } from '../Models.js';
import { invalidArgument } from '../Internal/Errors.js';
import * as Integers from '../Internal/Integers.js';
import * as Arc4Methods from './Arc4Methods.js';
import type { Arc4Function, CallArgument, EncodedValue } from './Arc4Methods.js';
import { NO_RESOURCES } from './AvmRequest.js';
import * as ProgramErrors from './ProgramErrors.js';
import type { AvmRequest, BuildPlan, Resources } from './AvmRequest.js';

/** Programs and state schema of a new application. */
export interface Creation {
  readonly approvalProgram: Uint8Array;
  readonly clearProgram: Uint8Array;
  readonly numGlobalInts: number;
  readonly numGlobalByteSlices: number;
  readonly numLocalInts: number;
  readonly numLocalByteSlices: number;
  readonly extraPages: number;
}

/** A call of an ARC-4 method, or a bare call without arguments when fn is undefined; appId 0 with creation creates the application. */
export interface AppCall {
  readonly appId: bigint;
  readonly fn?: Arc4Function;
  readonly args: readonly CallArgument[];
  readonly creation?: Creation;
}

/** The foreign arrays the references of the arguments point into, and the values to encode. */
interface EncodedCall {
  readonly appArgs: Uint8Array[];
  readonly references: Resources;
}

const UINT8 = ABIType.from('uint8');

export function request(call: AppCall, options: TransactionOptions = {}): AvmRequest {
  const payment = paymentOf(call, options);
  return {
    options, callsApplication: true,
    build: (sender, params, plan) => [...paymentTransactions(call, sender, params, payment), callTransaction(call, sender, params, plan)],
    explain: failure => (call.fn ? ProgramErrors.explain(call.fn.errors, failure) : failure),
  };
}

/** The application arguments of a call; references to accounts other than the sender count from 1. */
export function applicationArgs(call: AppCall): Uint8Array[] {
  return encodeCall(call, '').appArgs;
}

/** A payment to the application account, e.g. to fund the boxes it creates. */
export function fund(appId: bigint, amount: bigint, options: TransactionOptions = {}): AvmRequest {
  return {
    options, callsApplication: false, explain: failure => failure,
    build: (sender, params, plan) => [makePaymentTxnWithSuggestedParamsFromObject({
      sender, receiver: getApplicationAddress(appId), amount, suggestedParams: withFee(params, plan.fee),
    })],
  };
}

function callTransaction(call: AppCall, sender: string, params: SuggestedParams, plan: BuildPlan): Transaction {
  const encoded = encodeCall(call, sender);
  const resources = merge(encoded.references, plan.resources ?? NO_RESOURCES);
  return makeApplicationCallTxnFromObject({
    sender, appIndex: call.appId, onComplete: OnApplicationComplete.NoOpOC, appArgs: encoded.appArgs, suggestedParams: withFee(params, plan.fee),
    accounts: [...resources.accounts], foreignApps: [...resources.apps], foreignAssets: [...resources.assets], boxes: [...resources.boxes],
    ...call.creation,
  });
}

function paymentTransactions(call: AppCall, sender: string, params: SuggestedParams, amount: bigint | undefined): Transaction[] {
  if (amount === undefined) return [];
  const receiver = getApplicationAddress(call.appId);
  return [makePaymentTxnWithSuggestedParamsFromObject({ sender, receiver, amount, suggestedParams: withFee(params, BigInt(params.minFee)) })];
}

/** options.value pays a payable method; other methods take no coins. */
function paymentOf(call: AppCall, options: TransactionOptions): bigint | undefined {
  const value = options.value === undefined ? 0n : BigInt(Integers.parse(options.value, 'value').toFixed(0));
  const payable = call.args.some(arg => arg.kind === 'payment');
  if (payable && call.appId === 0n) throw invalidArgument('A payable method cannot create the application; fund it after deployment with options.value.');
  if (!payable && value > 0n) throw invalidArgument(`${call.fn?.method.signature ?? 'The call'} is not payable; options.value cannot be sent.`);
  return payable ? value : undefined;
}

/** Account 0 is the sender and application 0 the called application, so their references need no foreign entry. */
function encodeCall(call: AppCall, sender: string): EncodedCall {
  const references = { accounts: [] as string[], apps: [] as bigint[], assets: [] as bigint[], boxes: [] };
  const values = call.args.flatMap((arg): EncodedValue[] => {
    if (arg.kind === 'payment') return [];
    if (arg.kind === 'value') return [[arg.type, arg.value]];
    if (arg.kind === 'account') return [[UINT8, arg.value === sender ? 0 : indexOf(references.accounts, arg.value) + 1]];
    if (arg.kind === 'asset') return [[UINT8, indexOf(references.assets, arg.value)]];
    return [[UINT8, arg.value === call.appId ? 0 : indexOf(references.apps, arg.value) + 1]];
  });
  return { appArgs: call.fn ? Arc4Methods.encode(call.fn, values) : [], references };
}

/** The references of the arguments keep their positions; the resources found by simulation follow. */
function merge(references: Resources, found: Resources): Resources {
  return {
    accounts: unique([...references.accounts, ...found.accounts]), apps: unique([...references.apps, ...found.apps]),
    assets: unique([...references.assets, ...found.assets]), boxes: [...found.boxes],
  };
}

function indexOf<T>(items: T[], item: T): number {
  if (!items.includes(item)) items.push(item);
  return items.indexOf(item);
}

function unique<T>(items: readonly T[]): T[] {
  return [...new Set(items)];
}

function withFee(params: SuggestedParams, fee: bigint): SuggestedParams {
  return { ...params, fee, flatFee: true };
}
