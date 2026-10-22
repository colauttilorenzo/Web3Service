// The caller's event passed to every write, shared by every blockchain.
import type { ITransaction } from '../Models.js';

/** A copy of the caller's event, named after the operation unless the caller named it. */
export function named(onChainEvent: ITransaction | undefined, name: string): ITransaction {
  return { ...onChainEvent, name: onChainEvent?.name ?? name };
}
