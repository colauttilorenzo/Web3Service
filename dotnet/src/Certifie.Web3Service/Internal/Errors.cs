using System;
using System.Threading;

namespace Certifie.Web3
{
    /// <summary>Factories for errors raised in more than one place.</summary>
    internal static class Errors
    {
        public static Web3Exception InvalidArgument(string message) => new Web3Exception("INVALID_ARGUMENT", message);

        /// <summary>The contract does not offer the requested feature, e.g. minting on a contract that is not an NFT.</summary>
        public static Web3Exception NotSupported(string message) => new Web3Exception("NOT_SUPPORTED", message);

        /// <summary>The blockchain does not offer the operation at all, e.g. deploy on Bitcoin.</summary>
        public static NotImplementedException NotImplemented(string operation, string chainName, string hint = "") =>
            new NotImplementedException($"{operation} is not available on {chainName}.{(hint.Length > 0 ? " " + hint : "")}");

        public static Web3Exception MethodNotFound(string method) =>
            new Web3Exception("METHOD_NOT_FOUND", $"Contract method {method} is missing from the ABI.");

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

        public static Web3Exception TokenNotFound(string tokenId) => new Web3Exception("TOKEN_NOT_FOUND", $"Token {tokenId} does not exist.");

        /// <summary>The endpoint answers for another network than the configured one.</summary>
        public static Web3Exception ChainMismatch(string expected, string actual) =>
            new Web3Exception("CHAIN_ID_MISMATCH", $"Expected chain {expected}, received {actual}.");

        public static Web3Exception ReceiptDetailsFailed(string transactionHash, Exception cause) =>
            new Web3Exception("RECEIPT_DETAILS_FAILED",
                "The transaction was mined, but receipt enrichment failed; retrieve it using the transaction hash.", cause, transactionHash);

        /// <summary>True when the caller cancelled the operation; such errors are rethrown unchanged.</summary>
        public static bool IsCancellation(Exception error, CancellationToken cancellationToken) =>
            error is OperationCanceledException && cancellationToken.IsCancellationRequested;
    }
}
