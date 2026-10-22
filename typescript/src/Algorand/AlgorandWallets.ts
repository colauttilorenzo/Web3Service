import { bytesToHex, generateAccount, getApplicationAddress, mnemonicToSecretKey, secretKeyToMnemonic } from 'algosdk';
import type { IWallet } from '../Models.js';
import { invalidArgument } from '../Internal/Errors.js';
import { applicationId } from './AlgorandContract.js';

const SEED_BYTES = 32;

/** Algorand accounts: 25-word mnemonics and the keys they encode. Algorand accounts are not derived from a path, so path is null. */
export class AlgorandWallets {
  /** A new account's 25-word mnemonic. Store it as securely as a private key. */
  static createMnemonic(): string {
    return secretKeyToMnemonic(generateAccount().sk);
  }

  /** The account of a 25-word mnemonic: its address, public key and private key (the 32-byte seed; the secret key is seed + public key) as 0x hex. */
  static getWallet(mnemonic: string): IWallet {
    const { addr, sk } = toAccount(mnemonic);
    return {
      address: addr.toString(), publicKey: `0x${bytesToHex(sk.slice(SEED_BYTES))}`, privateKey: `0x${bytesToHex(sk.slice(0, SEED_BYTES))}`,
      path: null, index: '0',
    };
  }

  /** The account of an application, which holds its Algos and assets: e.g. the recipient that a notarization contract expects. */
  static getApplicationAddress(appId: string): string {
    return getApplicationAddress(applicationId(appId)).toString();
  }
}

function toAccount(mnemonic: string): ReturnType<typeof mnemonicToSecretKey> {
  try {
    return mnemonicToSecretKey(mnemonic);
  } catch (cause) {
    throw invalidArgument(`The mnemonic is not a valid 25-word Algorand mnemonic: ${(cause as Error).message}`);
  }
}
