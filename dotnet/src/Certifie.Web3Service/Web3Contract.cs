using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Certifie.Web3
{
    /// <summary>
    /// A contract at an address, described by its ABI (an EVM ABI, or an ARC-4, ARC-32 or ARC-56 description on Algorand).
    /// Lists its functions and calls them with a JSON object of named parameters, so no ABI encoding is needed.
    /// Created by <see cref="Web3Service.GetContract"/>; each blockchain provides its own implementation.
    /// </summary>
    public abstract class Web3Contract
    {
        private protected Web3Contract(string abi, string address)
        {
            Abi = abi;
            Address = address;
        }

        public string Abi { get; }

        /// <summary>Contract address on EVM networks, application ID on Algorand.</summary>
        public string Address { get; }

        /// <summary>Every function of the ABI: name, kind (read, write, payable) and the parameters <see cref="CallAsync"/> expects.</summary>
        public abstract IReadOnlyList<ContractMethod> Methods { get; }

        /// <summary>Standards recognized from the ABI, e.g. <c>["ERC-721", "ERC-721 Metadata", "ERC-165"]</c> or <c>["ARC-200"]</c>.</summary>
        public abstract IReadOnlyList<string> Standards { get; }

        /// <summary>The <c>owner()</c> of the contract (Ownable, ERC-173); null when the ABI has no <c>owner()</c> function.</summary>
        public abstract Task<string?> OwnerAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Calls a function by name (or signature) with named parameters, e.g. <c>CallAsync("transfer", new { to, amount = "1000" })</c>.
        /// Read functions return their value; write and payable functions send a transaction and return it, as JSON, once confirmed.
        /// </summary>
        public Task<JsonNode?> CallAsync(string method, object? parameters = null, TransactionOptions? options = null, CancellationToken cancellationToken = default) =>
            CallCoreAsync(method, ContractMethods.ToParameters(parameters), options, cancellationToken);

        /// <summary>Returns the outputs without a transaction; on a write function it is a free simulation. Integers become decimal strings.</summary>
        public Task<JsonNode?> ReadAsync(string method, object? parameters = null, CancellationToken cancellationToken = default) =>
            ReadCoreAsync(method, ContractMethods.ToParameters(parameters), cancellationToken);

        /// <summary>Sends a transaction, signed by the service's signer, and waits until it is confirmed. Coins for payable functions go in <c>options.Value</c>.</summary>
        public Task<TransactionResult> SendAsync(string method, object? parameters = null, TransactionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            SendCoreAsync(method, ContractMethods.ToParameters(parameters), options, cancellationToken);

        /// <summary>The call data of a function call (the application arguments on Algorand), e.g. to have it signed elsewhere.</summary>
        public string Encode(string method, object? parameters = null) => EncodeCore(method, ContractMethods.ToParameters(parameters));

        /// <summary>
        /// Mints an NFT with the safeMint, mint or mintTo function of the contract (also arc72_mint on Algorand), chosen by the parameter names,
        /// e.g. <c>MintNftAsync(new { to, uri = "ipfs://…" })</c>. Returns the IDs of the new tokens.
        /// </summary>
        public Task<NftMint> MintNftAsync(object parameters, TransactionOptions? options = null, CancellationToken cancellationToken = default) =>
            MintNftCoreAsync(ContractMethods.ToParameters(parameters), options, cancellationToken);

        /// <summary>Name, symbol, decimals and supply of a token; with the ID of an NFT, also its owner and URI.</summary>
        public abstract Task<TokenInfo> GetTokenInfoAsync(string? tokenId = null, CancellationToken cancellationToken = default);

        /// <summary>A transaction by hash, with the function it called on this contract and its arguments.</summary>
        public abstract Task<TransactionDetails> GetTransactionAsync(string transactionHash, CancellationToken cancellationToken = default);

        private protected abstract Task<JsonNode?> CallCoreAsync(string method, JsonObject parameters, TransactionOptions? options, CancellationToken cancellationToken);

        private protected abstract Task<JsonNode?> ReadCoreAsync(string method, JsonObject parameters, CancellationToken cancellationToken);

        private protected abstract Task<TransactionResult> SendCoreAsync(string method, JsonObject parameters, TransactionOptions? options,
            CancellationToken cancellationToken);

        private protected abstract string EncodeCore(string method, JsonObject parameters);

        private protected abstract Task<NftMint> MintNftCoreAsync(JsonObject parameters, TransactionOptions? options, CancellationToken cancellationToken);
    }
}
