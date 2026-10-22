using System.Collections.Generic;

namespace Certifie.Web3.Evm
{
    /// <summary>Recognizes ERC standards from the functions of an ABI. A standard matches when the ABI has all of its functions.</summary>
    internal static class ErcStandards
    {
        /// <summary>Standard, the function signatures it requires, and the standard it extends.</summary>
        private static readonly (string Id, string[] Functions, string? Extended)[] Table =
        {
            ("ERC-20", new[] { "totalSupply()", "balanceOf(address)", "transfer(address,uint256)", "transferFrom(address,address,uint256)",
                "approve(address,uint256)", "allowance(address,address)" }, null),
            ("ERC-721", new[] { "balanceOf(address)", "ownerOf(uint256)", "safeTransferFrom(address,address,uint256)",
                "safeTransferFrom(address,address,uint256,bytes)", "transferFrom(address,address,uint256)", "approve(address,uint256)",
                "setApprovalForAll(address,bool)", "getApproved(uint256)", "isApprovedForAll(address,address)" }, null),
            ("ERC-721 Metadata", new[] { "name()", "symbol()", "tokenURI(uint256)" }, "ERC-721"),
            ("ERC-721 Enumerable", new[] { "totalSupply()", "tokenOfOwnerByIndex(address,uint256)", "tokenByIndex(uint256)" }, "ERC-721"),
            ("ERC-1155", new[] { "balanceOf(address,uint256)", "balanceOfBatch(address[],uint256[])", "setApprovalForAll(address,bool)",
                "isApprovedForAll(address,address)", "safeTransferFrom(address,address,uint256,uint256,bytes)",
                "safeBatchTransferFrom(address,address,uint256[],uint256[],bytes)" }, null),
            ("ERC-1155 Metadata URI", new[] { "uri(uint256)" }, "ERC-1155"),
            ("ERC-165", new[] { "supportsInterface(bytes4)" }, null),
            ("ERC-173", new[] { "owner()", "transferOwnership(address)" }, null),
            ("ERC-2981", new[] { "royaltyInfo(uint256,uint256)" }, null),
        };

        private static readonly string[] TokenStandards = { "ERC-20", "ERC-721", "ERC-1155" };

        /// <summary>The standards whose functions all appear among the signatures, e.g. <c>["ERC-721", "ERC-721 Metadata", "ERC-165"]</c>.</summary>
        public static List<string> Detect(IReadOnlyCollection<string> signatures) => Standards.Detect(Table, signatures);

        /// <summary>The token standard among the detected ones.</summary>
        public static string? TokenStandard(IReadOnlyCollection<string> standards) => Standards.TokenStandard(TokenStandards, standards);
    }
}
