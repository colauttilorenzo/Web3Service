using System;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Certifie.Web3.Bitcoin
{
    /// <summary>
    /// Bitcoin through an Esplora REST API: reads blocks, fees and transactions. Bitcoin has no smart contracts or tokens,
    /// so contract, deploy, mint and token operations throw <see cref="NotImplementedException"/>.
    /// </summary>
    internal sealed class BitcoinChain : IWeb3Service
    {
        private const string NoContracts = "Bitcoin has no smart contracts.";
        private const string NoTokens = "Bitcoin has no native tokens.";
        private const string NextBlock = "1";
        private const double MinRelayFee = 1;
        private static readonly NetworkDefaults Defaults = new NetworkDefaults("mainnet", "Bitcoin", ("Bitcoin", "BTC", 8));
        private readonly BitcoinNetwork _network;
        private readonly Connector<EsploraSession> _esplora;

        public BitcoinChain(IBitcoinProvider provider, Web3ServiceOptions? options)
        {
            var (chainName, currency) = Configuration.ResolveNetwork(provider, Defaults);
            _network = new BitcoinNetwork(provider, Configuration.RequireChainName(provider.ChainId, Defaults.ChainId), chainName, currency);
            _esplora = EsploraConnector.Create(_network, Configuration.ResolveOptions(options).Timeout);
        }

        public string Kind => "bitcoin";

        public ResolvedProvider Provider => _network;

        /// <summary>The network name, e.g. <c>mainnet</c> or <c>testnet4</c>.</summary>
        public string ChainId => _network.ChainId;

        /// <summary>The block height and the fee rate for the next block, in satoshis per virtual byte.</summary>
        public Task<CheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
            _esplora.UseAsync(async session =>
            {
                var estimates = await session.JsonAsync("/fee-estimates").ConfigureAwait(false);
                var feeRate = Math.Ceiling(estimates?[NextBlock]?.GetValue<double>() ?? MinRelayFee);
                var height = await session.TextAsync("/blocks/tip/height").ConfigureAwait(false);
                return new CheckResult { BlockNumber = height, GasPrice = feeRate.ToString(CultureInfo.InvariantCulture) };
            }, cancellationToken);

        public Web3Contract GetContract(string abi, string address) => throw Errors.NotImplemented("GetContract", "Bitcoin", NoContracts);

        public Task<JsonNode?> CallContractMethodAsync(string method, string abi, string address, object[]? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw Errors.NotImplemented("CallContractMethodAsync", "Bitcoin", NoContracts);

        public Task<TransactionResult> MintAsync(string abi, string contractAddress, NotarizationDocument document, TransactionResult? onChainEvent = null,
            TransactionOptions? options = null, CancellationToken cancellationToken = default) =>
            throw Errors.NotImplemented("MintAsync", "Bitcoin", NoContracts);

        public Task<TransactionResult> DeployAsync(string abi, string bytecode, object[]? constructorArguments = null, TransactionResult? onChainEvent = null,
            TransactionOptions? options = null, CancellationToken cancellationToken = default) =>
            throw Errors.NotImplemented("DeployAsync", "Bitcoin", NoContracts);

        public Task<TransactionResult> DeployAsync(string abi, AvmPrograms programs, object[]? constructorArguments = null, TransactionResult? onChainEvent = null,
            TransactionOptions? options = null, CancellationToken cancellationToken = default) =>
            throw Errors.NotImplemented("DeployAsync", "Bitcoin", NoContracts);

        public Task<TokenCreation> CreateTokenAsync(TokenDefinition token, TransactionResult? onChainEvent = null, TransactionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw Errors.NotImplemented("CreateTokenAsync", "Bitcoin", NoTokens);

        public Task<TokenInfo> GetTokenInfoAsync(string tokenId, CancellationToken cancellationToken = default) =>
            throw Errors.NotImplemented("GetTokenInfoAsync", "Bitcoin", NoTokens);

        public Task<TransactionResult> GetTransactionReceiptAsync(string transactionHash, TransactionResult? onChainEvent = null,
            CancellationToken cancellationToken = default) =>
            _esplora.UseAsync(session => Transactions.ReceiptAsync(session, transactionHash, onChainEvent?.Copy() ?? new TransactionResult(), _network), cancellationToken);

        /// <summary>A transaction by ID; the ABI does not apply to Bitcoin.</summary>
        public Task<TransactionDetails> GetTransactionAsync(string transactionHash, string? abi = null, CancellationToken cancellationToken = default) =>
            _esplora.UseAsync(session => Transactions.DetailsAsync(session, transactionHash, _network), cancellationToken);
    }
}
