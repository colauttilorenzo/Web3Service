using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nethereum.Hex.HexConvertors.Extensions;

namespace Certifie.Web3.Algorand
{
    /// <summary>What deploy needs on Algorand: the approval and clear programs, the state schema and the create method of the application.</summary>
    internal static class AppPrograms
    {
        private const int PageBytes = 2048;
        private const int MaxExtraPages = 3;
        private static readonly Regex Hex = new Regex("^0x(?:[0-9a-f]{2})+$", RegexOptions.IgnoreCase);
        private static readonly Regex TealSource = new Regex(@"^\s*#pragma");
        private static readonly string[] CreateNames = { "create", "createApplication" };

        /// <summary>The creation call: the create method with the constructor arguments, or a bare creation when the contract has none.</summary>
        public static async Task<AppCall> CreationCallAsync(AlgodSession session, string abi, AvmPrograms? programs, object[] constructorArguments)
        {
            var (contract, _) = Arc4Methods.RequireArc4(abi);
            var pair = programs ?? DescribedPrograms(contract);
            var creation = Schema(contract, await ProgramAsync(session, pair.Approval).ConfigureAwait(false), await ProgramAsync(session, pair.Clear).ConfigureAwait(false));
            var fn = CreateMethod(Arc4Methods.Describe(abi));
            if (fn == null && constructorArguments.Length > 0) throw Errors.InvalidArgument("The contract has no create method, so it takes no constructor arguments.");
            var args = fn == null ? new List<CallArgument>() : Arc4Methods.ToArguments(fn, ContractMethods.FromPositional(fn, constructorArguments));
            return new AppCall(0, fn, args, creation);
        }

        /// <summary>The programs of the ARC-56 description: compiled (byteCode), or TEAL (source) for the node to compile.</summary>
        private static AvmPrograms DescribedPrograms(JsonObject contract)
        {
            var (approval, clear) = (JsonValues.Text(contract["byteCode"]?["approval"]), JsonValues.Text(contract["byteCode"]?["clear"]));
            if (!string.IsNullOrEmpty(approval) && !string.IsNullOrEmpty(clear)) return new AvmPrograms { Approval = approval!, Clear = clear! };
            (approval, clear) = (JsonValues.Text(contract["source"]?["approval"]), JsonValues.Text(contract["source"]?["clear"]));
            if (string.IsNullOrEmpty(approval) || string.IsNullOrEmpty(clear)) throw Errors.InvalidArgument("The contract description has no byteCode or source; pass the programs as AvmPrograms.");
            return new AvmPrograms { Approval = TextOf(approval!), Clear = TextOf(clear!) };
        }

        /// <summary>A program as 0x hex, as base64, or as TEAL source compiled by the node (which needs EnableDeveloperAPI).</summary>
        private static async Task<byte[]> ProgramAsync(AlgodSession session, string value)
        {
            if (Hex.IsMatch(value)) return value.HexToByteArray();
            if (!TealSource.IsMatch(value)) return Base64Program(value);
            var compiled = await session.PostAsync("v2/teal/compile", Encoding.UTF8.GetBytes(value), "text/plain").ConfigureAwait(false);
            return JsonValues.Base64(compiled["result"]);
        }

        private static byte[] Base64Program(string value)
        {
            try { return Convert.FromBase64String(value); }
            catch (FormatException) { throw Errors.InvalidArgument("A program must be 0x hex, base64 or TEAL source."); }
        }

        /// <summary>The create method: an ARC-56 method whose create actions include NoOp, else one named create or createApplication.</summary>
        private static Arc4Function? CreateMethod(List<Arc4Function> functions) =>
            functions.FirstOrDefault(fn => JsonValues.Items(fn.Spec["actions"]?["create"]).Any(action => JsonValues.Text(action) == "NoOp"))
            ?? functions.FirstOrDefault(fn => fn.Spec["actions"] == null && CreateNames.Contains(fn.Method.Name));

        /// <summary>The ARC-56 state schema, or the ARC-32 one under global and local. Both programs may take 2048 bytes, plus 2048 per extra page.</summary>
        private static Creation Schema(JsonObject contract, byte[] approval, byte[] clear)
        {
            var state = contract["state"];
            return new Creation
            {
                ApprovalProgram = approval, ClearProgram = clear,
                GlobalInts = Count(state?["schema"]?["global"]?["ints"], state?["global"]?["num_uints"]),
                GlobalBytes = Count(state?["schema"]?["global"]?["bytes"], state?["global"]?["num_byte_slices"]),
                LocalInts = Count(state?["schema"]?["local"]?["ints"], state?["local"]?["num_uints"]),
                LocalBytes = Count(state?["schema"]?["local"]?["bytes"], state?["local"]?["num_byte_slices"]),
                ExtraPages = Math.Min(MaxExtraPages, Math.Max(0, (approval.Length + clear.Length + PageBytes - 1) / PageBytes - 1)),
            };
        }

        private static int Count(JsonNode? arc56, JsonNode? arc32) => (int)JsonValues.Integer(arc56 ?? arc32);

        private static string TextOf(string base64) => Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
}
