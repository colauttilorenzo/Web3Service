using Nethereum.Hex.HexConvertors.Extensions;

namespace Certifie.Web3.Algorand
{
    /// <summary>
    /// Algorand accounts: 25-word mnemonics and the keys they encode. Algorand accounts are not derived from a path, so <c>Path</c> is null.
    /// Pass the mnemonic as <see cref="Web3ServiceOptions.Mnemonic"/> to sign with the account.
    /// </summary>
    public static class AlgorandWallets
    {
        /// <summary>A new account's 25-word mnemonic. Store it as securely as a private key.</summary>
        public static string CreateMnemonic() => Mnemonics.FromSeed(AvmCrypto.RandomSeed());

        /// <summary>
        /// The account of a 25-word mnemonic: its address, public key and private key as 0x hex.
        /// The private key is the 32-byte Ed25519 seed; the 64-byte secret key is the seed followed by the public key.
        /// </summary>
        public static WalletInfo GetWallet(string mnemonic)
        {
            var seed = Mnemonics.ToSeed(mnemonic);
            var publicKey = AvmCrypto.PublicKey(seed);
            return new WalletInfo { Address = AlgorandAddress.Encode(publicKey), PublicKey = publicKey.ToHex(true), PrivateKey = seed.ToHex(true), Path = null, Index = "0" };
        }
    }
}
