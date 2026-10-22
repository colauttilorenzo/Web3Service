using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Certifie.Web3.Algorand
{
    /// <summary>Signs and sends a group, then waits until its last transaction is confirmed.</summary>
    internal static class Submission
    {
        /// <summary>Signs and broadcasts the group, returning the ID of its last transaction once the node has accepted it.</summary>
        public static async Task<string> SendAsync(AlgodSession session, List<UnsignedTransaction> group, AlgorandSigner signer)
        {
            var (signed, transactionId) = (signer.Sign(group).SelectMany(bytes => bytes).ToArray(), group.Last().Id);
            try { await session.PostAsync("v2/transactions", signed, "application/x-binary").ConfigureAwait(false); }
            catch (Exception error) when (!session.IsCancellation(error))
            {
                if (await Transactions.TryPendingAsync(session, transactionId).ConfigureAwait(false) == null) throw Errors.SubmissionFailed(transactionId, error);
            }
            return transactionId;
        }

        /// <summary>Polls until the transaction is confirmed, the node drops it, or the timeout expires.</summary>
        public static async Task<JsonObject> WaitAsync(AlgodSession session, string transactionId, TimeSpan timeout, TimeSpan pollingInterval)
        {
            var deadline = DateTime.UtcNow + timeout;
            for (;;)
            {
                var pending = await Transactions.TryPendingAsync(session, transactionId).ConfigureAwait(false);
                if (JsonValues.Integer(pending?["confirmed-round"]) > 0) return pending!;
                if (JsonValues.Text(pending?["pool-error"]) is { Length: > 0 } poolError)
                    throw new Web3Exception("TRANSACTION_REVERTED", $"The node rejected the transaction: {poolError}", transactionHash: transactionId);
                if (DateTime.UtcNow >= deadline) throw Errors.SubmissionFailed(transactionId);
                await Task.Delay(pollingInterval, session.CancellationToken).ConfigureAwait(false);
            }
        }
    }
}
