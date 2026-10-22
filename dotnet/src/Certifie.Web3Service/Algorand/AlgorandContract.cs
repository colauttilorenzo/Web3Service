using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Certifie.Web3.Algorand
{
    /// <summary>A confirmed write: its JSON result and the transaction it came from.</summary>
    internal sealed class Sent
    {
        public Sent(TransactionResult result, AvmTransaction transaction) => (Result, Transaction) = (result, transaction);

        public TransactionResult Result { get; }
        public AvmTransaction Transaction { get; }
    }

    /// <summary>What an <see cref="AlgorandContract"/> needs from the <see cref="AlgorandChain"/> that created it.</summary>
    internal interface IAppRunner
    {
        Task<JsonNode?> ReadAsync(BigInteger appId, Arc4Function fn, List<CallArgument> args, CancellationToken cancellationToken);

        Task<Sent> SendAsync(BigInteger appId, Arc4Function fn, List<CallArgument> args, string name, TransactionOptions? options, CancellationToken cancellationToken);

        Task<TransactionDetails> GetTransactionAsync(string transactionHash, string abi, CancellationToken cancellationToken);
    }

    /// <summary>An Algorand application, described by its ARC-4 (or ARC-32, ARC-56) contract; its address is the application ID.</summary>
    internal sealed class AlgorandContract : Web3Contract
    {
        private static readonly Regex ByteArray = new Regex(@"^byte\[\d*\]$");
        private static readonly Regex ApplicationIdPattern = new Regex("^[0-9]+$");
        private readonly IReadOnlyList<Arc4Function> _functions;
        private readonly BigInteger _appId;
        private readonly IAppRunner _runner;

        public AlgorandContract(string abi, string address, IAppRunner runner) : base(abi, address)
        {
            (_appId, _runner, _functions) = (ApplicationId(address), runner, Arc4Methods.Describe(abi));
            Methods = _functions.Select(fn => fn.Method).ToList();
            Standards = ArcStandards.Detect(Methods.Select(method => method.Name).ToList());
        }

        public override IReadOnlyList<ContractMethod> Methods { get; }

        /// <summary>ARC standards recognized from the method names, e.g. <c>["ARC-200"]</c> or <c>["ARC-72", "ARC-73"]</c>.</summary>
        public override IReadOnlyList<string> Standards { get; }

        /// <summary>The application ID, which Algorand uses as the contract address.</summary>
        public static BigInteger ApplicationId(string address)
        {
            var id = address != null && ApplicationIdPattern.IsMatch(address) ? BigInteger.Parse(address) : BigInteger.Zero;
            return id > 0 ? id : throw Errors.InvalidArgument("The contract address of an Algorand application is its application ID, a positive integer.");
        }

        /// <summary>The value of the contract's owner() method; null when it has none.</summary>
        public override async Task<string?> OwnerAsync(CancellationToken cancellationToken = default)
        {
            var owner = _functions.FirstOrDefault(fn => fn.Method.Name == "owner" && fn.Method.Parameters.Count == 0);
            return owner == null ? null : TextOf(await ReadAsync(owner, new JsonObject(), cancellationToken).ConfigureAwait(false));
        }

        /// <summary>Name, symbol, decimals and supply of an ARC-200 token or ARC-72 collection; with the ID of an NFT, also its owner and URI.</summary>
        public override async Task<TokenInfo> GetTokenInfoAsync(string? tokenId = null, CancellationToken cancellationToken = default)
        {
            var standard = ArcStandards.TokenStandard(Standards) ?? throw Errors.NotSupported("The contract is not an ARC-200 or ARC-72 token.");
            var id = tokenId == null || standard == "ARC-200" ? null : Integers.ToDecimalString(Integers.Parse(tokenId, "tokenId"));
            var info = new TokenInfo { Standard = standard, Address = Address, TokenId = id };
            foreach (var (field, name) in Tokens.TokenReads(Methods.Select(method => method.Name).ToList(), id != null))
                TokenMethods.Assign(info, field, await ReadTokenAsync(name, id, cancellationToken).ConfigureAwait(false));
            return info;
        }

        public override Task<TransactionDetails> GetTransactionAsync(string transactionHash, CancellationToken cancellationToken = default) =>
            _runner.GetTransactionAsync(transactionHash, Abi, cancellationToken);

        private protected override async Task<JsonNode?> CallCoreAsync(string method, JsonObject parameters, TransactionOptions? options,
            CancellationToken cancellationToken)
        {
            var fn = ContractMethods.Find(_functions, method, parameters);
            if (fn.Method.Kind == "read") return await ReadAsync(fn, parameters, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.SerializeToNode((await SendAsync(fn, parameters, options, cancellationToken).ConfigureAwait(false)).Result);
        }

        /// <summary>Simulates the call, so it is free even on a write method.</summary>
        private protected override Task<JsonNode?> ReadCoreAsync(string method, JsonObject parameters, CancellationToken cancellationToken) =>
            ReadAsync(ContractMethods.Find(_functions, method, parameters), parameters, cancellationToken);

        private protected override async Task<TransactionResult> SendCoreAsync(string method, JsonObject parameters, TransactionOptions? options,
            CancellationToken cancellationToken) =>
            (await SendAsync(ContractMethods.Find(_functions, method, parameters), parameters, options, cancellationToken).ConfigureAwait(false)).Result;

        /// <summary>The application arguments, concatenated: the method selector followed by the encoded arguments.</summary>
        private protected override string EncodeCore(string method, JsonObject parameters)
        {
            var fn = ContractMethods.Find(_functions, method, parameters);
            return Arc4Methods.ToHex(AppCalls.ApplicationArgs(new AppCall(_appId, fn, Arc4Methods.ToArguments(fn, parameters))));
        }

        /// <summary>Mints with the safeMint, mint, mintTo or arc72_mint method; the IDs come from arc72_Transfer events or from the assets the call created.</summary>
        private protected override async Task<NftMint> MintNftCoreAsync(JsonObject parameters, TransactionOptions? options, CancellationToken cancellationToken)
        {
            var standard = ArcStandards.TokenStandard(Standards);
            if (standard == "ARC-200") throw Errors.NotSupported("The contract is an ARC-200 token, not an NFT.");
            var sent = await SendAsync(TokenMethods.MintFunction(_functions, parameters, "arc72_mint"), parameters, options, cancellationToken).ConfigureAwait(false);
            var tokenIds = Tokens.MintedTokenIds(sent.Transaction.Logs).Concat(sent.Transaction.InnerCreatedAssets.Select(Integers.ToDecimalString)).ToList();
            return new NftMint { Standard = standard ?? "ASA", TokenIds = tokenIds, Transaction = sent.Result };
        }

        private Task<JsonNode?> ReadAsync(Arc4Function fn, JsonObject parameters, CancellationToken cancellationToken) =>
            _runner.ReadAsync(_appId, fn, Arc4Methods.ToArguments(fn, parameters), cancellationToken);

        private Task<Sent> SendAsync(Arc4Function fn, JsonObject parameters, TransactionOptions? options, CancellationToken cancellationToken) =>
            _runner.SendAsync(_appId, fn, Arc4Methods.ToArguments(fn, parameters), fn.Method.Name, options, cancellationToken);

        /// <summary>A token field; fixed-size byte values such as arc200_name are text padded with zero bytes.</summary>
        private async Task<string> ReadTokenAsync(string name, string? tokenId, CancellationToken cancellationToken)
        {
            var fn = _functions.First(candidate => candidate.Method.Name == name);
            var argument = fn.Method.Parameters.Keys.FirstOrDefault();
            var parameters = argument == null ? new JsonObject() : new JsonObject { [argument] = tokenId };
            var value = TextOf(await ReadAsync(fn, parameters, cancellationToken).ConfigureAwait(false));
            return fn.Method.Outputs.TryGetValue("result0", out var type) && ByteArray.IsMatch(type) ? Arc4Values.BytesToText(value) : value;
        }

        private static string TextOf(JsonNode? value) => JsonValues.Text(value) ?? value?.ToJsonString() ?? "null";
    }
}
