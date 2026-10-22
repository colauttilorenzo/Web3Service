using System.Collections.Generic;
using System.Linq;
using NBitcoin;

namespace Certifie.Web3.Algorand
{
    /// <summary>
    /// Algorand's 25-word mnemonics: the 32-byte seed as 24 words of 11 bits (little-endian, from the BIP-39 English list),
    /// followed by a checksum word taken from the first 2 bytes of the seed's SHA-512/256 hash.
    /// </summary>
    internal static class Mnemonics
    {
        private const int Words = 25;
        private const int WordBits = 11;
        private const int ByteBits = 8;
        private const int ChecksumBytes = 2;

        public static string FromSeed(byte[] seed) => string.Join(" ", ToWords(seed).Append(ChecksumWord(seed)));

        /// <summary>The seed a mnemonic encodes; fails with <c>INVALID_ARGUMENT</c> when a word is unknown or the checksum is wrong.</summary>
        public static byte[] ToSeed(string mnemonic)
        {
            var words = (mnemonic ?? "").Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != Words) throw Invalid("it must have 25 words");
            var bytes = Repack(words.Take(Words - 1).Select(IndexOf), WordBits, ByteBits);
            if (bytes.Count != AvmCrypto.SeedBytes + 1 || bytes[AvmCrypto.SeedBytes] != 0) throw Invalid("failed to decode mnemonic");
            var seed = bytes.Take(AvmCrypto.SeedBytes).Select(value => (byte)value).ToArray();
            return ChecksumWord(seed) == words[Words - 1] ? seed : throw Invalid("wrong checksum");
        }

        private static IEnumerable<string> ToWords(byte[] bytes) =>
            Repack(bytes.Select(value => (int)value), ByteBits, WordBits).Select(index => Wordlist.English.GetWordAtIndex(index));

        private static string ChecksumWord(byte[] seed) => ToWords(AvmCrypto.Sha512_256(seed).Take(ChecksumBytes).ToArray()).First();

        private static int IndexOf(string word) =>
            Wordlist.English.WordExists(word, out var index) ? index : throw Invalid("the mnemonic contains a word that is not in the wordlist");

        /// <summary>Regroups little-endian values of one bit width into another; the remaining bits form a last value.</summary>
        private static List<int> Repack(IEnumerable<int> values, int fromBits, int toBits)
        {
            var (result, buffer, bits) = (new List<int>(), 0, 0);
            foreach (var value in values)
            {
                (buffer, bits) = (buffer | (value << bits), bits + fromBits);
                for (; bits >= toBits; bits -= toBits, buffer >>= toBits) result.Add(buffer & ((1 << toBits) - 1));
            }
            if (bits > 0) result.Add(buffer);
            return result;
        }

        private static Web3Exception Invalid(string reason) => Errors.InvalidArgument($"The mnemonic is not a valid 25-word Algorand mnemonic: {reason}");
    }
}
