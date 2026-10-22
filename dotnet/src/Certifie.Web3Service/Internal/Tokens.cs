using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Certifie.Web3
{
    /// <summary>Tokens and NFTs: which mint function to call, the IDs of the minted tokens, and which token data the contract can return.</summary>
    internal static class Tokens
    {
        private const string Transfer = "0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef";
        private const string TransferSingle = "0xc3d58168c5ae7397731d063d5bbf3d657854427343f4c083240f7aacaa2d0f62";
        private const string TransferBatch = "0x4a39dc06d4c0dbc64b70af90fd698a233a518aa5d07e595d983b8c0526c8f7fb";
        private const int WordDigits = 64;
        private const int WordBytes = 32;
        private static readonly string ZeroTopic = "0x" + new string('0', WordDigits);
        private static readonly string[] MintFunctions = { "safeMint", "mint", "mintTo" };
        private static readonly (string Field, string Signature)[] ContractReads =
            { ("name", "name()"), ("symbol", "symbol()"), ("decimals", "decimals()"), ("totalSupply", "totalSupply()") };
        private static readonly (string Field, string Signature)[] TokenReadList =
            { ("owner", "ownerOf(uint256)"), ("tokenURI", "tokenURI(uint256)"), ("tokenURI", "uri(uint256)") };

        /// <summary>The safeMint, mint or mintTo function whose parameter names match, in this order of preference.</summary>
        public static AbiFunction MintFunction(IReadOnlyList<AbiFunction> functions, JsonObject parameters)
        {
            var candidates = MintFunctions.SelectMany(name => functions.Where(fn => fn.Method.Name == name && fn.Method.Kind != "read")).ToList();
            if (candidates.Count == 0) throw Errors.NotSupported("The contract has no safeMint, mint or mintTo function.");
            return candidates.FirstOrDefault(fn => AbiMethods.HasParameters(fn, parameters))
                ?? throw Errors.InvalidArgument($"Pass the parameters of one of {AbiMethods.Signatures(candidates)}.");
        }

        /// <summary>IDs of the tokens the contract created: its transfers from the zero address.</summary>
        public static List<string> MintedTokenIds(IEnumerable<TransactionLog> logs, string address) =>
            logs.Where(log => string.Equals(log.Address, address, StringComparison.OrdinalIgnoreCase)).SelectMany(MintedIds).ToList();

        /// <summary>The reads that apply to the contract: contract data, plus the token's owner and URI when a token ID is given.</summary>
        public static List<(string Field, string Signature)> TokenReads(IReadOnlyCollection<string> signatures, bool withToken)
        {
            var reads = withToken ? ContractReads.Concat(TokenReadList) : ContractReads;
            return reads.Where(read => signatures.Contains(read.Signature)).ToList();
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

        /// <summary>ERC-1155 URIs may contain <c>{id}</c>, which stands for the token ID as 64 lowercase hex digits.</summary>
        public static TokenInfo ExpandTokenUri(TokenInfo info)
        {
            if (info.TokenURI == null || info.TokenId == null) return info;
            var hex = Integers.ToHex(Integers.Parse(info.TokenId, "tokenId")).Substring(2).PadLeft(WordDigits, '0');
            info.TokenURI = info.TokenURI.Replace("{id}", hex);
            return info;
        }

        private static IEnumerable<string> MintedIds(TransactionLog log)
        {
            var topics = (log.Topics ?? new List<string>()).Select(topic => topic.ToLowerInvariant()).ToList();
            var (topic, indexed) = (topics.FirstOrDefault(), topics.Skip(1).ToList());
            if (topic == Transfer && indexed.Count == 3 && indexed[0] == ZeroTopic) return new[] { Integers.ToDecimalString(Integers.Parse(indexed[2], "tokenId")) };
            if (topic == TransferSingle && indexed.Count > 1 && indexed[1] == ZeroTopic) return new[] { Word(log.Data ?? "0x", 0) };
            if (topic == TransferBatch && indexed.Count > 1 && indexed[1] == ZeroTopic) return BatchIds(log.Data ?? "0x");
            return Array.Empty<string>();
        }

        /// <summary>TransferBatch data starts with the offset of the ids array, which holds its length followed by the ids.</summary>
        private static IEnumerable<string> BatchIds(string data)
        {
            var offset = int.Parse(Word(data, 0), CultureInfo.InvariantCulture) / WordBytes;
            var length = int.Parse(Word(data, offset), CultureInfo.InvariantCulture);
            return Enumerable.Range(0, length).Select(index => Word(data, offset + 1 + index)).ToList();
        }

        /// <summary>The 32-byte word at the index, as a decimal string.</summary>
        private static string Word(string data, int index)
        {
            var start = 2 + index * WordDigits;
            var digits = start >= data.Length ? "0" : data.Substring(start, Math.Min(WordDigits, data.Length - start));
            return Integers.ToDecimalString(Integers.Parse("0x" + digits, "data"));
        }
    }
}
