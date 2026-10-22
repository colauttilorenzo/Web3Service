// Chooses the blockchain implementation from the type of the provider.
import { AlgorandChain } from '../Algorand/AlgorandChain.js';
import { BitcoinChain } from '../Bitcoin/BitcoinChain.js';
import { EvmChain } from '../Evm/EvmChain.js';
import type { IWeb3Service } from '../IWeb3Service.js';
import type { ProviderInput, Web3ServiceOptions } from '../Models.js';
import { invalidArgument } from './Errors.js';

/** IAvmProvider → AlgorandChain, IBitcoinProvider → BitcoinChain, IEvmProvider or a node URL → EvmChain. */
export function create(input: ProviderInput, options?: Web3ServiceOptions): IWeb3Service {
  const kind = typeof input === 'object' && input !== null ? input.kind ?? 'evm' : 'evm';
  switch (kind) {
    case 'evm': return new EvmChain(input as Exclude<ProviderInput, { kind: 'avm' | 'bitcoin' }>, options);
    case 'avm': return new AlgorandChain(input as Extract<ProviderInput, { kind: 'avm' }>, options);
    case 'bitcoin': return new BitcoinChain(input as Extract<ProviderInput, { kind: 'bitcoin' }>, options);
    default: throw invalidArgument(`Unknown provider kind ${String(kind)}; use evm, avm or bitcoin.`);
  }
}
