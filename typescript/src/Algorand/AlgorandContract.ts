import type {
  Abi, ChainId, ContractParameters, ContractResult, IContractMethod, INftMint, ITokenInfo, ITransaction, ITransactionDetails, TransactionOptions,
} from '../Models.js';
import { Web3Contract } from '../Web3Contract.js';
import * as ContractMethods from '../Internal/ContractMethods.js';
import { invalidArgument, notSupported } from '../Internal/Errors.js';
import * as Integers from '../Internal/Integers.js';
import * as TokenMethods from '../Internal/TokenMethods.js';
import * as AppCalls from './AppCalls.js';
import * as Arc4Methods from './Arc4Methods.js';
import type { Arc4Function, CallArgument } from './Arc4Methods.js';
import * as Arc4Values from './Arc4Values.js';
import * as ArcStandards from './ArcStandards.js';
import type { ArcTokenStandard } from './ArcStandards.js';
import * as Tokens from './Tokens.js';
import type { AvmTransaction } from './Transactions.js';

/** A confirmed write: its JSON result and the transaction it came from. */
export interface Sent {
  readonly result: ITransaction;
  readonly transaction: AvmTransaction;
}

/** What an AlgorandContract needs from the AlgorandChain that created it. */
export interface IAppRunner {
  read(appId: bigint, fn: Arc4Function, args: readonly CallArgument[]): Promise<ContractResult>;
  send(appId: bigint, fn: Arc4Function, args: readonly CallArgument[], name: string, options?: TransactionOptions): Promise<Sent>;
  getTransaction(transactionHash: string, abi: Abi): Promise<ITransactionDetails>;
}

const BYTE_ARRAY = /^byte\[\d*\]$/;
const NFT_MINT_METHODS = ['arc72_mint'];

/** An Algorand application, described by its ARC-4 (or ARC-32, ARC-56) contract; its address is the application ID. */
export class AlgorandContract extends Web3Contract {
  readonly methods: readonly IContractMethod[];
  /** ARC standards recognized from the method names, e.g. ['ARC-200'] or ['ARC-72', 'ARC-73']. */
  readonly standards: readonly string[];
  readonly #functions: readonly Arc4Function[];
  readonly #appId: bigint;
  readonly #runner: IAppRunner;

  /** @internal Use Web3Service.getContract. */
  constructor(abi: Abi, address: string, runner: IAppRunner) {
    super(abi, address);
    this.#appId = applicationId(address);
    this.#runner = runner;
    this.#functions = Arc4Methods.describe(abi);
    this.methods = this.#functions.map(fn => fn.method);
    this.standards = ArcStandards.detect(this.methods.map(method => method.name));
  }

  /** The value of the contract's owner() method; undefined when it has none. */
  async owner(): Promise<string | undefined> {
    const owner = this.#functions.find(fn => fn.method.name === 'owner' && Object.keys(fn.method.parameters).length === 0);
    return owner ? String(await this.#read(owner, {})) : undefined;
  }

  async call<T = ContractResult | ITransaction>(method: string, parameters: ContractParameters = {}, options?: TransactionOptions): Promise<T> {
    const fn = ContractMethods.find(this.#functions, method, parameters);
    return (fn.method.kind === 'read' ? this.#read(fn, parameters) : this.#send(fn, parameters, options).then(sent => sent.result)) as Promise<T>;
  }

  /** Simulates the call, so it is free even on a write method. */
  async read<T = ContractResult>(method: string, parameters: ContractParameters = {}): Promise<T> {
    return this.#read(ContractMethods.find(this.#functions, method, parameters), parameters) as Promise<T>;
  }

  async send(method: string, parameters: ContractParameters = {}, options?: TransactionOptions): Promise<ITransaction> {
    return (await this.#send(ContractMethods.find(this.#functions, method, parameters), parameters, options)).result;
  }

  /** The application arguments, concatenated: the method selector followed by the encoded arguments. */
  encode(method: string, parameters: ContractParameters = {}): string {
    const fn = ContractMethods.find(this.#functions, method, parameters);
    return Arc4Methods.toHex(AppCalls.applicationArgs({ appId: this.#appId, fn, args: Arc4Methods.toArguments(fn, parameters) }));
  }

  /** Mints with the safeMint, mint, mintTo or arc72_mint method; the IDs come from arc72_Transfer events or from the assets the call created. */
  async mintNft(parameters: ContractParameters, options?: TransactionOptions): Promise<INftMint> {
    const standard = ArcStandards.tokenStandard(this.standards);
    if (standard === 'ARC-200') throw notSupported('The contract is an ARC-200 token, not an NFT.');
    const { result, transaction } = await this.#send(TokenMethods.mintFunction(this.#functions, parameters, NFT_MINT_METHODS), parameters, options);
    const tokenIds = [...Tokens.mintedTokenIds(transaction.logs), ...transaction.innerCreatedAssets.map(String)];
    return { standard: standard ?? 'ASA', tokenIds, transaction: result };
  }

  /** Name, symbol, decimals and supply of an ARC-200 token or ARC-72 collection; with the ID of an NFT, also its owner and URI. */
  async getTokenInfo(tokenId?: ChainId): Promise<ITokenInfo> {
    const standard = this.#tokenStandard();
    const id = tokenId === undefined || standard === 'ARC-200' ? undefined : Integers.toDecimalString(tokenId, 'tokenId');
    const info: ITokenInfo = { standard, address: this.address, ...(id === undefined ? {} : { tokenId: id }) };
    for (const [field, name] of Tokens.tokenReads(this.methods.map(method => method.name), id !== undefined)) {
      TokenMethods.assign(info, field, await this.#readToken(name, id));
    }
    return info;
  }

  async getTransaction(transactionHash: string): Promise<ITransactionDetails> {
    return this.#runner.getTransaction(transactionHash, this.abi);
  }

  #read(fn: Arc4Function, parameters: ContractParameters): Promise<ContractResult> {
    return this.#runner.read(this.#appId, fn, Arc4Methods.toArguments(fn, parameters));
  }

  #send(fn: Arc4Function, parameters: ContractParameters, options?: TransactionOptions): Promise<Sent> {
    return this.#runner.send(this.#appId, fn, Arc4Methods.toArguments(fn, parameters), fn.method.name, options);
  }

  /** A token field; fixed-size byte values such as arc200_name are text padded with zero bytes. */
  async #readToken(name: string, tokenId: string | undefined): Promise<string> {
    const fn = this.#functions.find(candidate => candidate.method.name === name)!;
    const argument = Object.keys(fn.method.parameters)[0];
    const value = String(await this.#read(fn, argument === undefined ? {} : { [argument]: tokenId }));
    return BYTE_ARRAY.test(fn.method.outputs.result0 ?? '') ? Arc4Values.bytesToText(value) : value;
  }

  #tokenStandard(): ArcTokenStandard {
    const standard = ArcStandards.tokenStandard(this.standards);
    if (!standard) throw notSupported('The contract is not an ARC-200 or ARC-72 token.');
    return standard;
  }
}

/** The application ID, which Algorand uses as the contract address. */
export function applicationId(address: string): bigint {
  const id = typeof address === 'string' && /^[0-9]+$/.test(address) ? BigInt(address) : 0n;
  if (id === 0n) throw invalidArgument('The contract address of an Algorand application is its application ID, a positive integer.');
  return id;
}
