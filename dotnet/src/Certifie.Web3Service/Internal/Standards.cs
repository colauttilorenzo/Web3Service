using System.Collections.Generic;
using System.Linq;

namespace Certifie.Web3
{
    /// <summary>Recognizes token and contract standards from the functions of a contract. A standard matches when the contract has all of its functions.</summary>
    internal static class Standards
    {
        /// <summary>The standards whose functions (signatures on EVM, method names on Algorand) all appear among the keys, in table order.</summary>
        public static List<string> Detect((string Id, string[] Functions, string? Extended)[] table, IReadOnlyCollection<string> keys)
        {
            var found = new List<string>();
            foreach (var (id, functions, extended) in table)
            {
                if (functions.All(keys.Contains) && (extended == null || found.Contains(extended))) found.Add(id);
            }
            return found;
        }

        /// <summary>The first of the token standards among the detected ones.</summary>
        public static string? TokenStandard(string[] tokenStandards, IReadOnlyCollection<string> standards) => tokenStandards.FirstOrDefault(standards.Contains);
    }
}
