using System;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Nethereum.RPC.Eth.DTOs;
using RpcReceipt = Nethereum.RPC.Eth.DTOs.TransactionReceipt;

namespace Certifie.Web3
{
    /// <summary>
    /// Deploys contracts, notarizes document hashes with <c>mint</c>, and reads contracts and receipts on EVM networks.
    /// Native port of the <c>@certifie/web3-service</c> JavaScript class: same defaults, error codes and JSON output,
    /// with numeric values returned as decimal integer strings. Arithmetic uses <see cref="BigInteger"/>.
    /// </summary>
    public sealed class Web3Service
    {
        private const string MintSignature = "mint(address,string,string,string)";
        private static readonly Regex BytecodePattern = new Regex("^0x(?:[0-9a-f]{2})+$", RegexOptions.IgnoreCase);
        private readonly Web3ServiceOptions _options;
        private readonly RpcConnector _rpc;
        private readonly WriteQueue _writes = new WriteQueue();

        /// <summary>Initializes from a node URI (<c>https://</c>, <c>http://</c>, <c>wss://</c> or <c>ws://</c>) with Ethereum defaults.</summary>
        public Web3Service(string rpcURI, Web3ServiceOptions? options = null)
            : this(new EvmProvider { RpcURI = rpcURI }, options) { }

        /// <summary>Initializes from a provider configuration; omitted fields default to Ethereum (0x1, Ether, ETH, 18 decimals).</summary>
        public Web3Service(EvmProvider provider, Web3ServiceOptions? options = null)
        {
            Provider = Configuration.ResolveProvider(provider);
            _options = Configuration.ResolveOptions(options);
            _rpc = new RpcConnector(Provider, _options.Timeout);
        }

        public EthereumProvider Provider { get; }

        /// <summary>Normalized lowercase hex chain ID, e.g. <c>0x539</c>.</summary>
        public string ChainId => Provider.ChainId;

        public static string ShrinkAddress(string address) =>
            address.Length > 10 ? $"{address.Substring(0, 6)}...{address.Substring(address.Length - 4)}" : address;

        public static string? NormalizeChainId(string? chainId) => chainId == null ? null : Integers.NormalizeChainId(chainId);

        public static string NormalizeChainId(BigInteger chainId) => Integers.ToHex(Integers.RequireNonNegative(chainId, "chainId"));

        /// <summary>A new 12-word English BIP-39 phrase.</summary>
        public static string CreateMnemonic() => HdWallets.CreateMnemonic();

        /// <summary>Derives the wallet at <c>m/44'/60'/0'/0/{index}</c>; the default index 1 matches the original service.</summary>
        public static WalletInfo GetWallet(string mnemonic, int index = 1, string password = "") => HdWallets.Derive(mnemonic, index, password);

        public static string GetPrivateKey(string mnemonic, int index = 1, string password = "") => GetWallet(mnemonic, index, password).PrivateKey;

        public Task<CheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
            _rpc.UseAsync(async session =>
            {
                var blockNumber = await session.QuantityAsync(session.Eth.Blocks.GetBlockNumber.SendRequestAsync()).ConfigureAwait(false);
                var gasPrice = await session.QuantityAsync(session.Eth.GasPrice.SendRequestAsync()).ConfigureAwait(false);
                return new CheckResult { BlockNumber = Integers.ToDecimalString(blockNumber), GasPrice = Integers.ToDecimalString(gasPrice) };
            }, cancellationToken);

        /// <summary>
        /// Calls a read-only method. A single output is returned as its value, several outputs as a JSON array.
        /// Integers become decimal strings, bytes become 0x hex.
        /// </summary>
        public Task<JsonNode?> CallContractMethodAsync(string method, string abi, string address, object[]? parameters = null,
            CancellationToken cancellationToken = default) =>
            _rpc.UseAsync(async session =>
            {
                var function = ContractCalls.GetFunction(session.Eth, abi, address, method);
                var outputs = await session.WithTimeoutAsync(function.CallDecodingToDefaultAsync(parameters ?? Array.Empty<object>())).ConfigureAwait(false);
                return ContractCalls.OutputsToJson(outputs);
            }, cancellationToken);

        /// <summary>Notarizes a document hash by calling <c>mint(address,string,string,string)</c> on the contract.</summary>
        public Task<TransactionResult> MintAsync(string abi, string contractAddress, NotarizationDocument document, TransactionResult? onChainEvent = null,
            TransactionOptions? options = null, CancellationToken cancellationToken = default)
        {
            ValidateDocument(document);
            var arguments = new object[] { contractAddress, document.Hash.Data, document.Hash.Algorithm, document.Metadata ?? "" };
            return WriteAsync(Named(onChainEvent, "mint"), session =>
            {
                var function = ContractCalls.GetFunction(session.Eth, abi, contractAddress, "mint", $"The ABI must contain {MintSignature}.");
                return new TransactionRequest(function.GetData(arguments), contractAddress,
                    (from, value) => function.EstimateGasAsync(from, null, value, arguments), options);
            }, cancellationToken);
        }

        public Task<TransactionResult> DeployAsync(string abi, string bytecode, object[]? constructorArguments = null, TransactionResult? onChainEvent = null,
            TransactionOptions? options = null, CancellationToken cancellationToken = default)
        {
            ValidateBytecode(bytecode);
            var arguments = constructorArguments ?? Array.Empty<object>();
            return WriteAsync(Named(onChainEvent, "deploy"), session =>
            {
                var data = session.Eth.DeployContract.GetData(bytecode, abi, arguments);
                return new TransactionRequest(data, null,
                    (from, value) => session.Eth.TransactionManager.EstimateGasAsync(new CallInput { From = from, Data = data, Value = value }), options);
            }, cancellationToken);
        }

        public Task<TransactionResult> GetTransactionReceiptAsync(string transactionHash, TransactionResult? onChainEvent = null,
            CancellationToken cancellationToken = default) =>
            _rpc.UseAsync(async session =>
            {
                var receipt = await Receipts.GetAsync(session, transactionHash).ConfigureAwait(false) ?? throw Errors.TransactionNotFound(transactionHash);
                var result = onChainEvent?.Copy() ?? new TransactionResult();
                return await TransactionResults.FromReceiptAsync(session, receipt, result, Provider).ConfigureAwait(false);
            }, cancellationToken);

        private static TransactionResult Named(TransactionResult? onChainEvent, string name)
        {
            var copy = onChainEvent?.Copy() ?? new TransactionResult();
            copy.Name ??= name;
            return copy;
        }

        private static void ValidateDocument(NotarizationDocument? document)
        {
            if (string.IsNullOrEmpty(document?.Hash?.Data) || string.IsNullOrEmpty(document!.Hash.Algorithm))
                throw Errors.InvalidArgument("A document hash and algorithm are required.");
        }

        private static void ValidateBytecode(string? bytecode)
        {
            if (bytecode == null || !BytecodePattern.IsMatch(bytecode)) throw Errors.InvalidArgument("bytecode must be a non-empty 0x-prefixed hex string.");
        }

        /// <summary>Writes are queued per instance, so concurrent calls get consecutive nonces.</summary>
        private Task<TransactionResult> WriteAsync(TransactionResult onChainEvent, Func<RpcSession, TransactionRequest> prepare,
            CancellationToken cancellationToken) =>
            _writes.RunAsync(() => _rpc.UseAsync(session => SendAsync(session, prepare(session), onChainEvent), cancellationToken), cancellationToken);

        /// <summary>Signs and broadcasts the request, waits until it is mined and describes the result.</summary>
        private async Task<TransactionResult> SendAsync(RpcSession session, TransactionRequest request, TransactionResult onChainEvent)
        {
            onChainEvent.RequestedAt = TransactionResults.RequestedAt(onChainEvent.RequestedAt);
            var transactionHash = await KeySigner.Create(_options, ChainId).SendAsync(session, request).ConfigureAwait(false);
            var receipt = await Receipts.WaitAsync(session, transactionHash, _options.TransactionTimeout, _options.PollingInterval).ConfigureAwait(false);
            if (receipt.Status?.Value == 0) throw Errors.TransactionReverted(transactionHash);
            return await DescribeMinedAsync(session, receipt, onChainEvent).ConfigureAwait(false);
        }

        /// <summary>The transaction is already mined, so a failure here must still hand back its hash.</summary>
        private async Task<TransactionResult> DescribeMinedAsync(RpcSession session, RpcReceipt receipt, TransactionResult onChainEvent)
        {
            try { return await TransactionResults.FromReceiptAsync(session, receipt, onChainEvent, Provider).ConfigureAwait(false); }
            catch (Exception error) when (!session.IsCancellation(error)) { throw Errors.ReceiptDetailsFailed(receipt.TransactionHash, error); }
        }
    }
}
