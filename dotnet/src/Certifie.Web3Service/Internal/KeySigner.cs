using System;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;
using Nethereum.Web3.Accounts;

namespace Certifie.Web3
{
    /// <summary>Signs legacy (type 0) transactions locally with a private key and broadcasts them through the RPC node.</summary>
    internal sealed class KeySigner
    {
        private readonly Account _account;
        private readonly BigInteger _gasBuffer;

        private KeySigner(Account account, BigInteger gasBuffer)
        {
            _account = account;
            _gasBuffer = gasBuffer;
        }

        public static KeySigner Create(Web3ServiceOptions options, string chainId) =>
            new KeySigner(new Account(PrivateKey(options), Integers.Parse(chainId, "chainId")), options.GasBuffer);

        /// <summary>Signs and broadcasts, returning the hash once the node has accepted (or already mined) the transaction.</summary>
        public async Task<string> SendAsync(RpcSession session, TransactionRequest request)
        {
            var rawTransaction = await SignAsync(session, request).ConfigureAwait(false);
            var transactionHash = "0x" + Sha3Keccack.Current.CalculateHashFromHex(rawTransaction);
            await BroadcastAsync(session, rawTransaction, transactionHash).ConfigureAwait(false);
            return transactionHash;
        }

        /// <summary>The configured private key, or the one derived from the mnemonic at the configured wallet index.</summary>
        private static string PrivateKey(Web3ServiceOptions options)
        {
            var hasPrivateKey = !string.IsNullOrEmpty(options.PrivateKey);
            var hasMnemonic = !string.IsNullOrEmpty(options.Mnemonic);
            if (hasPrivateKey && hasMnemonic) throw Errors.InvalidArgument("Provide exactly one signer credential.");
            if (hasPrivateKey) return options.PrivateKey!;
            if (hasMnemonic) return HdWallets.Derive(options.Mnemonic!, options.WalletIndex, options.MnemonicPassword).PrivateKey;
            throw Errors.SignerRequired();
        }

        /// <summary>
        /// After a transport error, a mined receipt proves the broadcast went through:
        /// a second transaction is never signed or sent.
        /// </summary>
        private static async Task BroadcastAsync(RpcSession session, string rawTransaction, string transactionHash)
        {
            try { await session.WithTimeoutAsync(session.Eth.Transactions.SendRawTransaction.SendRequestAsync(rawTransaction.EnsureHexPrefix())).ConfigureAwait(false); }
            catch (Exception error) when (!session.IsCancellation(error))
            {
                if (await Receipts.TryGetAsync(session, transactionHash).ConfigureAwait(false) == null)
                    throw Errors.SubmissionFailed(transactionHash, error);
            }
        }

        private async Task<string> SignAsync(RpcSession session, TransactionRequest request)
        {
            var transaction = await LegacyTransactionAsync(session, request).ConfigureAwait(false);
            var manager = new AccountSignerTransactionManager(session.Client, _account, _account.ChainId) { UseLegacyAsDefault = true };
            return await manager.SignTransactionAsync(transaction).ConfigureAwait(false);
        }

        private async Task<TransactionInput> LegacyTransactionAsync(RpcSession session, TransactionRequest request)
        {
            var value = Integers.RequireNonNegative(request.Options.Value ?? 0, "value");
            return new TransactionInput
            {
                From = _account.Address, To = request.To, Data = request.Data, Type = new HexBigInteger(0), Value = new HexBigInteger(value),
                Gas = new HexBigInteger(await request.GasLimitAsync(session, _account.Address, value, _gasBuffer).ConfigureAwait(false)),
                GasPrice = new HexBigInteger(await GasPriceAsync(session, request.Options).ConfigureAwait(false)),
                Nonce = new HexBigInteger(await NonceAsync(session, request.Options).ConfigureAwait(false)),
            };
        }

        private static async Task<BigInteger> GasPriceAsync(RpcSession session, TransactionOptions options)
        {
            if (options.GasPrice.HasValue) return Integers.RequireNonNegative(options.GasPrice.Value, "gasPrice");
            return await session.QuantityAsync(session.Eth.GasPrice.SendRequestAsync()).ConfigureAwait(false);
        }

        private async Task<BigInteger> NonceAsync(RpcSession session, TransactionOptions options)
        {
            if (options.Nonce.HasValue) return Integers.RequireNonNegative(options.Nonce.Value, "nonce");
            var pendingCount = session.Eth.Transactions.GetTransactionCount.SendRequestAsync(_account.Address, BlockParameter.CreatePending());
            return await session.QuantityAsync(pendingCount).ConfigureAwait(false);
        }
    }
}
