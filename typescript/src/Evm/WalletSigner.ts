import type Big from 'big.js';
import type { ChainId, Eip1193Provider, IAvmWallet, IEthereumProvider } from '../Models.js';
import { Web3Error } from '../Web3Exception.js';
import type { ResolvedOptions } from '../Internal/Configuration.js';
import { invalidArgument } from '../Internal/Errors.js';
import * as Integers from '../Internal/Integers.js';
import type { RpcSession } from './RpcSession.js';
import type { TransactionRequest, TransactionSigner } from './TransactionRequest.js';

/** EIP-1193 error codes. */
const USER_REJECTED = 4001;
const UNRECOGNIZED_CHAIN = 4902;
const TRANSACTION_HASH = /^0x[0-9a-f]{64}$/i;
const HTTP_URI = /^https?:/i;

/**
 * The user's wallet (e.g. MetaMask) signs and broadcasts, so the private key never reaches the page.
 * Fees are left to the wallet unless the caller sets them.
 */
export class WalletSigner implements TransactionSigner {
  readonly #wallet: Eip1193Provider;
  readonly #switchChain: boolean;
  readonly #provider: Readonly<IEthereumProvider>;
  readonly #gasBuffer: Big;

  private constructor(wallet: Eip1193Provider, switchChain: boolean, provider: Readonly<IEthereumProvider>, gasBuffer: Big) {
    this.#wallet = wallet;
    this.#switchChain = switchChain;
    this.#provider = provider;
    this.#gasBuffer = gasBuffer;
  }

  /** The wallet cannot be combined with a private key or a mnemonic. */
  static create({ signer, gasBuffer }: ResolvedOptions, provider: Readonly<IEthereumProvider>): WalletSigner {
    if (signer?.privateKey || signer?.mnemonic) throw invalidArgument('Provide exactly one signer credential.');
    return new WalletSigner(eip1193(signer!.wallet!), signer!.switchChain ?? true, provider, gasBuffer);
  }

  async send(session: RpcSession, request: TransactionRequest): Promise<string> {
    const from = await withWalletErrors('The wallet could not be used.', () => this.#connectAccount());
    const transaction = await this.#transaction(session, request, from);
    const hash = await withWalletErrors('The wallet did not submit the transaction.',
      () => this.#wallet.request({ method: 'eth_sendTransaction', params: [transaction] }));
    return requireTransactionHash(hash);
  }

  /** The wallet's first account, once the wallet is on the configured chain. */
  async #connectAccount(): Promise<string> {
    const accounts = await this.#wallet.request({ method: 'eth_requestAccounts' });
    const from = Array.isArray(accounts) ? accounts[0] : undefined;
    if (typeof from !== 'string') throw new Web3Error('SIGNER_REQUIRED', 'The wallet did not provide an account.');
    await this.#ensureChain();
    return from;
  }

  async #ensureChain(): Promise<void> {
    const chainId = String(this.#provider.chainId);
    if (await this.#walletChainId() === chainId) return;
    if (!this.#switchChain) throw new Web3Error('CHAIN_ID_MISMATCH', `The wallet is not connected to chain ${chainId}.`);
    await this.#switchWalletChain();
    const actual = await this.#walletChainId();
    if (actual !== chainId) throw new Web3Error('CHAIN_ID_MISMATCH', `Expected wallet chain ${chainId}, received ${actual}.`);
  }

  async #walletChainId(): Promise<string> {
    return Integers.normalizeChainId(await this.#wallet.request({ method: 'eth_chainId' }) as ChainId);
  }

  /** Switches the wallet to the configured chain, adding the chain first (EIP-3085) when the wallet does not know it. */
  async #switchWalletChain(): Promise<void> {
    try {
      await this.#wallet.request({ method: 'wallet_switchEthereumChain', params: [{ chainId: String(this.#provider.chainId) }] });
    } catch (error) {
      if (walletErrorCode(error) !== UNRECOGNIZED_CHAIN) throw error;
      await this.#wallet.request({ method: 'wallet_addEthereumChain', params: [addChainParameters(this.#provider)] });
    }
  }

  async #transaction(session: RpcSession, request: TransactionRequest, from: string): Promise<Record<string, string>> {
    const { data, to, options } = request;
    const value = Integers.parse(options.value ?? 0, 'value');
    const gas = await request.gasLimit(session, from, value, this.#gasBuffer);
    return {
      from, data, value: Integers.toHex(value), gas: Integers.toHex(gas),
      ...(to ? { to } : {}), ...optionalHex('gasPrice', options.gasPrice), ...optionalHex('nonce', options.nonce),
    };
  }
}

/** Runs a wallet request and maps its failure to USER_REJECTED or WALLET_ERROR. */
async function withWalletErrors<T>(message: string, action: () => Promise<T>): Promise<T> {
  try {
    return await action();
  } catch (cause) {
    if (cause instanceof Web3Error) throw cause;
    if (walletErrorCode(cause) === USER_REJECTED) throw new Web3Error('USER_REJECTED', 'The request was rejected in the wallet.', { cause });
    throw new Web3Error('WALLET_ERROR', message, { cause });
  }
}

/** The EIP-1193 code, read from the error itself or from the original error that web3.js wraps. */
function walletErrorCode(error: unknown): number | undefined {
  const { code, data } = (error ?? {}) as { code?: unknown; data?: { originalError?: { code?: unknown } } };
  const value = code ?? data?.originalError?.code;
  return typeof value === 'number' ? value : undefined;
}

/** EIP-3085 chain description built from the service configuration. Wallets accept only HTTP(S) RPC URLs. */
function addChainParameters(provider: Readonly<IEthereumProvider>): Record<string, unknown> {
  const { chainId, chainName, nativeCurrency: { name, symbol, decimals }, rpcPublicURI, rpcURI, blockExplorerURI } = provider;
  return {
    chainId,
    chainName: chainName ?? `Chain ${chainId}`,
    nativeCurrency: { name: name ?? symbol, symbol, decimals },
    rpcUrls: [rpcPublicURI, rpcURI].filter(isHttpUri),
    ...(blockExplorerURI ? { blockExplorerUrls: [blockExplorerURI] } : {}),
  };
}

function optionalHex(name: string, value: ChainId | undefined): Record<string, string> {
  return value === undefined ? {} : { [name]: Integers.toHex(Integers.parse(value, name)) };
}

function isHttpUri(uri: string | undefined): uri is string {
  return !!uri && HTTP_URI.test(uri);
}

function requireTransactionHash(value: unknown): string {
  if (typeof value === 'string' && TRANSACTION_HASH.test(value)) return value;
  throw new Web3Error('TRANSACTION_SUBMISSION_FAILED', 'The wallet returned an invalid transaction hash.');
}

/** An Algorand wallet cannot sign EVM transactions. */
function eip1193(wallet: Eip1193Provider | IAvmWallet): Eip1193Provider {
  if (!('request' in wallet)) throw invalidArgument('An EVM wallet must be an EIP-1193 provider, such as window.ethereum.');
  return wallet;
}
