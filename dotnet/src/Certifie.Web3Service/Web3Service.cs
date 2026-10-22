using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Certifie.Web3.Algorand;
using Certifie.Web3.Evm;

namespace Certifie.Web3
{
    /// <summary>
    /// Deploys and calls contracts, creates tokens, and reads transactions on any supported blockchain.
    /// The provider selects the blockchain: an <see cref="IEvmProvider"/> (or a node URI) for EVM networks, an <see cref="IAvmProvider"/> for Algorand,
    /// an <see cref="IBitcoinProvider"/> for Bitcoin. The API and the JSON output are the same everywhere; an operation the blockchain does not have
    /// throws <see cref="System.NotImplementedException"/>. Native port of the <c>@certifie/web3-service</c> JavaScript class,
    /// with numeric values returned as decimal integer strings. Arithmetic uses <see cref="BigInteger"/>.
    /// </summary>
    public sealed class Web3Service : IWeb3Service
    {
        private readonly IWeb3Service _chain;

        /// <summary>Initializes from an EVM node URI (<c>https://</c>, <c>http://</c>, <c>wss://</c> or <c>ws://</c>) with Ethereum defaults.</summary>
        public Web3Service(string rpcURI, Web3ServiceOptions? options = null)
            : this(new EvmProvider { RpcURI = rpcURI }, options) { }

        /// <summary>Initializes from a provider: an <see cref="EvmProvider"/>, <see cref="AvmProvider"/>, <see cref="BitcoinProvider"/> or any implementation of their interfaces.</summary>
        public Web3Service(IBlockchainProvider provider, Web3ServiceOptions? options = null)
        {
            _chain = Blockchains.Create(provider, options);
        }

        public string Kind => _chain.Kind;

        public ResolvedProvider Provider => _chain.Provider;

        public string ChainId => _chain.ChainId;

        public static string ShrinkAddress(string address) =>
            address.Length > 10 ? $"{address.Substring(0, 6)}...{address.Substring(address.Length - 4)}" : address;

        /// <summary>EVM chain IDs as lowercase hex, e.g. 137 → 0x89.</summary>
        public static string? NormalizeChainId(string? chainId) => chainId == null ? null : Integers.NormalizeChainId(chainId);

        public static string NormalizeChainId(BigInteger chainId) => Integers.ToHex(Integers.RequireNonNegative(chainId, "chainId"));

        /// <summary>A new 12-word English BIP-39 phrase, for EVM wallets. Algorand accounts: see <see cref="AlgorandWallets"/>.</summary>
        public static string CreateMnemonic() => HdWallets.CreateMnemonic();

        /// <summary>Derives the EVM wallet at <c>m/44'/60'/0'/0/{index}</c>; the default index 1 matches the original service.</summary>
        public static WalletInfo GetWallet(string mnemonic, int index = 1, string password = "") => HdWallets.Derive(mnemonic, index, password);

        public static string GetPrivateKey(string mnemonic, int index = 1, string password = "") => GetWallet(mnemonic, index, password).PrivateKey;

        /// <summary>Standards recognized from the functions of an ABI, e.g. <c>["ERC-20"]</c>, <c>["ERC-721", "ERC-721 Metadata", "ERC-165"]</c> or <c>["ARC-200"]</c>.</summary>
        public static List<string> DetectStandards(string abi) => ContractMethods.ParseAbi(abi) is JsonArray
            ? ErcStandards.Detect(AbiMethods.Describe(abi).Select(fn => fn.Method.Signature).ToList())
            : ArcStandards.Detect(Arc4Methods.Describe(abi).Select(fn => fn.Method.Name).ToList());

        /// <summary>A contract at an address (an application ID on Algorand): lists its functions and calls them with named parameters, e.g. <c>contract.CallAsync("transfer", new { to, amount })</c>.</summary>
        public Web3Contract GetContract(string abi, string address) => _chain.GetContract(abi, address);

        public Task<CheckResult> CheckAsync(CancellationToken cancellationToken = default) => _chain.CheckAsync(cancellationToken);

        public Task<JsonNode?> CallContractMethodAsync(string method, string abi, string address, object[]? parameters = null,
            CancellationToken cancellationToken = default) =>
            _chain.CallContractMethodAsync(method, abi, address, parameters, cancellationToken);

        public Task<TransactionResult> DeployAsync(string abi, string bytecode, object[]? constructorArguments = null, TransactionResult? onChainEvent = null,
            TransactionOptions? options = null, CancellationToken cancellationToken = default) =>
            _chain.DeployAsync(abi, bytecode, constructorArguments, onChainEvent, options, cancellationToken);

        public Task<TransactionResult> DeployAsync(string abi, AvmPrograms programs, object[]? constructorArguments = null, TransactionResult? onChainEvent = null,
            TransactionOptions? options = null, CancellationToken cancellationToken = default) =>
            _chain.DeployAsync(abi, programs, constructorArguments, onChainEvent, options, cancellationToken);

        /// <summary>Creates a token without a contract: an Algorand Standard Asset. Not available on EVM networks, where tokens are contracts.</summary>
        public Task<TokenCreation> CreateTokenAsync(TokenDefinition token, TransactionResult? onChainEvent = null, TransactionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            _chain.CreateTokenAsync(token, onChainEvent, options, cancellationToken);

        public Task<TokenInfo> GetTokenInfoAsync(string tokenId, CancellationToken cancellationToken = default) => _chain.GetTokenInfoAsync(tokenId, cancellationToken);

        public Task<TransactionResult> GetTransactionReceiptAsync(string transactionHash, TransactionResult? onChainEvent = null,
            CancellationToken cancellationToken = default) =>
            _chain.GetTransactionReceiptAsync(transactionHash, onChainEvent, cancellationToken);

        public Task<TransactionDetails> GetTransactionAsync(string transactionHash, string? abi = null, CancellationToken cancellationToken = default) =>
            _chain.GetTransactionAsync(transactionHash, abi, cancellationToken);
    }
}
