using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Certifie.Web3.Algorand
{
    /// <summary>Readable reasons for rejected application calls: ARC-56 maps the program counters of failing opcodes to error messages.</summary>
    internal static class ProgramErrors
    {
        private const string InnerFailure = "inner tx";
        private static readonly Regex ProgramCounter = new Regex(@"pc=(\d+)");

        /// <summary>Program counter → error message of the approval program; empty for ARC-4 and ARC-32 descriptions, which have none.</summary>
        public static IReadOnlyDictionary<int, string> Of(JsonObject contract)
        {
            var (approval, messages) = (contract["sourceInfo"]?["approval"], new Dictionary<int, string>());
            if (JsonValues.Text(approval?["pcOffsetMethod"]) != "none") return messages;
            foreach (var entry in JsonValues.Items(approval!["sourceInfo"]))
            {
                var message = JsonValues.Text(entry["errorMessage"]);
                foreach (var pc in message == null ? new List<JsonNode>() : JsonValues.Items(entry["pc"])) messages[(int)JsonValues.Integer(pc)] = message!;
            }
            return messages;
        }

        /// <summary>The failure with the contract's message first, e.g. <c>already notarized (logic eval error: assert failed pc=201…)</c>.</summary>
        public static string Explain(IReadOnlyDictionary<int, string> messages, string failure)
        {
            var match = ProgramCounter.Match(failure);
            if (failure.Contains(InnerFailure) || !match.Success) return failure;
            return messages.TryGetValue(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), out var message) ? $"{message} ({failure})" : failure;
        }
    }
}
