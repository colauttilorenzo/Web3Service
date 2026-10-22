using System;
using System.Globalization;
using System.Numerics;

namespace Certifie.Web3
{
    /// <summary>Parts of a transaction result shared by every blockchain.</summary>
    internal static class SharedResults
    {
        /// <summary>The caller's <c>requestedAt</c> as a decimal string, or the current time in milliseconds.</summary>
        public static string RequestedAt(string? requestedAt) => requestedAt == null
            ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
            : Integers.ToDecimalString(Integers.Parse(requestedAt, "requestedAt"));

        /// <summary>A fee in the smallest unit of the native currency (wei, microAlgos, satoshis).</summary>
        public static NativeFee NativeFee(BigInteger amount, ResolvedProvider provider) => new NativeFee
        {
            Amount = Integers.ToDecimalString(amount), Symbol = provider.Symbol, Decimals = provider.Decimals.ToString(CultureInfo.InvariantCulture),
        };

        /// <summary>Block time in seconds as milliseconds, as a decimal string.</summary>
        public static string Milliseconds(BigInteger seconds) => Integers.ToDecimalString(seconds * 1000);
    }
}
