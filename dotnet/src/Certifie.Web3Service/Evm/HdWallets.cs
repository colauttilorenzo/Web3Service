using System.Globalization;
using NBitcoin;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.HdWallet;

namespace Certifie.Web3.Evm
{
    /// <summary>BIP-39 phrases and BIP-44 Ethereum wallets.</summary>
    internal static class HdWallets
    {
        public static string CreateMnemonic() => string.Join(" ", new Wallet(Wordlist.English, WordCount.Twelve).Words);

        /// <summary>Derives the wallet at <c>m/44'/60'/0'/0/{index}</c>.</summary>
        public static WalletInfo Derive(string mnemonic, int index, string password)
        {
            if (index < 0) throw Errors.InvalidArgument($"walletIndex must be an integer between 0 and {int.MaxValue}.");
            var key = new Wallet(mnemonic, password).GetEthereumKey(index);
            return new WalletInfo
            {
                Address = key.GetPublicAddress(), PublicKey = key.GetPubKey(true).ToHex(true), PrivateKey = key.GetPrivateKey(),
                Path = $"m/44'/60'/0'/0/{index}", Index = index.ToString(CultureInfo.InvariantCulture),
            };
        }
    }
}
