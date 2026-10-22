import type Big from 'big.js';
import { computeAddress } from 'ethers';
import type { Mnemonic } from 'ethers';
import type { TransactionOptions } from '../Models.js';
import type { ResolvedOptions } from './Configuration.js';
import { invalidArgument, signerRequired, submissionFailed } from './Errors.js';
import * as HdWallets from './HdWallets.js';
import * as Integers from './Integers.js';
import * as Receipts from './Receipts.js';
import type { RpcSession } from './RpcSession.js';
import type { TransactionRequest, TransactionSigner } from './TransactionRequest.js';

/** A legacy (type 0) transaction, as web3.js signs it. */
type LegacyTransaction = Record<string, string>;

/** The signing key, its address and the chain it signs for. */
interface Account {
  readonly privateKey: string;
  readonly address: string;
  readonly chainId: string;
}

/** Signs legacy (type 0) transactions locally with a private key and broadcasts them through the RPC node. */
export class KeySigner implements TransactionSigner {
  readonly #account: Account;
  readonly #gasBuffer: Big;

  private constructor(account: Account, gasBuffer: Big) {
    this.#account = account;
    this.#gasBuffer = gasBuffer;
  }

  static create(options: ResolvedOptions, chainId: string): KeySigner {
    const privateKey = KeySigner.#privateKey(options);
    return new KeySigner({ privateKey, address: computeAddress(privateKey), chainId }, options.gasBuffer);
  }

  /** Signs and broadcasts, returning the hash once the node has accepted (or already mined) the transaction. */
  async send(session: RpcSession, request: TransactionRequest): Promise<string> {
    const rawTransaction = await this.#sign(session, request);
    const transactionHash = session.web3.utils.keccak256(rawTransaction);
    await KeySigner.#broadcast(session, rawTransaction, transactionHash);
    return transactionHash;
  }

  /** The configured private key, or the one derived from the mnemonic at the configured wallet index. */
  static #privateKey({ signer }: ResolvedOptions): string {
    const hasPrivateKey = !!signer?.privateKey;
    const hasMnemonic = !!signer?.mnemonic;
    if (hasPrivateKey && hasMnemonic) throw invalidArgument('Provide exactly one signer credential.');
    if (hasPrivateKey) return signer!.privateKey!;
    if (hasMnemonic) return HdWallets.derive(KeySigner.#mnemonicOf(signer!.mnemonic!), signer!.walletIndex ?? 1).privateKey;
    throw signerRequired();
  }

  static #mnemonicOf(mnemonic: string | Pick<Mnemonic, 'phrase' | 'password'>): Mnemonic {
    return typeof mnemonic === 'string' ? HdWallets.getMnemonic(mnemonic) : HdWallets.getMnemonic(mnemonic.phrase, mnemonic.password);
  }

  /**
   * After a transport error, a mined receipt proves the broadcast went through:
   * a second transaction is never signed or sent.
   */
  static async #broadcast(session: RpcSession, rawTransaction: string, transactionHash: string): Promise<void> {
    try {
      await session.withTimeout(session.web3.requestManager.send({ method: 'eth_sendRawTransaction', params: [rawTransaction] }));
    } catch (error) {
      if (await Receipts.tryGet(session, transactionHash) === undefined) throw submissionFailed(transactionHash, error);
    }
  }

  async #sign(session: RpcSession, request: TransactionRequest): Promise<string> {
    const transaction = await this.#legacyTransaction(session, request);
    return (await session.web3.eth.accounts.signTransaction(transaction, this.#account.privateKey)).rawTransaction;
  }

  async #legacyTransaction(session: RpcSession, request: TransactionRequest): Promise<LegacyTransaction> {
    const { address, chainId } = this.#account;
    const value = Integers.parse(request.options.value ?? 0, 'value');
    return {
      from: address, ...(request.to ? { to: request.to } : {}), data: request.data, type: '0x0', value: value.toFixed(0), chainId,
      gas: (await request.gasLimit(session, address, value, this.#gasBuffer)).toFixed(0),
      gasPrice: (await KeySigner.#gasPrice(session, request.options)).toFixed(0),
      nonce: (await this.#nonce(session, request.options)).toFixed(0),
    };
  }

  static async #gasPrice(session: RpcSession, options: TransactionOptions): Promise<Big> {
    if (options.gasPrice !== undefined) return Integers.parse(options.gasPrice, 'gasPrice');
    return session.quantity(session.web3.eth.getGasPrice());
  }

  async #nonce(session: RpcSession, options: TransactionOptions): Promise<Big> {
    if (options.nonce !== undefined) return Integers.parse(options.nonce, 'nonce');
    return session.quantity(session.web3.eth.getTransactionCount(this.#account.address, 'pending'));
  }
}
