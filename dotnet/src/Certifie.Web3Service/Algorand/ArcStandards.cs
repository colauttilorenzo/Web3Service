using System.Collections.Generic;

namespace Certifie.Web3.Algorand
{
    /// <summary>Recognizes ARC standards from the method names of an ARC-4 contract.</summary>
    internal static class ArcStandards
    {
        /// <summary>Standard, the method names it requires, and the standard it extends.</summary>
        private static readonly (string Id, string[] Functions, string? Extended)[] Table =
        {
            ("ARC-200", new[] { "arc200_name", "arc200_symbol", "arc200_decimals", "arc200_totalSupply", "arc200_balanceOf", "arc200_transfer",
                "arc200_transferFrom", "arc200_approve", "arc200_allowance" }, null),
            ("ARC-72", new[] { "arc72_ownerOf", "arc72_transferFrom", "arc72_tokenURI" }, null),
            ("ARC-72 Enumeration", new[] { "arc72_balanceOf", "arc72_totalSupply", "arc72_tokenByIndex" }, "ARC-72"),
            ("ARC-72 Management", new[] { "arc72_approve", "arc72_setApprovalForAll", "arc72_getApproved", "arc72_isApprovedForAll" }, "ARC-72"),
            ("ARC-73", new[] { "supportsInterface" }, null),
        };

        private static readonly string[] TokenStandards = { "ARC-200", "ARC-72" };

        /// <summary>The standards whose methods all appear among the names, e.g. <c>["ARC-72", "ARC-73"]</c>.</summary>
        public static List<string> Detect(IReadOnlyCollection<string> names) => Standards.Detect(Table, names);

        /// <summary>The token standard among the detected ones.</summary>
        public static string? TokenStandard(IReadOnlyCollection<string> standards) => Standards.TokenStandard(TokenStandards, standards);
    }
}
