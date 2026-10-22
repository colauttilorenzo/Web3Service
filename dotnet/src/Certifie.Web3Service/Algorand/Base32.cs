using System;
using System.Text;

namespace Certifie.Web3.Algorand
{
    /// <summary>RFC 4648 base32 without padding, as Algorand addresses and transaction IDs use it.</summary>
    internal static class Base32
    {
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        private const int BitsPerCharacter = 5;
        private const int BitsPerByte = 8;

        public static string Encode(byte[] bytes)
        {
            var text = new StringBuilder();
            var (buffer, bits) = (0, 0);
            foreach (var value in bytes)
            {
                (buffer, bits) = ((buffer << BitsPerByte) | value, bits + BitsPerByte);
                for (; bits >= BitsPerCharacter; bits -= BitsPerCharacter) text.Append(Alphabet[(buffer >> (bits - BitsPerCharacter)) & 31]);
            }
            if (bits > 0) text.Append(Alphabet[(buffer << (BitsPerCharacter - bits)) & 31]);
            return text.ToString();
        }

        /// <summary>The bytes of a base32 text; null when it holds another character.</summary>
        public static byte[]? Decode(string text)
        {
            var bytes = new byte[text.Length * BitsPerCharacter / BitsPerByte];
            var (buffer, bits, index) = (0, 0, 0);
            foreach (var character in text)
            {
                var value = Alphabet.IndexOf(character);
                if (value < 0) return null;
                (buffer, bits) = ((buffer << BitsPerCharacter) | value, bits + BitsPerCharacter);
                if (bits >= BitsPerByte) (bytes[index++], bits) = ((byte)(buffer >> (bits - BitsPerByte)), bits - BitsPerByte);
            }
            return bytes;
        }
    }
}
