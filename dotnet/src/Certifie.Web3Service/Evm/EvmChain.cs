using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Nethereum.RPC.Eth.DTOs;
using RpcReceipt = Nethereum.RPC.Eth.DTOs.TransactionReceipt;

namespace Certifie.Web3.Evm
{
    /// <summary>
    /// Deploys contracts, notarizes document hashes with <c>mint</c>, and reads contracts and receipts on EVM networks.
    /// Mirrors EvmChain in the JavaScript package: same defaults, error codes and JSON output.
    /// </summary>
    internal sealed class EvmChain : IWeb3Service, IContractRunner
    {
        private const string MintSignature = "mint(address,string,string,string)";
        private const string TokenHint = "EVM tokens are contracts: deploy one with DeployAsync and use GetContract(...).MintNftAsync and GetTokenInfoAsync.";
        private static readonly Regex BytecodePattern = new Regex("^0x(?:[0-9a-f]{2})+$", RegexOptions.IgnoreCase);
        private readonly EthereumProvider _provider;
        private readonly Web3ServiceOptions _options;
        private readonly Connector<RpcSession> _rpc;
        private readonly WriteQueue _writes = new WriteQueue();

        /// <summary>Omitted provider fields default to Ethereum (0x1, Ether, ETH, 18 decimals).</summary>
        public EvmChain(IEvmProvider provider, Web3ServiceOptions? options)
        {
            _provider = Configuration.ResolveProvider(provider);
            _options = Configuration.ResolveOptions(options);
            _rpc = RpcConnector.Create(_provider, _options.Timeout);
        }

        public string Kind => "evm";

        public ResolvedProvider Provider => _provider;

        /// <summary>Normalized lowercase hex chain ID, e.g. <c>0x539</c>.</summary>
        public string ChainId => _provider.ChainId;

        public Web3Contract GetContract(string abi, string address) => new EvmContract(abi, address, this);

        public Task<CheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
            _rpc.UseAsync(async session =>
            {
                var blockNumber = await session.QuantityAsync(session.Eth.Blocks.GetBlockNumber.SendRequestAsync()).ConfigureAwait(false);
                var gasPrice = await session.QuantityAsync(session.Eth.GasPrice.SendRequestAsync()).ConfigureAwait(false);
                return new CheckResult { BlockNumber = Integers.ToDecimalString(blockNumber), GasPrice = Integers.ToDecimalString(gasPrice) };
            }, cancellationToken);

        /// <summary>Integers become decimal strings, bytes become 0x hex.</summary>
        public Task<JsonNode?> CallContractMethodAsync(string method, string abi, string address, object[]? parameters = null,
            CancellationToken cancellationToken = default) =>
            _rpc.UseAsync(async session =>
            {
                var function = ContractCalls.GetFunction(session.Eth, AbiMethods.RequireEvmAbi(abi), address, method);
                var outputs = await session.WithTimeoutAsync(function.CallDecodingToDefaultAsync(parameters ?? Array.Empty<object>())).ConfigureAwait(false);
                return ContractCalls.OutputsToJson(outputs);
            }, cancellationToken);

        public Task<TransactionResult> MintAsync(string abi, string contractAddress, NotarizationDocument document, TransactionResult? onChainEvent = null,
            TransactionOptions? options = null, CancellationToken cancellationToken = default)
        {
            object[] arguments = Notarization.MintArguments(contractAddress, document);
            AbiMethods.RequireEvmAbi(abi);
            return WriteAsync(Notarization.Named(onChainEvent, "mint"), session =>
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
            AbiMethods.RequireEvmAbi(abi);
            var arguments = constructorArguments ?? Array.Empty<object>();
            return WriteAsync(Notarization.Named(onChainEvent, "deploy"), session =>
            {
                var data = session.Eth.DeployContract.GetData(bytecode, abi, arguments);
                return new TransactionRequest(data, null,
                    (from, value) => session.Eth.TransactionManager.EstimateGasAsync(new CallInput { From = from, Data = data, Value = value }), options);
            }, cancellationToken);
        }

        /// <summary>EVM contracts are deployed from bytecode, not from Algorand programs.</summary>
        public Task<TransactionResult> DeployAsync(string abi, AvmPrograms programs, object[]? constructorArguments = null, TransactionResult? onChainEvent = null,
            TransactionOptions? options = null, CancellationToken cancellationToken = default) =>
            throw Errors.InvalidArgument("bytecode must be a non-empty 0x-prefixed hex string.");

        public Task<TokenCreation> CreateTokenAsync(TokenDefinition token, TransactionResult? onChainEvent = null, TransactionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw Errors.NotImplemented("CreateTokenAsync", "EVM networks", TokenHint);

        public Task<TokenInfo> GetTokenInfoAsync(string tokenId, CancellationToken cancellationToken = default) =>
            throw Errors.NotImplemented("GetTokenInfoAsync", "EVM networks", TokenHint);

        public Task<TransactionResult> GetTransactionReceiptAsync(string transactionHash, TransactionResult? onChainEvent = null,
            CancellationToken cancellationToken = default) =>
            _rpc.UseAsync(async session =>
            {
                var receipt = await Receipts.GetAsync(session, transactionHash).ConfigureAwait(false) ?? throw Errors.TransactionNotFound(transactionHash);
                var result = onChainEvent?.Copy() ?? new TransactionResult();
                return await TransactionResults.FromReceiptAsync(session, receipt, result, _provider).ConfigureAwait(false);
            }, cancellationToken);

        public Task<TransactionDetails> GetTransactionAsync(string transactionHash, string? abi = null, CancellationToken cancellationToken = default) =>
            _rpc.UseAsync(async session =>
            {
                var functions = abi == null ? new List<AbiFunction>() : AbiMethods.Describe(abi);
                var details = Transactions.FromRpc(await Transactions.GetAsync(session, transactionHash).ConfigureAwait(false), functions);
                return await TransactionResults.AddMinedDetailsAsync(session, details, _provider).ConfigureAwait(false);
            }, cancellationToken);

        Task<JsonNode?> IContractRunner.ReadAsync(string abi, string address, string signature, object[] args, CancellationToken cancellationToken) =>
            CallContractMethodAsync(signature, abi, address, args, cancellationToken);

        Task<TransactionResult> IContractRunner.SendAsync(string abi, string address, string signature, object[] args, string name, TransactionOptions? options,
            CancellationToken cancellationToken) =>
            TransactAsync(abi, address, signature, args, new TransactionResult { Name = name }, options, cancellationToken);

        Task<TransactionDetails> IContractRunner.GetTransactionAsync(string transactionHash, string abi, CancellationToken cancellationToken) =>
            GetTransactionAsync(transactionHash, abi, cancellationToken);

        /// <summary>Sends a call to any state-changing contract function.</summary>
        private Task<TransactionResult> TransactAsync(string abi, string address, string signature, object[] args, TransactionResult onChainEvent,
            TransactionOptions? options, CancellationToken cancellationToken) =>
            WriteAsync(onChainEvent, session =>
            {
                var function = ContractCalls.GetFunction(session.Eth, abi, address, signature);
                return new TransactionRequest(function.GetData(args), address, (from, value) => function.EstimateGasAsync(from, null, value, args), options);
            }, cancellationToken);

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
            onChainEvent.RequestedAt = SharedResults.RequestedAt(onChainEvent.RequestedAt);
            var transactionHash = await KeySigner.Create(_options, ChainId).SendAsync(session, request).ConfigureAwait(false);
            var receipt = await Receipts.WaitAsync(session, transactionHash, _options.TransactionTimeout, _options.PollingInterval).ConfigureAwait(false);
            if (receipt.Status?.Value == 0) throw Errors.TransactionReverted(transactionHash);
            return await DescribeMinedAsync(session, receipt, onChainEvent).ConfigureAwait(false);
        }

        /// <summary>The transaction is already mined, so a failure here must still hand back its hash.</summary>
        private async Task<TransactionResult> DescribeMinedAsync(RpcSession session, RpcReceipt receipt, TransactionResult onChainEvent)
        {
            try { return await TransactionResults.FromReceiptAsync(session, receipt, onChainEvent, _provider).ConfigureAwait(false); }
            catch (Exception error) when (!session.IsCancellation(error)) { throw Errors.ReceiptDetailsFailed(receipt.TransactionHash, error); }
        }
    }
}
