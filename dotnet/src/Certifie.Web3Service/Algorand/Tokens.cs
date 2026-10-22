using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Certifie.Web3.Algorand
{
    /// <summary>Algorand application tokens: the IDs of minted NFTs, and which token data an ARC-200 or ARC-72 contract can return.</summary>
    internal static class Tokens
    {
        /// <summary>ARC-28 selector of arc72_Transfer(address,address,uint256): the first 4 bytes of its SHA-512/256 hash.</summary>
        private static readonly byte[] Arc72Transfer = { 0xd8, 0x08, 0xd4, 0xf4 };
        private static readonly Arc4Tuple TransferFields = (Arc4Tuple)Arc4Type.Parse("(address,address,uint256)");
        private static readonly (string Field, string Method)[] ContractReads =
        {
            ("name", "arc200_name"), ("symbol", "arc200_symbol"), ("decimals", "arc200_decimals"), ("totalSupply", "arc200_totalSupply"),
            ("totalSupply", "arc72_totalSupply"),
        };
        private static readonly (string Field, string Method)[] TokenReadList = { ("owner", "arc72_ownerOf"), ("tokenURI", "arc72_tokenURI") };

        /// <summary>IDs of the NFTs the application minted: its arc72_Transfer events from the zero address.</summary>
        public static List<string> MintedTokenIds(IEnumerable<byte[]> logs) => logs.Where(IsMint)
            .Select(log => (List<object>)TransferFields.Decode(Arc4Tuple.Slice(log, Arc72Transfer.Length, log.Length - Arc72Transfer.Length)))
            .Select(fields => Integers.ToDecimalString((BigInteger)fields[2])).ToList();

        /// <summary>The reads the contract offers: token data, plus the NFT's owner and URI when a token ID is given.</summary>
        public static List<(string Field, string Method)> TokenReads(IReadOnlyCollection<string> names, bool withToken) =>
            TokenMethods.TokenReads(ContractReads, TokenReadList, names, withToken);

        private static bool IsMint(byte[] log) => log.Length >= Arc72Transfer.Length + TransferFields.StaticSize
            && log.Take(Arc72Transfer.Length).SequenceEqual(Arc72Transfer) && log.Skip(Arc72Transfer.Length).Take(AlgorandAddress.KeyBytes).All(value => value == 0);
    }
}
