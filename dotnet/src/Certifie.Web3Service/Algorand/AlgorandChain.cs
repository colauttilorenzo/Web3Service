using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Certifie.Web3.Algorand
{
    /// <summary>
    /// Algorand through algod: deploys and calls ARC-4 applications, creates Algorand Standard Assets and reads transactions.
    /// Every write is simulated first, so the resources the application reads and the fees of its inner transactions are filled in,
    /// and a call the application would reject is never sent. Mirrors AlgorandChain in the JavaScript package with the same JSON output.
    /// </summary>
    internal sealed class AlgorandChain : IWeb3Service, IAppRunner
    {
        private static readonly NetworkDefaults Defaults = new NetworkDefaults("mainnet-v1.0", "Algorand", ("Algo", "ALGO", 6));
        private readonly AlgorandNetwork _network;
        private readonly Web3ServiceOptions _options;
        private readonly Connector<AlgodSession> _algod;
        private readonly WriteQueue _writes = new WriteQueue();

        public AlgorandChain(IAvmProvider provider, Web3ServiceOptions? options)
        {
            var (chainName, currency) = Configuration.ResolveNetwork(provider, Defaults);
            _network = new AlgorandNetwork(provider, Configuration.RequireChainName(provider.ChainId, Defaults.ChainId), chainName, currency);
            _options = Configuration.ResolveOptions(options);
            _algod = AlgodConnector.Create(_network, _options.Timeout);
        }

        public string Kind => "avm";

        public ResolvedProvider Provider => _network;

        /// <summary>The genesis ID of the network, e.g. <c>mainnet-v1.0</c>.</summary>
        public string ChainId => _network.ChainId;

        /// <summary>An application by its ID: lists its ARC-4 methods and calls them with named parameters.</summary>
        public Web3Contract GetContract(string abi, string address) => new AlgorandContract(abi, address, this);

        /// <summary>The last round and the minimum fee per transaction in microAlgos.</summary>
        public Task<CheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
            _algod.UseAsync(session => Task.FromResult(new CheckResult
            {
                BlockNumber = Integers.ToDecimalString(session.Params.FirstValid), GasPrice = Integers.ToDecimalString(session.Params.MinFee),
            }), cancellationToken);

        public Task<JsonNode?> CallContractMethodAsync(string method, string abi, string address, object[]? parameters = null,
            CancellationToken cancellationToken = default)
        {
            var fn = ContractMethods.Find(Arc4Methods.Describe(abi), method, new JsonObject());
            var args = Arc4Methods.ToArguments(fn, ContractMethods.FromPositional(fn, parameters ?? Array.Empty<object>()));
            return ReadAsync(AlgorandContract.ApplicationId(address), fn, args, cancellationToken);
        }

        /// <summary>On Algorand the bytecode must be empty: the programs come from the ARC-56 description. Pass AvmPrograms to deploy other programs.</summary>
        public Task<TransactionResult> DeployAsync(string abi, string bytecode, object[]? constructorArguments = null, TransactionResult? onChainEvent = null,
            TransactionOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(bytecode)) throw Errors.InvalidArgument("On Algorand pass the programs as AvmPrograms, or an empty bytecode to take them from the ARC-56 description.");
            return DeployProgramsAsync(abi, null, constructorArguments, onChainEvent, options, cancellationToken);
        }

        /// <summary>Creates the application; options.Value then funds its account, e.g. for the boxes it stores.</summary>
        public Task<TransactionResult> DeployAsync(string abi, AvmPrograms programs, object[]? constructorArguments = null, TransactionResult? onChainEvent = null,
            TransactionOptions? options = null, CancellationToken cancellationToken = default) =>
            DeployProgramsAsync(abi, programs ?? throw Errors.InvalidArgument("programs is required."), constructorArguments, onChainEvent, options, cancellationToken);

        /// <summary>Creates an Algorand Standard Asset; an NFT has TotalSupply 1 and Decimals 0.</summary>
        public async Task<TokenCreation> CreateTokenAsync(TokenDefinition token, TransactionResult? onChainEvent = null, TransactionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var request = Assets.Creation(token, options);
            var sent = await WriteAsync(ChainEvents.Named(onChainEvent, "createToken"), _ => Task.FromResult(request), cancellationToken).ConfigureAwait(false);
            return new TokenCreation { Standard = "ASA", TokenId = sent.Transaction.CreatedAssetId?.ToString() ?? "", Transaction = sent.Result };
        }

        public Task<TokenInfo> GetTokenInfoAsync(string tokenId, CancellationToken cancellationToken = default) =>
            _algod.UseAsync(session => Assets.InfoAsync(session, tokenId), cancellationToken);

        public Task<TransactionResult> GetTransactionReceiptAsync(string transactionHash, TransactionResult? onChainEvent = null,
            CancellationToken cancellationToken = default) =>
            _algod.UseAsync(async session =>
            {
                var transaction = await Transactions.GetAsync(session, transactionHash).ConfigureAwait(false);
                if (transaction.ConfirmedRound == null) throw Errors.TransactionNotFound(transactionHash);
                return await Results.FromConfirmedAsync(session, transaction, onChainEvent?.Copy() ?? new TransactionResult(), _network).ConfigureAwait(false);
            }, cancellationToken);

        public Task<TransactionDetails> GetTransactionAsync(string transactionHash, string? abi = null, CancellationToken cancellationToken = default)
        {
            var functions = abi == null ? new List<Arc4Function>() : Arc4Methods.Describe(abi);
            return _algod.UseAsync(async session =>
                await Results.DetailsAsync(session, await Transactions.GetAsync(session, transactionHash).ConfigureAwait(false), functions, _network).ConfigureAwait(false),
                cancellationToken);
        }

        /// <summary>
        /// Reads are simulated without a fee, from the signer's account when there is one, so they are free and need no funded account.
        /// Inner transactions of a read are paid by the application account.
        /// </summary>
        public Task<JsonNode?> ReadAsync(BigInteger appId, Arc4Function fn, List<CallArgument> args, CancellationToken cancellationToken) =>
            _algod.UseAsync(async session =>
            {
                var request = AppCalls.Request(new AppCall(appId, fn, args));
                var group = request.Build(ReadSender(appId), session.Params, new BuildPlan(0));
                var simulated = await Simulation.RunAsync(session, group).ConfigureAwait(false);
                if (simulated.TransactionFailed) throw Simulation.Rejected(group.Last().Id, request.Explain(simulated.Failure!));
                return Arc4Methods.ReturnValue(fn, JsonValues.Items(simulated.Result["logs"]).Select(JsonValues.Base64).ToList());
            }, cancellationToken);

        Task<Sent> IAppRunner.SendAsync(BigInteger appId, Arc4Function fn, List<CallArgument> args, string name, TransactionOptions? options,
            CancellationToken cancellationToken)
        {
            var request = AppCalls.Request(new AppCall(appId, fn, args), options);
            return WriteAsync(new TransactionResult { Name = name }, _ => Task.FromResult(request), cancellationToken);
        }

        Task<TransactionDetails> IAppRunner.GetTransactionAsync(string transactionHash, string abi, CancellationToken cancellationToken) =>
            GetTransactionAsync(transactionHash, abi, cancellationToken);

        private async Task<TransactionResult> DeployProgramsAsync(string abi, AvmPrograms? programs, object[]? constructorArguments, TransactionResult? onChainEvent,
            TransactionOptions? options, CancellationToken cancellationToken)
        {
            var callOptions = new TransactionOptions { GasPrice = options?.GasPrice };
            var deployed = await WriteAsync(ChainEvents.Named(onChainEvent, "deploy"), async session => AppCalls.Request(
                await AppPrograms.CreationCallAsync(session, abi, programs, constructorArguments ?? Array.Empty<object>()).ConfigureAwait(false), callOptions),
                cancellationToken).ConfigureAwait(false);
            if (options?.Value > 0) await FundAsync(deployed.Transaction.CreatedAppId!.Value, options.Value.Value, cancellationToken).ConfigureAwait(false);
            return deployed.Result;
        }

        private string ReadSender(BigInteger appId)
        {
            try { return AlgorandSigner.Create(_options).Address; }
            catch (Web3Exception) { return AlgorandAddress.OfApplication(appId); }
        }

        private Task<Sent> FundAsync(BigInteger appId, BigInteger value, CancellationToken cancellationToken) =>
            WriteAsync(new TransactionResult { Name = "fund" }, _ => Task.FromResult(AppCalls.Fund(appId, value)), cancellationToken);

        /// <summary>Writes are queued per instance, so the groups of concurrent calls never share validity windows and signer state.</summary>
        private Task<Sent> WriteAsync(TransactionResult onChainEvent, Func<AlgodSession, Task<AvmRequest>> prepare, CancellationToken cancellationToken) =>
            _writes.RunAsync(() => _algod.UseAsync(async session =>
                await SendAsync(session, await prepare(session).ConfigureAwait(false), onChainEvent).ConfigureAwait(false), cancellationToken), cancellationToken);

        /// <summary>Simulates, signs and sends the group, waits until it is confirmed and describes the result.</summary>
        private async Task<Sent> SendAsync(AlgodSession session, AvmRequest request, TransactionResult onChainEvent)
        {
            onChainEvent.RequestedAt = SharedResults.RequestedAt(onChainEvent.RequestedAt);
            var signer = AlgorandSigner.Create(_options);
            var group = await Simulation.PlanAsync(session, request, signer.Address).ConfigureAwait(false);
            var transactionId = await Submission.SendAsync(session, group, signer).ConfigureAwait(false);
            var confirmed = await Submission.WaitAsync(session, transactionId, _options.TransactionTimeout, _options.PollingInterval).ConfigureAwait(false);
            return await DescribeConfirmedAsync(session, Transactions.FromPending(transactionId, confirmed), onChainEvent).ConfigureAwait(false);
        }

        /// <summary>The transaction is already confirmed, so a failure here must still hand back its ID.</summary>
        private async Task<Sent> DescribeConfirmedAsync(AlgodSession session, AvmTransaction transaction, TransactionResult onChainEvent)
        {
            try { return new Sent(await Results.FromConfirmedAsync(session, transaction, onChainEvent, _network).ConfigureAwait(false), transaction); }
            catch (Exception error) when (!session.IsCancellation(error)) { throw Errors.ReceiptDetailsFailed(transaction.Id, error); }
        }
    }
}
