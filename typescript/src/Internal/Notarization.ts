// The notarization call shared by every blockchain: mint(address,string,string,string) with the contract's own address first.
import type { ITransaction, NotarizationDocument } from '../Models.js';
import { invalidArgument } from './Errors.js';

/** The arguments of mint: the contract's address, the hash, its algorithm and the metadata. */
export function mintArguments(contractAddress: string, document: NotarizationDocument | undefined): string[] {
  if (!document?.hash?.data || !document.hash.algorithm) throw invalidArgument('A document hash and algorithm are required.');
  return [contractAddress, document.hash.data, document.hash.algorithm, document.metadata ?? ''];
}

/** A copy of the caller's event, named after the operation unless the caller named it. */
export function named(onChainEvent: ITransaction | undefined, name: string): ITransaction {
  return { ...onChainEvent, name: onChainEvent?.name ?? name };
}
