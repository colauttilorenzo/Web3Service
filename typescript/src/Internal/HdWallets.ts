// BIP-39 phrases and BIP-44 Ethereum wallets.
import { HDNodeWallet, Mnemonic, randomBytes } from 'ethers';
import type { IWallet } from '../Models.js';
import { invalidArgument } from './Errors.js';

/** 16 bytes of entropy give a 12-word BIP-39 phrase. */
const MNEMONIC_ENTROPY_BYTES = 16;
const MAX_WALLET_INDEX = 2 ** 31 - 1;

export function createMnemonic(): Mnemonic {
  return Mnemonic.fromEntropy(randomBytes(MNEMONIC_ENTROPY_BYTES));
}

export function getMnemonic(phrase: string, password = ''): Mnemonic {
  return Mnemonic.fromPhrase(phrase, password);
}

/** Derives the wallet at m/44'/60'/0'/0/{index}. */
export function derive(mnemonic: Mnemonic, index: number): IWallet {
  if (!Number.isInteger(index) || index < 0 || index > MAX_WALLET_INDEX) {
    throw invalidArgument(`walletIndex must be an integer between 0 and ${MAX_WALLET_INDEX}.`);
  }
  const { address, publicKey, privateKey, path } = HDNodeWallet.fromMnemonic(mnemonic, `m/44'/60'/0'/0/${index}`);
  return { address, publicKey, privateKey, path, index: String(index) };
}
