using System;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Certifie.Web3.Bitcoin
{
    /// <summary>Bitcoin transactions as Esplora returns them (GET /tx/:txid, values and fees in satoshis), in the same JSON shape as on the other blockchains.</summary>
    internal static class Transactions
    {
        public static async Task<TransactionDetails> DetailsAsync(EsploraSession session, string transactionHash, ResolvedProvider network)
        {
            var transaction = await GetAsync(session, transactionHash).ConfigureAwait(false);
            var position = await PositionOfAsync(session, transaction).ConfigureAwait(false);
            return new TransactionDetails
            {
                Hash = JsonValues.Text(transaction["txid"]) ?? "", From = SenderOf(transaction), To = RecipientOf(transaction), Value = ValueOf(transaction),
                Nonce = "0", Gas = "0", Input = "0x", Type = TypeOf(transaction), BlockHash = position?.Hash, BlockNumber = position?.Height,
                TransactionIndex = position?.Index, Pending = position == null, Timestamp = position?.Timestamp,
                Receipt = position == null ? null : ReceiptOf(transaction, position), NativeFee = position == null ? null : FeeOf(transaction, network),
            };
        }

        /// <summary>The confirmed transaction as a result, like a receipt on EVM networks.</summary>
        public static async Task<TransactionResult> ReceiptAsync(EsploraSession session, string transactionHash, TransactionResult result, ResolvedProvider network)
        {
            var transaction = await GetAsync(session, transactionHash).ConfigureAwait(false);
            var position = await PositionOfAsync(session, transaction).ConfigureAwait(false) ?? throw Errors.TransactionNotFound(transactionHash);
            result.RequestedAt = SharedResults.RequestedAt(result.RequestedAt);
            (result.Tx, result.CompletedAt, result.RejectedAt, result.Error) = (ReceiptOf(transaction, position), position.Timestamp, null, null);
            result.NativeFee = FeeOf(transaction, network);
            return result;
        }

        private static async Task<JsonNode> GetAsync(EsploraSession session, string transactionHash) =>
            await session.JsonAsync($"/tx/{Uri.EscapeDataString(transactionHash)}").ConfigureAwait(false) ?? throw Errors.TransactionNotFound(transactionHash);

        private static async Task<BlockPosition?> PositionOfAsync(EsploraSession session, JsonNode transaction)
        {
            var status = transaction["status"];
            var hash = JsonValues.Text(status?["block_hash"]);
            if (!JsonValues.Flag(status?["confirmed"]) || hash == null) return null;
            var ids = JsonValues.Items(await session.JsonAsync($"/block/{hash}/txids").ConfigureAwait(false)).Select(JsonValues.Text).ToList();
            return new BlockPosition(hash, Integers.ToDecimalString(JsonValues.Integer(status?["block_height"])),
                Integers.ToDecimalString(Math.Max(ids.IndexOf(JsonValues.Text(transaction["txid"])), 0)), SharedResults.Milliseconds(JsonValues.Integer(status?["block_time"])));
        }

        /// <summary>Gas and logs do not apply to Bitcoin, so they are zero and empty.</summary>
        private static TransactionReceipt ReceiptOf(JsonNode transaction, BlockPosition position) => new TransactionReceipt
        {
            TransactionHash = JsonValues.Text(transaction["txid"]) ?? "", BlockHash = position.Hash, BlockNumber = position.Height, TransactionIndex = position.Index,
            From = SenderOf(transaction), To = RecipientOf(transaction), ContractAddress = null, Type = TypeOf(transaction),
            Status = "1", CumulativeGasUsed = "0", GasUsed = "0", LogsBloom = "",
        };

        /// <summary>The address of the first input; empty for coinbase transactions.</summary>
        private static string SenderOf(JsonNode transaction) => JsonValues.Text(transaction["vin"]?[0]?["prevout"]?["scriptpubkey_address"]) ?? "";

        private static string? RecipientOf(JsonNode transaction) => JsonValues.Text(transaction["vout"]?[0]?["scriptpubkey_address"]);

        /// <summary>Satoshis sent to other addresses: the outputs that do not return change to the sender.</summary>
        private static string ValueOf(JsonNode transaction) => Integers.ToDecimalString(JsonValues.Items(transaction["vout"])
            .Where(output => JsonValues.Text(output["scriptpubkey_address"]) != SenderOf(transaction))
            .Aggregate(BigInteger.Zero, (total, output) => total + JsonValues.Integer(output["value"])));

        private static string TypeOf(JsonNode transaction) => Integers.ToDecimalString(JsonValues.Integer(transaction["version"]));

        private static NativeFee FeeOf(JsonNode transaction, ResolvedProvider network) => SharedResults.NativeFee(JsonValues.Integer(transaction["fee"]), network);

        /// <summary>Where a confirmed transaction sits: its block and position.</summary>
        private sealed class BlockPosition
        {
            public BlockPosition(string hash, string height, string index, string timestamp) => (Hash, Height, Index, Timestamp) = (hash, height, index, timestamp);

            public string Hash { get; }
            public string Height { get; }
            public string Index { get; }
            public string Timestamp { get; }
        }
    }
}
