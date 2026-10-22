import type Big from 'big.js';
import type { TransactionOptions } from '../Models.js';
import * as Integers from '../Internal/Integers.js';
import type { RpcSession } from './RpcSession.js';

/** Estimates the gas for a sender address and a value. */
export type EstimateGas = (from: string, value: Big) => Promise<unknown>;

/** An encoded contract call or deployment, ready to be signed. */
export class TransactionRequest {
  readonly data: string;
  /** Recipient contract; undefined for deployments. */
  readonly to: string | undefined;
  readonly estimateGas: EstimateGas;
  readonly options: TransactionOptions;

  constructor(data: string, to: string | undefined, estimateGas: EstimateGas, options: TransactionOptions | undefined) {
    this.data = data;
    this.to = to;
    this.estimateGas = estimateGas;
    this.options = options ?? {};
  }

  /** The explicit gas limit, or the node's estimate plus the configured buffer. */
  async gasLimit(session: RpcSession, from: string, value: Big, gasBuffer: Big): Promise<Big> {
    if (this.options.gas !== undefined) return Integers.parse(this.options.gas, 'gas');
    const estimate = await session.quantity(this.estimateGas(from, value));
    return estimate.plus(gasBuffer);
  }
}

/** Signs and broadcasts a request, resolving with the transaction hash once the network has accepted it. */
export interface TransactionSigner {
  send(session: RpcSession, request: TransactionRequest): Promise<string>;
}
