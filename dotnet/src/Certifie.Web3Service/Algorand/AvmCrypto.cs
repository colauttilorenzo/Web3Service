using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace Certifie.Web3.Algorand
{
    /// <summary>The hash and signature schemes of Algorand: SHA-512/256 and Ed25519.</summary>
    internal static class AvmCrypto
    {
        public const int SeedBytes = 32;

        /// <summary>SHA-512/256 of the parts, concatenated.</summary>
        public static byte[] Sha512_256(params byte[][] parts)
        {
            var digest = new Sha512tDigest(256);
            foreach (var part in parts) digest.BlockUpdate(part, 0, part.Length);
            var hash = new byte[digest.GetDigestSize()];
            digest.DoFinal(hash, 0);
            return hash;
        }

        /// <summary>The Ed25519 public key of a 32-byte seed.</summary>
        public static byte[] PublicKey(byte[] seed)
        {
            var publicKey = new byte[Ed25519.PublicKeySize];
            Ed25519.GeneratePublicKey(seed, 0, publicKey, 0);
            return publicKey;
        }

        public static byte[] Sign(byte[] seed, byte[] message)
        {
            var signature = new byte[Ed25519.SignatureSize];
            Ed25519.Sign(seed, 0, message, 0, message.Length, signature, 0);
            return signature;
        }

        public static byte[] RandomSeed()
        {
            var seed = new byte[SeedBytes];
            using var random = RandomNumberGenerator.Create();
            random.GetBytes(seed);
            return seed;
        }
    }
}
