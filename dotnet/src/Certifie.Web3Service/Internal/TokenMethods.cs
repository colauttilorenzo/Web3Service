using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Certifie.Web3
{
    /// <summary>Token functions shared by every blockchain: which mint function to call and which token data the contract can return.</summary>
    internal static class TokenMethods
    {
        private static readonly string[] MintFunctions = { "safeMint", "mint", "mintTo" };

        /// <summary>The safeMint, mint or mintTo function whose parameter names match, in this order of preference; extra names are tried last.</summary>
        public static T MintFunction<T>(IReadOnlyList<T> methods, JsonObject parameters, params string[] extraNames) where T : IDescribedMethod
        {
            var names = MintFunctions.Concat(extraNames).ToList();
            var candidates = names.SelectMany(name => methods.Where(fn => fn.Method.Name == name && fn.Method.Kind != "read")).ToList();
            if (candidates.Count == 0) throw Errors.NotSupported($"The contract has no {string.Join(", ", names.Take(names.Count - 1))} or {names.Last()} function.");
            return candidates.FirstOrDefault(fn => ContractMethods.HasParameters(fn, parameters))
                ?? throw Errors.InvalidArgument($"Pass the parameters of one of {ContractMethods.Signatures(candidates.Cast<IDescribedMethod>())}.");
        }

        /// <summary>The reads the contract offers: contract data, plus the token's owner and URI when a token ID is given.</summary>
        public static List<(string Field, string Method)> TokenReads((string Field, string Method)[] contractReads, (string Field, string Method)[] tokenReads,
            IReadOnlyCollection<string> keys, bool withToken)
        {
            var reads = withToken ? contractReads.Concat(tokenReads) : contractReads;
            return reads.Where(read => keys.Contains(read.Method)).ToList();
        }

        public static void Assign(TokenInfo info, string field, string value)
        {
            switch (field)
            {
                case "name": info.Name = value; break;
                case "symbol": info.Symbol = value; break;
                case "decimals": info.Decimals = value; break;
                case "totalSupply": info.TotalSupply = value; break;
                case "owner": info.Owner = value; break;
                default: info.TokenURI = value; break;
            }
        }
    }
}
