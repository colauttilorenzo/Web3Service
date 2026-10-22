using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Certifie.Web3.Algorand
{
    /// <summary>The outcome of a simulated group: the result of its last transaction and what the group needs to succeed.</summary>
    internal sealed class Simulated
    {
        public Simulated(JsonObject result, Resources resources, BigInteger innerTransactions, string? failure, bool transactionFailed) =>
            (Result, Resources, InnerTransactions, Failure, TransactionFailed) = (result, resources, innerTransactions, failure, transactionFailed);

        public JsonObject Result { get; }
        public Resources Resources { get; }
        public BigInteger InnerTransactions { get; }
        /// <summary>Why the group would fail.</summary>
        public string? Failure { get; }
        /// <summary>
        /// True when a transaction would fail: an application rejects it or its sender cannot pay. False when only the group fee is short,
        /// as for reads, which simulate without a fee because they are never sent; algod names the transaction in every transaction failure.
        /// </summary>
        public bool TransactionFailed { get; }
    }

    /// <summary>
    /// Simulates a group before it is signed: finds the resources an application reads, prices its inner transactions,
    /// and rejects calls that would fail, so nothing is sent or paid for.
    /// </summary>
    internal static class Simulation
    {
        /// <summary>Inner transactions an application may send in one group; the simulation fee covers them.</summary>
        private const int MaxInnerTransactions = 256;

        /// <summary>The group ready to sign: with the resources found by simulation, the exact fee, and a group ID.</summary>
        public static async Task<List<UnsignedTransaction>> PlanAsync(AlgodSession session, AvmRequest request, string sender)
        {
            var parameters = session.Params;
            if (!request.CallsApplication) return UnsignedTransaction.AssignGroup(request.Build(sender, parameters, new BuildPlan(FeeOf(request.Options, parameters, 0))));
            var draft = request.Build(sender, parameters, new BuildPlan(await SimulationFeeAsync(session, sender).ConfigureAwait(false)));
            var simulated = await RunAsync(session, draft).ConfigureAwait(false);
            if (simulated.Failure != null) throw Rejected(draft.Last().Id, request.Explain(simulated.Failure));
            var fee = FeeOf(request.Options, parameters, simulated.InnerTransactions);
            return UnsignedTransaction.AssignGroup(request.Build(sender, parameters, new BuildPlan(fee, simulated.Resources)));
        }

        /// <summary>Runs a group without signatures, allowing the resources the application reads to be left out. Assigns the group ID.</summary>
        public static async Task<Simulated> RunAsync(AlgodSession session, List<UnsignedTransaction> transactions)
        {
            var response = await session.PostAsync("v2/transactions/simulate?format=json", Msgpack.Encode(RequestOf(transactions)), "application/msgpack")
                .ConfigureAwait(false);
            var group = response["txn-groups"]![0]!;
            var last = JsonValues.Items(group["txn-results"]).Last();
            var failure = JsonValues.Text(group["failure-message"]) is { Length: > 0 } message ? message : null;
            return new Simulated((JsonObject)last["txn-result"]!, ResourcesOf(group["unnamed-resources-accessed"], last["unnamed-resources-accessed"]),
                CountInner(last["txn-result"]), failure, failure != null && transactions.Any(transaction => failure.Contains(transaction.Id)));
        }

        /// <summary>The application rejected the call: it is never sent, so it costs nothing.</summary>
        public static Web3Exception Rejected(string transactionHash, string failure) =>
            new Web3Exception("TRANSACTION_REVERTED", $"The transaction would be rejected: {failure}", transactionHash: transactionHash);

        private static MsgpackMap RequestOf(List<UnsignedTransaction> transactions)
        {
            var txns = UnsignedTransaction.AssignGroup(transactions).Select(transaction => (object)transaction.ToUnsignedMap()).ToList();
            return new MsgpackMap().Add("txn-groups", new List<object> { new MsgpackMap().Add("txns", txns) })
                .Add("allow-empty-signatures", true).Add("allow-unnamed-resources", true);
        }

        /// <summary>options.GasPrice is the explicit fee; otherwise the minimum fee for the transaction and each inner transaction.</summary>
        private static BigInteger FeeOf(TransactionOptions options, SuggestedParams parameters, BigInteger innerTransactions) =>
            options.GasPrice ?? parameters.MinFee * (1 + innerTransactions);

        /// <summary>Enough fee for any inner transaction, without exceeding what the sender can spend: an overspent simulation never runs the application.</summary>
        private static async Task<BigInteger> SimulationFeeAsync(AlgodSession session, string sender)
        {
            var account = await session.GetAsync($"v2/accounts/{sender}?exclude=all&format=json").ConfigureAwait(false);
            var minFee = session.Params.MinFee;
            var spendable = JsonValues.Integer(account["amount"]) - JsonValues.Integer(account["min-balance"]) - minFee;
            return spendable < minFee ? minFee : BigInteger.Min(minFee * (1 + MaxInnerTransactions), spendable);
        }

        private static BigInteger CountInner(JsonNode? result) =>
            JsonValues.Items(result?["inner-txns"]).Aggregate(BigInteger.Zero, (total, inner) => total + 1 + CountInner(inner));

        /// <summary>The group's and the transaction's unnamed resources; local states and holdings need their account and application or asset.</summary>
        private static Resources ResourcesOf(params JsonNode?[] found)
        {
            var all = found.OfType<JsonNode>().ToList();
            var (locals, holdings) = (Items(all, "app-locals"), Items(all, "asset-holdings"));
            return new Resources(
                Items(all, "accounts").Concat(locals.Select(local => local?["account"])).Concat(holdings.Select(holding => holding?["account"]))
                    .Select(account => JsonValues.Text(account)!).ToList(),
                Items(all, "apps").Concat(locals.Select(local => local?["app"])).Select(JsonValues.Integer).ToList(),
                Items(all, "assets").Concat(holdings.Select(holding => holding?["asset"])).Select(JsonValues.Integer).ToList(),
                BoxesOf(all));
        }

        private static List<JsonNode?> Items(List<JsonNode> all, string key) => all.SelectMany(item => JsonValues.Items(item[key])).Cast<JsonNode?>().ToList();

        /// <summary>The boxes read, then an empty reference for each extra box of I/O budget the call needs.</summary>
        private static List<BoxReference> BoxesOf(List<JsonNode> all) =>
            Items(all, "boxes").Select(box => new BoxReference(JsonValues.Integer(box?["app"]), JsonValues.Base64(box?["name"])))
                .Concat(all.SelectMany(item => Enumerable.Repeat(new BoxReference(0, new byte[0]), (int)JsonValues.Integer(item["extra-box-refs"])))).ToList();
    }
}
