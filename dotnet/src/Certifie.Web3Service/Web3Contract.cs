using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Certifie.Web3
{
    /// <summary>What a <see cref="Web3Contract"/> needs from the <see cref="Web3Service"/> that created it.</summary>
    internal interface IContractRunner
    {
        Task<JsonNode?> ReadAsync(string abi, string address, string signature, object[] args, CancellationToken cancellationToken);

        Task<TransactionResult> SendAsync(string abi, string address, string signature, object[] args, string name, TransactionOptions? options,
            CancellationToken cancellationToken);

        Task<TransactionDetails> GetTransactionAsync(string transactionHash, string abi, CancellationToken cancellationToken);
    }

    /// <summary>
    /// A contract at an address, described by its ABI. Lists its functions and calls them with a JSON object of named parameters,
    /// so no ABI encoding is needed. Created by <see cref="Web3Service.GetContract"/>.
    /// </summary>
    public sealed class Web3Contract
    {
        private static readonly Regex AddressPattern = new Regex("^0x[0-9a-f]{40}$", RegexOptions.IgnoreCase);
        private readonly IReadOnlyList<AbiFunction> _functions;
        private readonly IContractRunner _runner;

        internal Web3Contract(string abi, string address, IContractRunner runner)
        {
            if (address == null || !AddressPattern.IsMatch(address)) throw Errors.InvalidArgument("The contract address must be 0x followed by 40 hex digits.");
            Abi = abi;
            Address = address;
            _runner = runner;
            _functions = AbiMethods.Describe(abi);
            Methods = _functions.Select(fn => fn.Method).ToList();
            Standards = ErcStandards.Detect(Methods.Select(method => method.Signature).ToList());
        }

        public string Abi { get; }

        public string Address { get; }

        /// <summary>Every function of the ABI: name, kind (read, write, payable) and the parameters <see cref="CallAsync"/> expects.</summary>
        public IReadOnlyList<ContractMethod> Methods { get; }

        /// <summary>ERC standards recognized from the ABI, e.g. <c>["ERC-721", "ERC-721 Metadata", "ERC-165"]</c>.</summary>
        public IReadOnlyList<string> Standards { get; }

        /// <summary>The <c>owner()</c> of the contract (Ownable, ERC-173); null when the ABI has no <c>owner()</c> function.</summary>
        public async Task<string?> OwnerAsync(CancellationToken cancellationToken = default) =>
            Has("owner()") ? (await ReadAsync("owner()", null, cancellationToken).ConfigureAwait(false))?.GetValue<string>() : null;

        /// <summary>
        /// Calls a function by name (or signature) with named parameters, e.g. <c>CallAsync("transfer", new { to, amount = "1000" })</c>.
        /// Read functions return their value; write and payable functions send a transaction and return it, as JSON, once mined.
        /// </summary>
        public Task<JsonNode?> CallAsync(string method, object? parameters = null, TransactionOptions? options = null, CancellationToken cancellationToken = default)
        {
            var values = AbiMethods.ToParameters(parameters);
            var fn = AbiMethods.Find(_functions, method, values);
            return fn.Method.Kind == "read" ? ReadAsync(fn, values, cancellationToken) : SendAsJsonAsync(fn, values, options, cancellationToken);
        }

        /// <summary>Returns the outputs without a transaction; on a write function it is a free dry run. Integers become decimal strings.</summary>
        public Task<JsonNode?> ReadAsync(string method, object? parameters = null, CancellationToken cancellationToken = default)
        {
            var values = AbiMethods.ToParameters(parameters);
            return ReadAsync(AbiMethods.Find(_functions, method, values), values, cancellationToken);
        }

        /// <summary>Sends a transaction, signed by the service's signer, and waits until it is mined. Coins for payable functions go in <c>options.Value</c>.</summary>
        public Task<TransactionResult> SendAsync(string method, object? parameters = null, TransactionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var values = AbiMethods.ToParameters(parameters);
            return SendAsync(AbiMethods.Find(_functions, method, values), values, options, cancellationToken);
        }

        /// <summary>The call data of a function call, e.g. to have it signed elsewhere.</summary>
        public string Encode(string method, object? parameters = null)
        {
            var values = AbiMethods.ToParameters(parameters);
            var fn = AbiMethods.Find(_functions, method, values);
            return ContractCalls.Encode(fn, AbiMethods.ToArguments(fn, values));
        }

        /// <summary>
        /// Mints an NFT on an ERC-721 or ERC-1155 contract with its safeMint, mint or mintTo function, chosen by the parameter names,
        /// e.g. <c>MintNftAsync(new { to, uri = "ipfs://…" })</c>. Returns the IDs of the new tokens.
        /// </summary>
        public async Task<NftMint> MintNftAsync(object parameters, TransactionOptions? options = null, CancellationToken cancellationToken = default)
        {
            var standard = TokenStandard();
            if (standard == "ERC-20") throw Errors.NotSupported("The contract is an ERC-20 token, not an NFT.");
            var values = AbiMethods.ToParameters(parameters);
            var transaction = await SendAsync(Tokens.MintFunction(_functions, values), values, options, cancellationToken).ConfigureAwait(false);
            return new NftMint { Standard = standard, TokenIds = Tokens.MintedTokenIds(transaction.Tx?.Logs ?? new List<TransactionLog>(), Address), Transaction = transaction };
        }

        /// <summary>Name, symbol, decimals and supply of a token; with the ID of an NFT, also its owner and URI.</summary>
        public async Task<TokenInfo> GetTokenInfoAsync(string? tokenId = null, CancellationToken cancellationToken = default)
        {
            var standard = TokenStandard();
            var id = tokenId == null || standard == "ERC-20" ? null : Integers.ToDecimalString(Integers.Parse(tokenId, "tokenId"));
            var info = new TokenInfo { Standard = standard, Address = Address, TokenId = id };
            foreach (var (field, signature) in Tokens.TokenReads(Methods.Select(method => method.Signature).ToList(), id != null))
            {
                var args = signature.EndsWith("()", System.StringComparison.Ordinal) ? new object[0] : new object[] { Integers.Parse(id, "tokenId") };
                Tokens.Assign(info, field, (await _runner.ReadAsync(Abi, Address, signature, args, cancellationToken).ConfigureAwait(false))!.GetValue<string>());
            }
            return Tokens.ExpandTokenUri(info);
        }

        /// <summary>A transaction by hash, with the function it called on this contract and its arguments.</summary>
        public Task<TransactionDetails> GetTransactionAsync(string transactionHash, CancellationToken cancellationToken = default) =>
            _runner.GetTransactionAsync(transactionHash, Abi, cancellationToken);

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
