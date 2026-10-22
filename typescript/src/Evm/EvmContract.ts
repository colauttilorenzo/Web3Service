import type { ContractAbi } from 'web3';
import type {
  ChainId, ContractParameters, ContractResult, IContractMethod, INftMint, ITokenInfo, ITransaction, ITransactionDetails, TransactionOptions,
} from '../Models.js';
import { Web3Contract } from '../Web3Contract.js';
import * as AbiMethods from './AbiMethods.js';
import type { AbiFunction } from './AbiMethods.js';
import * as ContractCalls from './ContractCalls.js';
import * as ContractMethods from '../Internal/ContractMethods.js';
import { invalidArgument, notSupported } from '../Internal/Errors.js';
import * as Integers from '../Internal/Integers.js';
import * as ErcStandards from './ErcStandards.js';
import type { ErcTokenStandard } from './ErcStandards.js';
import * as TokenMethods from '../Internal/TokenMethods.js';
import * as Tokens from './Tokens.js';

const ADDRESS = /^0x[0-9a-f]{40}$/i;

/** What an EvmContract needs from the EvmChain that created it. */
export interface IContractRunner {
  read(abi: ContractAbi, address: string, signature: string, args: unknown[]): Promise<ContractResult>;
  send(abi: ContractAbi, address: string, signature: string, args: unknown[], name: string, options?: TransactionOptions): Promise<ITransaction>;
  getTransaction(transactionHash: string, abi: ContractAbi): Promise<ITransactionDetails>;
}

/** A contract on an EVM network, described by its JSON ABI. */
export class EvmContract extends Web3Contract {
  declare readonly abi: ContractAbi;
  readonly methods: readonly IContractMethod[];
  /** ERC standards recognized from the ABI, e.g. ['ERC-721', 'ERC-721 Metadata', 'ERC-165']. */
  readonly standards: readonly string[];
  readonly #functions: readonly AbiFunction[];
  readonly #runner: IContractRunner;

  /** @internal Use Web3Service.getContract. */
  constructor(abi: ContractAbi, address: string, runner: IContractRunner) {
    if (!ADDRESS.test(address)) throw invalidArgument('The contract address must be 0x followed by 40 hex digits.');
    super(abi, address);
    this.#runner = runner;
    this.#functions = AbiMethods.describe(abi);
    this.methods = this.#functions.map(fn => fn.method);
    this.standards = ErcStandards.detect(this.methods.map(method => method.signature));
  }

  /** The owner() of the contract (Ownable, ERC-173); undefined when the ABI has no owner() function. */
  async owner(): Promise<string | undefined> {
    return this.#has('owner()') ? String(await this.read('owner()')) : undefined;
  }

  /**
   * Calls a function by name (or signature) with named parameters, e.g. call('transfer', { to, amount: '1000' }).
   * Read functions return their value; write and payable functions send a transaction and return it once mined.
   */
  async call<T = ContractResult | ITransaction>(method: string, parameters: ContractParameters = {}, options?: TransactionOptions): Promise<T> {
    const fn = ContractMethods.find(this.#functions, method, parameters);
    return (fn.method.kind === 'read' ? this.#read(fn, parameters) : this.#send(fn, parameters, options)) as Promise<T>;
  }

  /** Returns the outputs without a transaction; on a write function it is a free simulation, sent from the zero address. Integers become decimal strings. */
  async read<T = ContractResult>(method: string, parameters: ContractParameters = {}): Promise<T> {
    return this.#read(ContractMethods.find(this.#functions, method, parameters), parameters) as Promise<T>;
  }

  /** Sends a transaction, signed by the service's signer, and waits until it is mined. Coins for payable functions go in options.value. */
  async send(method: string, parameters: ContractParameters = {}, options?: TransactionOptions): Promise<ITransaction> {
    return this.#send(ContractMethods.find(this.#functions, method, parameters), parameters, options);
  }

  /** The call data of a function call, e.g. to have it signed elsewhere. */
  encode(method: string, parameters: ContractParameters = {}): string {
    const fn = ContractMethods.find(this.#functions, method, parameters);
    return ContractCalls.encode(fn, AbiMethods.toArguments(fn, parameters));
  }

  /**
   * Mints an NFT on an ERC-721 or ERC-1155 contract with its safeMint, mint or mintTo function, chosen by the parameter names,
   * e.g. mintNft({ to, uri: 'ipfs://…' }). Returns the IDs of the new tokens.
   */
  async mintNft(parameters: ContractParameters, options?: TransactionOptions): Promise<INftMint> {
    const standard = this.#tokenStandard();
    if (standard === 'ERC-20') throw notSupported('The contract is an ERC-20 token, not an NFT.');
    const transaction = await this.#send(TokenMethods.mintFunction(this.#functions, parameters), parameters, options);
    return { standard, tokenIds: Tokens.mintedTokenIds(transaction.tx?.logs ?? [], this.address), transaction };
  }

  /** Name, symbol, decimals and supply of a token; with the ID of an NFT, also its owner and URI. */
  async getTokenInfo(tokenId?: ChainId): Promise<ITokenInfo> {
    const standard = this.#tokenStandard();
    const id = tokenId === undefined || standard === 'ERC-20' ? undefined : Integers.toDecimalString(tokenId, 'tokenId');
    const info: ITokenInfo = { standard, address: this.address, ...(id === undefined ? {} : { tokenId: id }) };
    for (const [field, signature] of Tokens.tokenReads(this.methods.map(method => method.signature), id !== undefined)) {
      TokenMethods.assign(info, field, String(await this.#runner.read(this.abi, this.address, signature, signature.endsWith('()') ? [] : [id])));
    }
    return Tokens.expandTokenUri(info);
  }

  /** A transaction by hash, with the function it called on this contract and its arguments. */
  async getTransaction(transactionHash: string): Promise<ITransactionDetails> {
    return this.#runner.getTransaction(transactionHash, this.abi);
  }

  #read(fn: AbiFunction, parameters: ContractParameters): Promise<ContractResult> {
    return this.#runner.read(this.abi, this.address, fn.method.signature, AbiMethods.toArguments(fn, parameters));
  }

  #send(fn: AbiFunction, parameters: ContractParameters, options?: TransactionOptions): Promise<ITransaction> {
    return this.#runner.send(this.abi, this.address, fn.method.signature, AbiMethods.toArguments(fn, parameters), fn.method.name, options);
  }

  #has(signature: string): boolean {
    return this.methods.some(method => method.signature === signature);
  }

  #tokenStandard(): ErcTokenStandard {
    const standard = ErcStandards.tokenStandard(this.standards);
    if (!standard) throw notSupported('The contract is not an ERC-20, ERC-721 or ERC-1155 token.');
    return standard;
  }
}
