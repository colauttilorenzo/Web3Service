import { Web3Error } from '../Web3Exception.js';

/** Factories for errors raised in more than one place. */
export function invalidArgument(message: string): Web3Error {
  return new Web3Error('INVALID_ARGUMENT', message);
}

export function signerRequired(): Web3Error {
  return new Web3Error('SIGNER_REQUIRED', 'Provide a private key or mnemonic to sign transactions.');
}

/** The node did not confirm the transaction. It may still be mined, so callers must check the hash before retrying. */
export function submissionFailed(transactionHash: string, cause?: unknown): Web3Error {
  const message = 'Submission failed or mining was not confirmed; check the transaction hash before retrying.';
  return new Web3Error('TRANSACTION_SUBMISSION_FAILED', message, { cause, transactionHash });
}

export function transactionReverted(transactionHash: string): Web3Error {
  return new Web3Error('TRANSACTION_REVERTED', 'The transaction was mined and reverted.', { transactionHash });
}

export function transactionNotFound(transactionHash: string): Web3Error {
  return new Web3Error('TRANSACTION_NOT_FOUND', 'The transaction is unknown or has not been mined.', { transactionHash });
}

export function receiptDetailsFailed(transactionHash: string, cause: unknown): Web3Error {
  const message = 'The transaction was mined, but receipt enrichment failed; retrieve it using the transaction hash.';
  return new Web3Error('RECEIPT_DETAILS_FAILED', message, { cause, transactionHash });
}
