// What deploy needs on Algorand: the approval and clear programs, the state schema and the create method of the application.
import { base64ToBytes, hexToBytes } from 'algosdk';
import type { Abi, Bytecode, IArc4Contract } from '../Models.js';
import { invalidArgument } from '../Internal/Errors.js';
import type { AlgodSession } from './AlgodSession.js';
import type { AppCall, Creation } from './AppCalls.js';
import * as Arc4Methods from './Arc4Methods.js';
import type { Arc4Function } from './Arc4Methods.js';

/** Programs as base64 or 0x hex, as an ARC-56 description stores them. */
interface ProgramPair {
  readonly approval: string;
  readonly clear: string;
}

/** ARC-56 state schema, and the ARC-32 one under global and local. */
interface StateDescription {
  schema?: { global?: { ints?: number; bytes?: number }; local?: { ints?: number; bytes?: number } };
  global?: { num_uints?: number; num_byte_slices?: number };
  local?: { num_uints?: number; num_byte_slices?: number };
}

const PAGE_BYTES = 2048;
const MAX_EXTRA_PAGES = 3;
const HEX = /^0x(?:[0-9a-f]{2})+$/i;
const TEAL_SOURCE = /^\s*#pragma/;
const CREATE_NAMES = ['create', 'createApplication'];

/** The creation call: the create method with the constructor arguments, or a bare creation when the contract has none. */
export async function creationCall(session: AlgodSession, abi: Abi, bytecode: Bytecode, constructorArguments: readonly unknown[]): Promise<AppCall> {
  const programs = await programsOf(session, abi as IArc4Contract, bytecode);
  const fn = createMethod(Arc4Methods.describe(abi));
  if (!fn && constructorArguments.length > 0) throw invalidArgument('The contract has no create method, so it takes no constructor arguments.');
  const args = fn ? Arc4Methods.toArguments(fn, Arc4Methods.toParameters(fn, constructorArguments)) : [];
  return { appId: 0n, fn, args, creation: { ...programs, ...schemaOf(abi as IArc4Contract), extraPages: extraPages(programs) } };
}

/** The programs passed to deploy, else those of the ARC-56 description: compiled (byteCode), or TEAL (source) for the node to compile. */
async function programsOf(session: AlgodSession, contract: IArc4Contract, bytecode: Bytecode): Promise<Pick<Creation, 'approvalProgram' | 'clearProgram'>> {
  const pair = typeof bytecode === 'object' && bytecode !== null ? bytecode : describedPrograms(contract, bytecode);
  const [approvalProgram, clearProgram] = await Promise.all([program(session, pair.approval), program(session, pair.clear)]);
  return { approvalProgram, clearProgram };
}

function describedPrograms(contract: IArc4Contract, bytecode: Bytecode): ProgramPair {
  if (bytecode) throw invalidArgument('On Algorand bytecode is { approval, clear }, or empty to take the programs from the ARC-56 description.');
  const compiled = contract.byteCode as ProgramPair | undefined;
  if (compiled?.approval && compiled.clear) return compiled;
  const source = contract.source as ProgramPair | undefined;
  if (!source?.approval || !source.clear) throw invalidArgument('The contract description has no byteCode or source; pass the programs as bytecode.');
  return { approval: textOf(source.approval), clear: textOf(source.clear) };
}

/** A program as 0x hex, as base64, or as TEAL source compiled by the node (which needs EnableDeveloperAPI). */
async function program(session: AlgodSession, value: string): Promise<Uint8Array> {
  if (HEX.test(value)) return hexToBytes(value.slice(2));
  if (!TEAL_SOURCE.test(value)) return base64Program(value);
  const compiled = await session.post<{ result: string }>('v2/teal/compile', new TextEncoder().encode(value), 'text/plain');
  return base64ToBytes(compiled.result);
}

function base64Program(value: string): Uint8Array {
  try {
    return base64ToBytes(value);
  } catch {
    throw invalidArgument('A program must be 0x hex, base64 or TEAL source.');
  }
}

/** The create method: an ARC-56 method whose create actions include NoOp, else one named create or createApplication. */
function createMethod(functions: readonly Arc4Function[]): Arc4Function | undefined {
  const described = functions.find(fn => fn.spec.actions?.create?.includes('NoOp'));
  return described ?? functions.find(fn => !fn.spec.actions && CREATE_NAMES.includes(fn.method.name));
}

function schemaOf(contract: IArc4Contract): Omit<Creation, 'approvalProgram' | 'clearProgram' | 'extraPages'> {
  const state = (contract.state ?? {}) as StateDescription;
  return {
    numGlobalInts: state.schema?.global?.ints ?? state.global?.num_uints ?? 0,
    numGlobalByteSlices: state.schema?.global?.bytes ?? state.global?.num_byte_slices ?? 0,
    numLocalInts: state.schema?.local?.ints ?? state.local?.num_uints ?? 0,
    numLocalByteSlices: state.schema?.local?.bytes ?? state.local?.num_byte_slices ?? 0,
  };
}

/** Both programs together may take 2048 bytes, plus 2048 for each extra page. */
function extraPages({ approvalProgram, clearProgram }: Pick<Creation, 'approvalProgram' | 'clearProgram'>): number {
  return Math.min(MAX_EXTRA_PAGES, Math.max(0, Math.ceil((approvalProgram.length + clearProgram.length) / PAGE_BYTES) - 1));
}

function textOf(base64: string): string {
  return new TextDecoder().decode(base64ToBytes(base64));
}
