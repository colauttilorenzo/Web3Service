using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nethereum.Hex.HexConvertors.Extensions;

namespace Certifie.Web3.Algorand
{
    /// <summary>Algorand Standard Assets: tokens and NFTs created without a contract.</summary>
    internal static class Assets
    {
        private const int MaxUnitNameBytes = 8;
        private const int MaxAssetNameBytes = 32;
        private const int MaxUrlBytes = 96;
        private const int MaxDecimals = 19;
        private static readonly BigInteger MaxTotal = (BigInteger.One << 64) - 1;
        private static readonly Regex MetadataHash = new Regex("^0x[0-9a-f]{64}$", RegexOptions.IgnoreCase);

        /// <summary>The asset creation; the creator manages the asset and may later reconfigure it.</summary>
        public static AvmRequest Creation(TokenDefinition token, TransactionOptions? options)
        {
            var asset = Validate(token);
            return new AvmRequest(options, false, (sender, parameters, plan) => new List<UnsignedTransaction>
            {
                new UnsignedTransaction("acfg", sender, plan.Fee, parameters, TransactionFields.AssetCreation(asset, sender)),
            });
        }

        /// <summary>Name, unit name, decimals, total supply and URL of an asset, with its creator.</summary>
        public static async Task<TokenInfo> InfoAsync(AlgodSession session, string tokenId)
        {
            var id = Integers.ToDecimalString(Integers.Parse(tokenId, "tokenId"));
            var asset = (await session.OptionalAsync($"v2/assets/{id}").ConfigureAwait(false))?["params"] ?? throw Errors.TokenNotFound(id);
            return new TokenInfo
            {
                Standard = "ASA", Address = id, Name = JsonValues.Text(asset["name"]), Symbol = JsonValues.Text(asset["unit-name"]),
                Decimals = Integers.ToDecimalString(JsonValues.Integer(asset["decimals"])), TotalSupply = Integers.ToDecimalString(JsonValues.Integer(asset["total"])),
                TokenURI = JsonValues.Text(asset["url"]), Creator = JsonValues.Text(asset["creator"]),
            };
        }

        private static AssetFields Validate(TokenDefinition? token)
        {
            if (token == null) throw Errors.InvalidArgument("A token definition is required.");
            var decimals = token.Decimals ?? 0;
            if (decimals < 0 || decimals > MaxDecimals) throw Errors.InvalidArgument($"decimals must be an integer between 0 and {MaxDecimals}.");
            if (token.TotalSupply < 1 || token.TotalSupply > MaxTotal) throw Errors.InvalidArgument("totalSupply must be between 1 and 2^64 - 1.");
            return new AssetFields
            {
                AssetName = Text(token.Name, "name", MaxAssetNameBytes), UnitName = Text(token.Symbol, "symbol", MaxUnitNameBytes), Total = token.TotalSupply,
                Decimals = decimals, Url = token.Uri == null ? null : Text(token.Uri, "uri", MaxUrlBytes), MetadataHash = Hash(token.MetadataHash),
            };
        }

        private static string Text(string? value, string name, int maxBytes)
        {
            if (string.IsNullOrEmpty(value)) throw Errors.InvalidArgument($"{name} is required.");
            return Encoding.UTF8.GetByteCount(value) <= maxBytes ? value! : throw Errors.InvalidArgument($"{name} must be at most {maxBytes} bytes.");
        }

        private static byte[]? Hash(string? value)
        {
            if (value == null) return null;
            return MetadataHash.IsMatch(value) ? value.HexToByteArray() : throw Errors.InvalidArgument("metadataHash must be 32 bytes of 0x hex.");
        }
    }
}
