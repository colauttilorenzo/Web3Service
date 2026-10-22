using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using RpcReceipt = Nethereum.RPC.Eth.DTOs.TransactionReceipt;

namespace Certifie.Web3.Evm
{
    /// <summary>Builds the JSON-safe <see cref="TransactionResult"/> of a mined transaction.</summary>
    internal static class TransactionResults
    {
        /// <summary>Fills <paramref name="result"/> with the receipt, the outcome at block time and the exact fee.</summary>
        public static async Task<TransactionResult> FromReceiptAsync(RpcSession session, RpcReceipt receipt, TransactionResult result, EthereumProvider provider)
        {
            var tx = NormalizeReceipt(receipt);
            var timestamp = await BlockTimestampAsync(session, receipt.BlockNumber).ConfigureAwait(false);
            result.RequestedAt = SharedResults.RequestedAt(result.RequestedAt);
            result.Tx = tx;
            ApplyOutcome(result, tx, timestamp);
            result.NativeFee = await NativeFeeAsync(session, receipt, provider).ConfigureAwait(false);
            return result;
        }

        /// <summary>Adds the receipt, the block time and the exact fee of a mined transaction.</summary>
        public static async Task<TransactionDetails> AddMinedDetailsAsync(RpcSession session, TransactionDetails details, EthereumProvider provider)
        {
            var receipt = details.Pending ? null : await Receipts.GetAsync(session, details.Hash).ConfigureAwait(false);
            if (receipt == null) return details;
            details.Receipt = NormalizeReceipt(receipt);
            details.Timestamp = await BlockTimestampAsync(session, receipt.BlockNumber).ConfigureAwait(false);
            details.NativeFee = await NativeFeeAsync(session, receipt, provider).ConfigureAwait(false);
            return details;
        }

        /// <summary>Every field is set, so stale values from the caller's event are overwritten.</summary>
        private static void ApplyOutcome(TransactionResult result, TransactionReceipt tx, string timestamp)
        {
            var reverted = tx.Status == "0";
            result.CompletedAt = reverted ? null : timestamp;
            result.RejectedAt = reverted ? timestamp : null;
            result.Error = reverted ? Errors.TransactionReverted(tx.TransactionHash).ToInfo() : null;
        }

        /// <summary>Block time in milliseconds, as a decimal string.</summary>
        private static async Task<string> BlockTimestampAsync(RpcSession session, HexBigInteger blockNumber)
        {
            var block = await session.WithTimeoutAsync(session.Eth.Blocks.GetBlockWithTransactionsHashesByNumber.SendRequestAsync(blockNumber)).ConfigureAwait(false);
            return SharedResults.Milliseconds(block.Timestamp.Value);
        }

        /// <summary>Exact gas cost in the smallest unit. Nodes without <c>effectiveGasPrice</c> fall back to the transaction's gas price.</summary>
        private static async Task<NativeFee?> NativeFeeAsync(RpcSession session, RpcReceipt receipt, EthereumProvider provider)
        {
            var gasPrice = receipt.EffectiveGasPrice?.Value ?? await TransactionGasPriceAsync(session, receipt.TransactionHash).ConfigureAwait(false);
            return gasPrice == null ? null : SharedResults.NativeFee(receipt.GasUsed.Value * gasPrice.Value, provider);
        }

        private static async Task<BigInteger?> TransactionGasPriceAsync(RpcSession session, string transactionHash)
        {
            var transaction = await session.WithTimeoutAsync(session.Eth.Transactions.GetTransactionByHash.SendRequestAsync(transactionHash)).ConfigureAwait(false);
            return transaction?.GasPrice?.Value;
        }

        private static TransactionReceipt NormalizeReceipt(RpcReceipt receipt) => new TransactionReceipt
        {
            TransactionHash = receipt.TransactionHash, BlockHash = receipt.BlockHash, From = receipt.From, To = receipt.To,
            BlockNumber = Integers.ToDecimalString(receipt.BlockNumber.Value), TransactionIndex = Integers.ToDecimalString(receipt.TransactionIndex.Value),
            ContractAddress = receipt.ContractAddress, Type = Integers.ToOptionalDecimalString(receipt.Type),
            Status = Integers.ToDecimalString(receipt.Status?.Value ?? BigInteger.One),
            CumulativeGasUsed = Integers.ToDecimalString(receipt.CumulativeGasUsed.Value), GasUsed = Integers.ToDecimalString(receipt.GasUsed.Value),
            EffectiveGasPrice = Integers.ToOptionalDecimalString(receipt.EffectiveGasPrice), LogsBloom = receipt.LogsBloom,
            Logs = (receipt.Logs ?? Array.Empty<FilterLog>()).Select(NormalizeLog).ToList(),
        };

        private static TransactionLog NormalizeLog(FilterLog log) => new TransactionLog
        {
            Address = log.Address, Data = log.Data, Topics = log.Topics?.Select(topic => topic?.ToString() ?? "").ToList(),
            LogIndex = Integers.ToOptionalDecimalString(log.LogIndex), TransactionIndex = Integers.ToOptionalDecimalString(log.TransactionIndex),
            TransactionHash = log.TransactionHash, BlockHash = log.BlockHash,
            BlockNumber = Integers.ToOptionalDecimalString(log.BlockNumber), Removed = log.Removed,
        };
    }
}
