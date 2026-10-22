using System;
using System.Threading.Tasks;
using RpcReceipt = Nethereum.RPC.Eth.DTOs.TransactionReceipt;

namespace Certifie.Web3.Evm
{
    /// <summary>Receipt lookups through the configured RPC node, so receipts are read the same way whoever signed.</summary>
    internal static class Receipts
    {
        /// <summary>The receipt, or null while the transaction is unknown or pending.</summary>
        public static async Task<RpcReceipt?> GetAsync(RpcSession session, string transactionHash) =>
            await session.WithTimeoutAsync(session.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(transactionHash)).ConfigureAwait(false);

        /// <summary>Like <see cref="GetAsync"/>, but an RPC failure also counts as "not mined yet".</summary>
        public static async Task<RpcReceipt?> TryGetAsync(RpcSession session, string transactionHash)
        {
            try { return await GetAsync(session, transactionHash).ConfigureAwait(false); }
            catch (Exception error) when (!session.IsCancellation(error)) { return null; }
        }

        /// <summary>Polls until the transaction is mined or the timeout expires.</summary>
        public static async Task<RpcReceipt> WaitAsync(RpcSession session, string transactionHash, TimeSpan timeout, TimeSpan pollingInterval)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                var receipt = await TryGetAsync(session, transactionHash).ConfigureAwait(false);
                if (receipt != null) return receipt;
                if (DateTime.UtcNow >= deadline) throw Errors.SubmissionFailed(transactionHash);
                await Task.Delay(pollingInterval, session.CancellationToken).ConfigureAwait(false);
            }
        }
    }
}
