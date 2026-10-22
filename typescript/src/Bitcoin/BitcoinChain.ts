import type {
  ContractResult, IBitcoinNetwork, IBitcoinProvider, ICheckResult, ITokenCreation, ITokenInfo, ITransaction, ITransactionDetails, Web3ServiceOptions,
} from '../Models.js';
import type { IWeb3Service } from '../IWeb3Service.js';
import type { Web3Contract } from '../Web3Contract.js';
import * as Configuration from '../Internal/Configuration.js';
import type { NetworkDefaults } from '../Internal/Configuration.js';
import { notImplemented } from '../Internal/Errors.js';
import { EsploraConnector } from './EsploraSession.js';
import * as Transactions from './Transactions.js';

const DEFAULTS: NetworkDefaults = { chainId: 'mainnet', chainName: 'Bitcoin', currency: { name: 'Bitcoin', symbol: 'BTC', decimals: 8 } };
const NO_CONTRACTS = 'Bitcoin has no smart contracts.';
const NEXT_BLOCK = '1';
const MIN_RELAY_FEE = 1;

/**
 * Bitcoin through an Esplora REST API: reads blocks, fees and transactions. Bitcoin has no smart contracts or tokens,
 * so contract, deploy, mint and token operations fail with NotImplementedError.
 */
export class BitcoinChain implements IWeb3Service {
  readonly kind = 'bitcoin';
  readonly provider: Readonly<IBitcoinNetwork>;
  readonly #esplora: EsploraConnector;

  constructor(input: IBitcoinProvider, options?: Web3ServiceOptions) {
    const network = Configuration.resolveNetwork(input, DEFAULTS);
    this.provider = Object.freeze({ ...network, kind: 'bitcoin', chainId: Configuration.requireChainName(input.chainId, DEFAULTS.chainId) });
    this.#esplora = new EsploraConnector(this.provider, Configuration.resolveOptions(options).timeoutMs);
  }

  /** The network name, e.g. mainnet or testnet4. */
  get chainId(): string {
    return this.provider.chainId;
  }

  /** The block height and the fee rate for the next block, in satoshis per virtual byte. */
  async check(): Promise<ICheckResult> {
    return this.#esplora.use(async session => {
      const estimates = await session.json<Record<string, number>>('/fee-estimates') ?? {};
      return { blockNumber: await session.text('/blocks/tip/height'), gasPrice: String(Math.ceil(estimates[NEXT_BLOCK] ?? MIN_RELAY_FEE)) };
    });
  }

  getContract(): Web3Contract {
    throw notImplemented('getContract', 'Bitcoin', NO_CONTRACTS);
  }

  async callContractMethod<T = ContractResult>(): Promise<T> {
    throw notImplemented('callContractMethod', 'Bitcoin', NO_CONTRACTS);
  }

  async mint(): Promise<ITransaction> {
    throw notImplemented('mint', 'Bitcoin', NO_CONTRACTS);
  }

  async deploy(): Promise<ITransaction> {
    throw notImplemented('deploy', 'Bitcoin', NO_CONTRACTS);
  }

  async createToken(): Promise<ITokenCreation> {
    throw notImplemented('createToken', 'Bitcoin', 'Bitcoin has no native tokens.');
  }

  async getTokenInfo(): Promise<ITokenInfo> {
    throw notImplemented('getTokenInfo', 'Bitcoin', 'Bitcoin has no native tokens.');
  }

  async getTransactionReceipt(transactionHash: string, onChainEvent?: ITransaction): Promise<ITransaction> {
    return this.#esplora.use(session => Transactions.receipt(session, transactionHash, { ...onChainEvent }, this.provider));
  }

  /** A transaction by ID; the ABI does not apply to Bitcoin. */
  async getTransaction(transactionHash: string): Promise<ITransactionDetails> {
    return this.#esplora.use(session => Transactions.details(session, transactionHash, this.provider));
  }
}
