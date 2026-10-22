using System;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Nethereum.Hex.HexTypes;

namespace Certifie.Web3
{
    /// <summary>Parsing and formatting of the non-negative integers used by JSON-RPC. Arithmetic uses <see cref="BigInteger"/>.</summary>
    internal static class Integers
    {
        private static readonly Regex IntegerPattern = new Regex("^(?:0x[0-9a-f]+|[0-9]+)$", RegexOptions.IgnoreCase);

        /// <summary>Parses a decimal (<c>"1337"</c>) or 0x hex (<c>"0x539"</c>) integer.</summary>
        public static BigInteger Parse(string? value, string name)
        {
            if (value == null || !IntegerPattern.IsMatch(value))
                throw Errors.InvalidArgument($"{name} must be a non-negative integer; use strings for large values.");
            return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? BigInteger.Parse("0" + value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
                : BigInteger.Parse(value, CultureInfo.InvariantCulture);
        }

        public static BigInteger RequireNonNegative(BigInteger value, string name) =>
            value < 0 ? throw Errors.InvalidArgument($"{name} must be a non-negative integer.") : value;

        public static string ToDecimalString(BigInteger value) => value.ToString(CultureInfo.InvariantCulture);

        public static string? ToOptionalDecimalString(HexBigInteger? value) => value == null ? null : ToDecimalString(value.Value);

        /// <summary>Lowercase 0x hex without leading zeros, e.g. <c>0x539</c>.</summary>
        public static string ToHex(BigInteger value) =>
            "0x" + (value.IsZero ? "0" : value.ToString("x", CultureInfo.InvariantCulture).TrimStart('0'));

        public static string NormalizeChainId(string chainId) => ToHex(Parse(chainId, "chainId"));
    }
}
