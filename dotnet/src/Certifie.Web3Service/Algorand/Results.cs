using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Nethereum.Hex.HexConvertors.Extensions;

namespace Certifie.Web3.Algorand
{
    /// <summary>Builds the JSON-safe result, receipt and details of an Algorand transaction, in the same shape as on EVM networks.</summary>
    internal static class Results
    {
        /// <summary>Fills result with the receipt, the confirmation time and the exact fee. Algorand never commits failed transactions.</summary>
        public static async Task<TransactionResult> FromConfirmedAsync(AlgodSession session, AvmTransaction transaction, TransactionResult result, ResolvedProvider network)
        {
            var position = await PositionOfAsync(session, transaction).ConfigureAwait(false);
            result.RequestedAt = SharedResults.RequestedAt(result.RequestedAt);
            result.Tx = Receipt(transaction, position);
            (result.CompletedAt, result.RejectedAt, result.Error) = (position.Timestamp, null, null);
            result.NativeFee = SharedResults.NativeFee(transaction.Fee, network);
            return result;
        }

        /// <summary>The transaction with its receipt, block time and fee once confirmed, and the called method when the contract describes it.</summary>
        public static async Task<TransactionDetails> DetailsAsync(AlgodSession session, AvmTransaction transaction, IReadOnlyList<Arc4Function> functions,
            ResolvedProvider network)
        {
            var position = transaction.ConfirmedRound == null ? null : await PositionOfAsync(session, transaction).ConfigureAwait(false);
            return new TransactionDetails
            {
                Hash = transaction.Id, From = transaction.Sender, To = transaction.Receiver, Value = Integers.ToDecimalString(transaction.Amount), Nonce = "0", Gas = "0",
                Input = Arc4Methods.ToHex(transaction.AppArgs), Type = transaction.Type, BlockHash = position?.Hash, BlockNumber = position?.Round,
                TransactionIndex = position?.Index, Pending = position == null, Timestamp = position?.Timestamp,
                Receipt = position == null ? null : Receipt(transaction, position),
                NativeFee = position == null ? null : SharedResults.NativeFee(transaction.Fee, network), Method = Arc4Methods.DecodeCall(functions, transaction.AppArgs),
            };
        }

        /// <summary>Gas fields do not apply to Algorand, so they are zero; a created application's ID is its contract address.</summary>
        private static TransactionReceipt Receipt(AvmTransaction transaction, BlockPosition position) => new TransactionReceipt
        {
            TransactionHash = transaction.Id, BlockHash = position.Hash, BlockNumber = position.Round, TransactionIndex = position.Index,
            From = transaction.Sender, To = transaction.Receiver, ContractAddress = transaction.CreatedAppId?.ToString(), Type = transaction.Type,
            Status = "1", CumulativeGasUsed = "0", GasUsed = "0", LogsBloom = "", Logs = LogsOf(transaction, position),
        };

        /// <summary>Application logs, as hex data of the application account; ARC-28 events start with their 4-byte selector.</summary>
        private static List<TransactionLog> LogsOf(AvmTransaction transaction, BlockPosition position)
        {
            var address = AlgorandAddress.OfApplication(transaction.CreatedAppId ?? transaction.AppId);
            return transaction.Logs.Select((log, index) => new TransactionLog
            {
                Address = address, Data = log.ToHex(true), Topics = new List<string>(), LogIndex = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                TransactionIndex = position.Index, TransactionHash = transaction.Id, BlockHash = position.Hash, BlockNumber = position.Round, Removed = false,
            }).ToList();
        }

        private static async Task<BlockPosition> PositionOfAsync(AlgodSession session, AvmTransaction transaction)
        {
            var round = Integers.ToDecimalString(transaction.ConfirmedRound!.Value);
            var (hash, block, ids) = (session.GetAsync($"v2/blocks/{round}/hash"), session.GetAsync($"v2/blocks/{round}?header-only=true&format=json"),
                session.GetAsync($"v2/blocks/{round}/txids"));
            await Task.WhenAll(hash, block, ids).ConfigureAwait(false);
            var index = JsonValues.Items(ids.Result["blockTxids"]).Select(JsonValues.Text).ToList().IndexOf(transaction.Id);
            return new BlockPosition(round, JsonValues.Text(hash.Result["blockHash"]) ?? "", SharedResults.Milliseconds(JsonValues.Integer(block.Result["block"]?["ts"])),
                Integers.ToDecimalString(System.Math.Max(index, 0)));
        }

        /// <summary>Where a confirmed transaction sits: its round, block hash, block time in milliseconds and position in the block.</summary>
        private sealed class BlockPosition
        {
            public BlockPosition(string round, string hash, string timestamp, string index) => (Round, Hash, Timestamp, Index) = (round, hash, timestamp, index);

            public string Round { get; }
            public string Hash { get; }
            public string Timestamp { get; }
            public string Index { get; }
        }
    }
}
