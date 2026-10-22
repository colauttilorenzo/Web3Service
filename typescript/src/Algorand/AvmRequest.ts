import type { BoxReference, SuggestedParams, Transaction } from 'algosdk';
import type { TransactionOptions } from '../Models.js';

/** The accounts, applications, assets and boxes an application call reads, found by simulation. */
export interface Resources {
  readonly accounts: readonly string[];
  readonly apps: readonly bigint[];
  readonly assets: readonly bigint[];
  readonly boxes: readonly BoxReference[];
}

/** The fee of the last transaction of the group, and the resources it needs. */
export interface BuildPlan {
  readonly fee: bigint;
  readonly resources?: Resources;
}

/** A group of transactions to sign and send, built for a sender. The last transaction is the one the result describes. */
export interface AvmRequest {
  readonly options: TransactionOptions;
  /** True when the group calls an application, so simulation must find its resources and inner transactions. */
  readonly callsApplication: boolean;
  build(sender: string, params: SuggestedParams, plan: BuildPlan): Transaction[];
  /** The reason of a failed simulation, with the contract's error message when it has one. */
  explain(failure: string): string;
}

export const NO_RESOURCES: Resources = { accounts: [], apps: [], assets: [], boxes: [] };
