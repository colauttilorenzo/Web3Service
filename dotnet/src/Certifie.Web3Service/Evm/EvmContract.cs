using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Certifie.Web3.Evm
{
    /// <summary>What an <see cref="EvmContract"/> needs from the <see cref="EvmChain"/> that created it.</summary>
    internal interface IContractRunner
    {
        Task<JsonNode?> ReadAsync(string abi, string address, string signature, object[] args, CancellationToken cancellationToken);

        Task<TransactionResult> SendAsync(string abi, string address, string signature, object[] args, string name, TransactionOptions? options,
            CancellationToken cancellationToken);

        Task<TransactionDetails> GetTransactionAsync(string transactionHash, string abi, CancellationToken cancellationToken);
    }

    /// <summary>A contract on an EVM network, described by its JSON ABI.</summary>
    internal sealed class EvmContract : Web3Contract
    {
        private static readonly Regex AddressPattern = new Regex("^0x[0-9a-f]{40}$", RegexOptions.IgnoreCase);
        private readonly IReadOnlyList<AbiFunction> _functions;
        private readonly IContractRunner _runner;

        public EvmContract(string abi, string address, IContractRunner runner) : base(abi, RequireAddress(address))
        {
            _runner = runner;
            _functions = AbiMethods.Describe(abi);
            Methods = _functions.Select(fn => fn.Method).ToList();
            Standards = ErcStandards.Detect(Methods.Select(method => method.Signature).ToList());
        }

        public override IReadOnlyList<ContractMethod> Methods { get; }

        /// <summary>ERC standards recognized from the ABI, e.g. <c>["ERC-721", "ERC-721 Metadata", "ERC-165"]</c>.</summary>
        public override IReadOnlyList<string> Standards { get; }

        public override async Task<string?> OwnerAsync(CancellationToken cancellationToken = default) =>
            Has("owner()") ? (await ReadAsync("owner()", null, cancellationToken).ConfigureAwait(false))?.GetValue<string>() : null;

        /// <summary>Name, symbol, decimals and supply of an ERC-20, ERC-721 or ERC-1155 token; with the ID of an NFT, also its owner and URI.</summary>
        public override async Task<TokenInfo> GetTokenInfoAsync(string? tokenId = null, CancellationToken cancellationToken = default)
        {
            var standard = TokenStandard();
            var id = tokenId == null || standard == "ERC-20" ? null : Integers.ToDecimalString(Integers.Parse(tokenId, "tokenId"));
            var info = new TokenInfo { Standard = standard, Address = Address, TokenId = id };
            foreach (var (field, signature) in Tokens.TokenReads(Methods.Select(method => method.Signature).ToList(), id != null))
            {
                var args = signature.EndsWith("()", System.StringComparison.Ordinal) ? new object[0] : new object[] { Integers.Parse(id, "tokenId") };
                TokenMethods.Assign(info, field, (await _runner.ReadAsync(Abi, Address, signature, args, cancellationToken).ConfigureAwait(false))!.GetValue<string>());
            }
            return Tokens.ExpandTokenUri(info);
        }

        public override Task<TransactionDetails> GetTransactionAsync(string transactionHash, CancellationToken cancellationToken = default) =>
            _runner.GetTransactionAsync(transactionHash, Abi, cancellationToken);

        private protected override Task<JsonNode?> CallCoreAsync(string method, JsonObject parameters, TransactionOptions? options,
            CancellationToken cancellationToken)
        {
            var fn = ContractMethods.Find(_functions, method, parameters);
            return fn.Method.Kind == "read" ? ReadAsync(fn, parameters, cancellationToken) : SendAsJsonAsync(fn, parameters, options, cancellationToken);
        }

        private protected override Task<JsonNode?> ReadCoreAsync(string method, JsonObject parameters, CancellationToken cancellationToken) =>
            ReadAsync(ContractMethods.Find(_functions, method, parameters), parameters, cancellationToken);

        private protected override Task<TransactionResult> SendCoreAsync(string method, JsonObject parameters, TransactionOptions? options,
            CancellationToken cancellationToken) =>
            SendAsync(ContractMethods.Find(_functions, method, parameters), parameters, options, cancellationToken);

        private protected override string EncodeCore(string method, JsonObject parameters)
        {
            var fn = ContractMethods.Find(_functions, method, parameters);
            return ContractCalls.Encode(fn, AbiMethods.ToArguments(fn, parameters));
        }

        /// <summary>Mints on an ERC-721 or ERC-1155 contract; the token IDs come from its Transfer, TransferSingle and TransferBatch events.</summary>
        private protected override async Task<NftMint> MintNftCoreAsync(JsonObject parameters, TransactionOptions? options, CancellationToken cancellationToken)
        {
            var standard = TokenStandard();
            if (standard == "ERC-20") throw Errors.NotSupported("The contract is an ERC-20 token, not an NFT.");
            var transaction = await SendAsync(TokenMethods.MintFunction(_functions, parameters), parameters, options, cancellationToken).ConfigureAwait(false);
            return new NftMint { Standard = standard, TokenIds = Tokens.MintedTokenIds(transaction.Tx?.Logs ?? new List<TransactionLog>(), Address), Transaction = transaction };
        }

        private static string RequireAddress(string address) => address != null && AddressPattern.IsMatch(address)
            ? address : throw Errors.InvalidArgument("The contract address must be 0x followed by 40 hex digits.");

        private Task<JsonNode?> ReadAsync(AbiFunction fn, JsonObject parameters, CancellationToken cancellationToken) =>
            _runner.ReadAsync(Abi, Address, fn.Method.Signature, AbiMethods.ToArguments(fn, parameters), cancellationToken);

        private Task<TransactionResult> SendAsync(AbiFunction fn, JsonObject parameters, TransactionOptions? options, CancellationToken cancellationToken) =>
            _runner.SendAsync(Abi, Address, fn.Method.Signature, AbiMethods.ToArguments(fn, parameters), fn.Method.Name, options, cancellationToken);

        private async Task<JsonNode?> SendAsJsonAsync(AbiFunction fn, JsonObject parameters, TransactionOptions? options, CancellationToken cancellationToken) =>
            JsonSerializer.SerializeToNode(await SendAsync(fn, parameters, options, cancellationToken).ConfigureAwait(false));

        private bool Has(string signature) => Methods.Any(method => method.Signature == signature);

        private string TokenStandard() =>
            ErcStandards.TokenStandard(Standards) ?? throw Errors.NotSupported("The contract is not an ERC-20, ERC-721 or ERC-1155 token.");
    }
}
