using System;
using System.Threading;

namespace Certifie.Web3
{
    /// <summary>Factories for errors raised in more than one place.</summary>
    internal static class Errors
    {
        public static Web3Exception InvalidArgument(string message) => new Web3Exception("INVALID_ARGUMENT", message);

        public static Web3Exception SignerRequired() =>
            new Web3Exception("SIGNER_REQUIRED", "Provide a private key or mnemonic to sign transactions.");

        /// <summary>The node did not confirm the transaction. It may still be mined, so callers must check the hash before retrying.</summary>
        public static Web3Exception SubmissionFailed(string transactionHash, Exception? cause = null) =>
            new Web3Exception("TRANSACTION_SUBMISSION_FAILED",
                "Submission failed or mining was not confirmed; check the transaction hash before retrying.", cause, transactionHash);

        public static Web3Exception TransactionReverted(string transactionHash) =>
            new Web3Exception("TRANSACTION_REVERTED", "The transaction was mined and reverted.", transactionHash: transactionHash);

        public static Web3Exception TransactionNotFound(string transactionHash) =>
            new Web3Exception("TRANSACTION_NOT_FOUND", "The transaction is unknown or has not been mined.", transactionHash: transactionHash);

        public static Web3Exception ReceiptDetailsFailed(string transactionHash, Exception cause) =>
            new Web3Exception("RECEIPT_DETAILS_FAILED",
                "The transaction was mined, but receipt enrichment failed; retrieve it using the transaction hash.", cause, transactionHash);

        /// <summary>True when the caller cancelled the operation; such errors are rethrown unchanged.</summary>
        public static bool IsCancellation(Exception error, CancellationToken cancellationToken) =>
            error is OperationCanceledException && cancellationToken.IsCancellationRequested;
    }
}
