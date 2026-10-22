// Signs Algorand transactions with a local key (25-word mnemonic or private key) or with the user's wallet.
import { base64ToBytes, encodeAddress, encodeUnsignedTransaction, hexToBytes, mnemonicFromSeed, mnemonicToSecretKey } from 'algosdk';
import type { Transaction } from 'algosdk';
import type { IAvmWallet, SignerOptions } from '../Models.js';
import { Web3Error } from '../Web3Exception.js';
import { invalidArgument, signerRequired } from '../Internal/Errors.js';

/** The account that signs, and how it signs a group of transactions. */
export interface AvmSigner {
  readonly address: string;
  sign(transactions: readonly Transaction[]): Promise<Uint8Array[]>;
}

const SEED_HEX = /^0x[0-9a-f]{64}$/i;
const SECRET_KEY_BYTES = 64;

/** The configured wallet, mnemonic or private key; exactly one of them. */
export function create(signer: SignerOptions | undefined): AvmSigner {
  if (signer?.wallet) return walletSigner(signer as { wallet: IAvmWallet });
  const hasPrivateKey = !!signer?.privateKey;
  const hasMnemonic = !!signer?.mnemonic;
  if (hasPrivateKey && hasMnemonic) throw invalidArgument('Provide exactly one signer credential.');
  if (hasPrivateKey) return keySigner(secretKeyOf(signer!.privateKey!));
  if (hasMnemonic) return keySigner(mnemonicToSecretKey(phraseOf(signer!.mnemonic!)).sk);
  throw signerRequired();
}

/** A 32-byte seed as 0x hex, or a 64-byte secret key as base64 (algosdk's account.sk). */
export function secretKeyOf(privateKey: string): Uint8Array {
  if (SEED_HEX.test(privateKey)) return mnemonicToSecretKey(mnemonicFromSeed(hexToBytes(privateKey.slice(2)))).sk;
  const bytes = tryBase64(privateKey);
  if (bytes?.length !== SECRET_KEY_BYTES) throw invalidArgument('An Algorand private key is a 0x-prefixed 32-byte seed or a base64 64-byte secret key.');
  return bytes;
}

function keySigner(secretKey: Uint8Array): AvmSigner {
  const address = encodeAddress(secretKey.slice(SECRET_KEY_BYTES / 2));
  return { address, sign: async transactions => transactions.map(transaction => transaction.signTxn(secretKey)) };
}

/** The wallet signs every transaction of the group; a transaction it leaves unsigned is a rejection. */
function walletSigner(signer: { wallet: IAvmWallet }): AvmSigner {
  const { wallet } = signer;
  if (typeof wallet.address !== 'string' || typeof wallet.signTransactions !== 'function') {
    throw invalidArgument('On Algorand the wallet must have an address and signTransactions, e.g. from @txnlab/use-wallet.');
  }
  return { address: wallet.address, sign: transactions => signWithWallet(wallet, transactions) };
}

async function signWithWallet(wallet: IAvmWallet, transactions: readonly Transaction[]): Promise<Uint8Array[]> {
  let signed: (Uint8Array | null)[];
  try {
    signed = await wallet.signTransactions(transactions.map(encodeUnsignedTransaction));
  } catch (cause) {
    throw new Web3Error('WALLET_ERROR', 'The wallet did not sign the transactions.', { cause });
  }
  if (signed.length !== transactions.length || signed.some(item => !item)) throw new Web3Error('USER_REJECTED', 'The request was rejected in the wallet.');
  return signed as Uint8Array[];
}

function phraseOf(mnemonic: string | { phrase: string }): string {
  return typeof mnemonic === 'string' ? mnemonic : mnemonic.phrase;
}

function tryBase64(value: string): Uint8Array | undefined {
  try {
    return base64ToBytes(value);
  } catch {
    return undefined;
  }
}
