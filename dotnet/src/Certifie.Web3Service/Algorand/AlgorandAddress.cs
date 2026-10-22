using System;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Certifie.Web3.Algorand
{
    /// <summary>Algorand addresses: the base32 of a 32-byte public key followed by the last 4 bytes of its SHA-512/256 hash.</summary>
    internal static class AlgorandAddress
    {
        public const int KeyBytes = 32;
        private const int ChecksumBytes = 4;
        private const int AddressLength = 58;
        private static readonly byte[] AppIdPrefix = Encoding.ASCII.GetBytes("appID");

        public static string Encode(byte[] publicKey) => Base32.Encode(publicKey.Concat(Checksum(publicKey)).ToArray());

        /// <summary>The public key of an address; fails with <c>INVALID_ARGUMENT</c> when the address is malformed.</summary>
        public static byte[] Decode(string address, string name = "address") =>
            TryDecode(address) ?? throw Errors.InvalidArgument($"{name} must be an Algorand address.");

        public static bool IsValid(string? address) => address != null && TryDecode(address) != null;

        /// <summary>The account of an application: the hash of "appID" followed by the application ID as 8 big-endian bytes.</summary>
        public static string OfApplication(BigInteger appId) => Encode(AvmCrypto.Sha512_256(AppIdPrefix, Uint64(appId)));

        public static byte[] Uint64(BigInteger value)
        {
            var bytes = value.ToByteArray().Take(sizeof(ulong)).ToArray();
            Array.Resize(ref bytes, sizeof(ulong));
            Array.Reverse(bytes);
            return bytes;
        }

        private static byte[]? TryDecode(string address)
        {
            var bytes = address.Length == AddressLength ? Base32.Decode(address) : null;
            if (bytes == null) return null;
            var publicKey = bytes.Take(KeyBytes).ToArray();
            return Checksum(publicKey).SequenceEqual(bytes.Skip(KeyBytes).Take(ChecksumBytes)) ? publicKey : null;
        }

        private static byte[] Checksum(byte[] publicKey) => AvmCrypto.Sha512_256(publicKey).Skip(KeyBytes - ChecksumBytes).ToArray();
    }
}
