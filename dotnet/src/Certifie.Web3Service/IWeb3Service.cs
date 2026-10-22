using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Certifie.Web3
{
    /// <summary>
    /// What every blockchain offers through <see cref="Web3Service"/>. The provider passed to <see cref="Web3Service"/> selects the implementation:
    /// <see cref="IEvmProvider"/> → EVM networks, <see cref="IAvmProvider"/> → Algorand, <see cref="IBitcoinProvider"/> → Bitcoin.
    /// An operation the blockchain does not have throws <see cref="System.NotImplementedException"/>.
    /// </summary>
    public interface IWeb3Service
    {
        /// <summary><c>evm</c>, <c>avm</c> or <c>bitcoin</c>.</summary>
        string Kind { get; }

        /// <summary>The provider configuration with the defaults applied.</summary>
        ResolvedProvider Provider { get; }

        /// <summary>Hex chain ID on EVM networks (e.g. <c>0x539</c>), genesis ID on Algorand (e.g. <c>mainnet-v1.0</c>), network name on Bitcoin.</summary>
        string ChainId { get; }

        /// <summary>The latest block and the current fee.</summary>
        Task<CheckResult> CheckAsync(CancellationToken cancellationToken = default);

        /// <summary>A contract at an address (an application ID on Algorand): lists its functions and calls them with named parameters.</summary>
        Web3Contract GetContract(string abi, string address);

        /// <summary>Calls a read-only method. A single output is returned as its value, several outputs as a JSON array. Integers become decimal strings.</summary>
        Task<JsonNode?> CallContractMethodAsync(string method, string abi, string address, object[]? parameters = null, CancellationToken cancellationToken = default);

        /// <summary>Notarizes a document hash by calling <c>mint(address,string,string,string)</c> on the contract.</summary>
        Task<TransactionResult> MintAsync(string abi, string contractAddress, NotarizationDocument document, TransactionResult? onChainEvent = null,
            TransactionOptions? options = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deploys a contract; <c>Tx.ContractAddress</c> is its address (its application ID on Algorand).
        /// On Algorand an empty bytecode takes the programs from the ARC-56 description.
        /// </summary>
        Task<TransactionResult> DeployAsync(string abi, string bytecode, object[]? constructorArguments = null, TransactionResult? onChainEvent = null,
            TransactionOptions? options = null, CancellationToken cancellationToken = default);

        /// <summary>Deploys an Algorand application from its approval and clear programs.</summary>
        Task<TransactionResult> DeployAsync(string abi, AvmPrograms programs, object[]? constructorArguments = null, TransactionResult? onChainEvent = null,
            TransactionOptions? options = null, CancellationToken cancellationToken = default);

        /// <summary>Creates a token without a contract, such as an Algorand Standard Asset; EVM tokens are contracts, deployed with DeployAsync.</summary>
        Task<TokenCreation> CreateTokenAsync(TokenDefinition token, TransactionResult? onChainEvent = null, TransactionOptions? options = null,
            CancellationToken cancellationToken = default);

        /// <summary>Data of a token created with CreateTokenAsync, e.g. an Algorand Standard Asset by its ID.</summary>
        Task<TokenInfo> GetTokenInfoAsync(string tokenId, CancellationToken cancellationToken = default);

        /// <summary>The receipt of a confirmed transaction, with its block time and exact fee.</summary>
        Task<TransactionResult> GetTransactionReceiptAsync(string transactionHash, TransactionResult? onChainEvent = null, CancellationToken cancellationToken = default);

        /// <summary>A transaction by hash, pending or confirmed. With the contract ABI, the called function and its arguments are decoded too.</summary>
        Task<TransactionDetails> GetTransactionAsync(string transactionHash, string? abi = null, CancellationToken cancellationToken = default);
    }
}
