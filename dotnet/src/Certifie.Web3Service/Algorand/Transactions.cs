using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Certifie.Web3.Algorand
{
    /// <summary>The fields of a transaction the service reports, whichever API returned it.</summary>
    internal sealed class AvmTransaction
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string Sender { get; set; } = "";
        /// <summary>Receiver of a payment or asset transfer, account of a called application; null for creations.</summary>
        public string? Receiver { get; set; }
        public BigInteger Amount { get; set; }
        public BigInteger Fee { get; set; }
        public BigInteger AppId { get; set; }
        public List<byte[]> AppArgs { get; set; } = new List<byte[]>();
        public BigInteger? CreatedAppId { get; set; }
        public BigInteger? CreatedAssetId { get; set; }
        /// <summary>Assets created by the inner transactions of an application call.</summary>
        public List<BigInteger> InnerCreatedAssets { get; set; } = new List<BigInteger>();
        public BigInteger? ConfirmedRound { get; set; }
        public List<byte[]> Logs { get; set; } = new List<byte[]>();
    }

    /// <summary>Algorand transactions read by ID, from algod (pending and recently confirmed) or from the indexer, as one model.</summary>
    internal static class Transactions
    {
        /// <summary>The transaction from algod, which keeps pending and recent ones, else from the indexer when one is configured.</summary>
        public static async Task<AvmTransaction> GetAsync(AlgodSession session, string transactionId)
        {
            var pending = await GetPendingAsync(session, transactionId).ConfigureAwait(false);
            if (pending != null) return FromPending(transactionId, pending);
            var indexed = (await session.OptionalIndexedAsync($"v2/transactions/{Uri.EscapeDataString(transactionId)}").ConfigureAwait(false))?["transaction"];
            return indexed != null ? FromIndexer(indexed) : throw Errors.TransactionNotFound(transactionId);
        }

        /// <summary>Like GetPendingAsync, but an RPC failure also counts as "unknown".</summary>
        public static async Task<JsonObject?> TryPendingAsync(AlgodSession session, string transactionId)
        {
            try { return await GetPendingAsync(session, transactionId).ConfigureAwait(false); }
            catch (Exception error) when (!session.IsCancellation(error)) { return null; }
        }

        public static AvmTransaction FromPending(string id, JsonObject info)
        {
            var txn = info["txn"]?["txn"];
            var appId = JsonValues.Integer(txn?["apid"]);
            return new AvmTransaction
            {
                Id = id, Type = JsonValues.Text(txn?["type"]) ?? "", Sender = JsonValues.Text(txn?["snd"]) ?? "", AppId = appId, Fee = JsonValues.Integer(txn?["fee"]),
                Receiver = ReceiverOf(JsonValues.Text(txn?["type"]), appId, JsonValues.Text(txn?["rcv"] ?? txn?["arcv"])),
                Amount = JsonValues.Integer(txn?["amt"] ?? txn?["aamt"]), AppArgs = JsonValues.Items(txn?["apaa"]).Select(JsonValues.Base64).ToList(),
                CreatedAppId = JsonValues.OptionalInteger(info["application-index"]), CreatedAssetId = JsonValues.OptionalInteger(info["asset-index"]),
                ConfirmedRound = Positive(info["confirmed-round"]), InnerCreatedAssets = InnerAssets(info, "asset-index"), Logs = LogsOf(info),
            };
        }

        public static AvmTransaction FromIndexer(JsonNode transaction)
        {
            var (app, type) = (transaction["application-transaction"], JsonValues.Text(transaction["tx-type"]));
            var transfer = transaction["payment-transaction"] ?? transaction["asset-transfer-transaction"];
            return new AvmTransaction
            {
                Id = JsonValues.Text(transaction["id"]) ?? "", Type = type ?? "", Sender = JsonValues.Text(transaction["sender"]) ?? "",
                AppId = JsonValues.Integer(app?["application-id"]), Receiver = ReceiverOf(type, JsonValues.Integer(app?["application-id"]), JsonValues.Text(transfer?["receiver"])),
                Amount = JsonValues.Integer(transfer?["amount"]), Fee = JsonValues.Integer(transaction["fee"]), AppArgs = JsonValues.Items(app?["application-args"]).Select(JsonValues.Base64).ToList(),
                CreatedAppId = JsonValues.OptionalInteger(transaction["created-application-index"]), CreatedAssetId = JsonValues.OptionalInteger(transaction["created-asset-index"]),
                ConfirmedRound = Positive(transaction["confirmed-round"]), InnerCreatedAssets = InnerAssets(transaction, "created-asset-index"), Logs = LogsOf(transaction),
            };
        }

        private static Task<JsonObject?> GetPendingAsync(AlgodSession session, string transactionId) =>
            session.OptionalAsync($"v2/transactions/pending/{Uri.EscapeDataString(transactionId)}?format=json");

        /// <summary>A called application receives through its account; a creation has no receiver.</summary>
        private static string? ReceiverOf(string? type, BigInteger appId, string? receiver) =>
            type != "appl" ? receiver : appId.IsZero ? null : AlgorandAddress.OfApplication(appId);

        private static BigInteger? Positive(JsonNode? round) => JsonValues.Integer(round) is var value && value > 0 ? value : (BigInteger?)null;

        private static List<BigInteger> InnerAssets(JsonNode transaction, string key) =>
            JsonValues.Items(transaction["inner-txns"]).Select(inner => JsonValues.Integer(inner[key])).Where(id => id > 0).ToList();

        private static List<byte[]> LogsOf(JsonNode transaction) => JsonValues.Items(transaction["logs"]).Select(JsonValues.Base64).ToList();
    }
}
