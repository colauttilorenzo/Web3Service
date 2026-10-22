// Readable reasons for rejected application calls: ARC-56 maps the program counters of failing opcodes to error messages.
import type { IArc4Contract } from '../Models.js';

/** Program counter → error message, e.g. 201 → 'already notarized'. */
export type ErrorMessages = ReadonlyMap<number, string>;

/** ARC-56 sourceInfo of the approval program; offsets other than 'none' are not mapped. */
interface SourceInfo {
  readonly sourceInfo?: readonly { readonly pc: readonly number[]; readonly errorMessage?: string }[];
  readonly pcOffsetMethod?: string;
}

const PROGRAM_COUNTER = /pc=(\d+)/;
const INNER_FAILURE = 'inner tx';

/** The error messages of the approval program; empty for ARC-4 and ARC-32 descriptions, which have none. */
export function of(contract: IArc4Contract): ErrorMessages {
  const approval = (contract.sourceInfo as { approval?: SourceInfo } | undefined)?.approval;
  if (approval?.pcOffsetMethod !== 'none') return new Map();
  const entries = (approval.sourceInfo ?? []).filter(entry => entry.errorMessage);
  return new Map(entries.flatMap(entry => entry.pc.map(pc => [pc, entry.errorMessage!] as const)));
}

/** The failure with the contract's message first, e.g. 'already notarized (logic eval error: assert failed pc=201…)'. */
export function explain(messages: ErrorMessages, failure: string): string {
  const pc = failure.includes(INNER_FAILURE) ? undefined : PROGRAM_COUNTER.exec(failure)?.[1];
  const message = pc === undefined ? undefined : messages.get(Number(pc));
  return message ? `${message} (${failure})` : failure;
}
