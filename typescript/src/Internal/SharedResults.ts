// Parts of a transaction result shared by every blockchain.
import type { ITransaction, NativeCurrency } from '../Models.js';
import * as Integers from './Integers.js';

const MILLISECONDS_PER_SECOND = 1000;

/** The caller's requestedAt as a decimal string, or the current time in milliseconds. */
export function requestedAt(value: string | undefined): string {
  return value === undefined ? String(Date.now()) : Integers.toDecimalString(value, 'requestedAt');
}

/** A fee in the smallest unit of the native currency (wei, microAlgos, satoshis). */
export function nativeFee(amount: unknown, currency: Readonly<NativeCurrency>): NonNullable<ITransaction['nativeFee']> {
  return { amount: Integers.toDecimalString(amount), symbol: currency.symbol, decimals: String(currency.decimals) };
}

/** A block time in seconds as milliseconds, as a decimal string. */
export function milliseconds(seconds: unknown): string {
  return Integers.toDecimalString(Integers.parse(seconds, 'timestamp').times(MILLISECONDS_PER_SECOND));
}
