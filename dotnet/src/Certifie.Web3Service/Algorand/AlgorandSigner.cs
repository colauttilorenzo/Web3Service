using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Nethereum.Hex.HexConvertors.Extensions;

namespace Certifie.Web3.Algorand
{
    /// <summary>Signs Algorand transactions with a local key: a 25-word mnemonic or a private key.</summary>
    internal sealed class AlgorandSigner
    {
        private const int SecretKeyBytes = 64;
        private static readonly Regex SeedHex = new Regex("^0x[0-9a-f]{64}$", RegexOptions.IgnoreCase);
        private readonly byte[] _seed;

        private AlgorandSigner(byte[] seed) => (_seed, Address) = (seed, AlgorandAddress.Encode(AvmCrypto.PublicKey(seed)));

        /// <summary>The account that signs.</summary>
        public string Address { get; }

        /// <summary>The configured mnemonic or private key; exactly one of them.</summary>
        public static AlgorandSigner Create(Web3ServiceOptions options)
        {
            var (hasPrivateKey, hasMnemonic) = (!string.IsNullOrEmpty(options.PrivateKey), !string.IsNullOrEmpty(options.Mnemonic));
            if (hasPrivateKey && hasMnemonic) throw Errors.InvalidArgument("Provide exactly one signer credential.");
            if (hasPrivateKey) return new AlgorandSigner(SeedOf(options.PrivateKey!));
            return hasMnemonic ? new AlgorandSigner(Mnemonics.ToSeed(options.Mnemonic!)) : throw Errors.SignerRequired();
        }

        /// <summary>A 32-byte seed as 0x hex, or a 64-byte secret key (seed followed by public key) as base64.</summary>
        public static byte[] SeedOf(string privateKey)
        {
            if (SeedHex.IsMatch(privateKey)) return privateKey.HexToByteArray();
            var secretKey = TryBase64(privateKey);
            if (secretKey?.Length != SecretKeyBytes) throw Errors.InvalidArgument("An Algorand private key is a 0x-prefixed 32-byte seed or a base64 64-byte secret key.");
            return secretKey.Take(AvmCrypto.SeedBytes).ToArray();
        }

        public List<byte[]> Sign(IEnumerable<UnsignedTransaction> transactions) => transactions.Select(transaction => transaction.Sign(_seed)).ToList();

        private static byte[]? TryBase64(string value)
        {
            try { return Convert.FromBase64String(value); }
            catch (FormatException) { return null; }
        }
    }
}
