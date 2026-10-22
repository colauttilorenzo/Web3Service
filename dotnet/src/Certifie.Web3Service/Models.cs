using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Certifie.Web3
{
    /// <summary>Gas token of the network. Decimals are informational: fees are always returned in the smallest unit.</summary>
    public sealed class NativeCurrency
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("decimals")] public int? Decimals { get; set; }
    }

    /// <summary>
    /// The fields every provider shares. Only <see cref="RpcURI"/> is required; omitted fields use the defaults of the blockchain.
    /// The type of the provider selects the blockchain: <see cref="IEvmProvider"/>, <see cref="IAvmProvider"/> or <see cref="IBitcoinProvider"/>.
    /// </summary>
    public interface IBlockchainProvider
    {
        /// <summary>Node or API URI.</summary>
        string RpcURI { get; }
        /// <summary>Fallback endpoint, used when the primary is unavailable or on another network.</summary>
        string? RpcPublicURI { get; }
        /// <summary>EVM chain ID (decimal or hex), Algorand genesis ID or Bitcoin network name.</summary>
        string? ChainId { get; }
        string? ChainName { get; }
        NativeCurrency? NativeCurrency { get; }
        string? BlockExplorerURI { get; }
    }

    /// <summary>An EVM network (Ethereum, Polygon, …) through a JSON-RPC node. Defaults: chain 0x1, Ethereum, Ether, ETH, 18 decimals.</summary>
    public interface IEvmProvider : IBlockchainProvider { }

    /// <summary>Algorand (the AVM) through an algod node. Defaults: mainnet-v1.0, Algorand, Algo, ALGO, 6 decimals.</summary>
    public interface IAvmProvider : IBlockchainProvider
    {
        /// <summary>algod API token, sent as X-Algo-API-Token; public nodes need none.</summary>
        string? Token { get; }
        /// <summary>Indexer URI, used to read transactions that algod no longer keeps.</summary>
        string? IndexerURI { get; }
        string? IndexerToken { get; }
    }

    /// <summary>Bitcoin through an Esplora REST API, such as https://mempool.space/api. Defaults: mainnet, Bitcoin, BTC, 8 decimals.</summary>
    public interface IBitcoinProvider : IBlockchainProvider { }

    /// <summary>Initialization input for EVM networks. Only <see cref="RpcURI"/> is required; omitted fields use Ethereum defaults.</summary>
    public sealed class EvmProvider : IEvmProvider
    {
        /// <summary>Decimal (<c>"1337"</c>) or hex (<c>"0x539"</c>) chain ID. Default: <c>0x1</c>.</summary>
        [JsonPropertyName("chainId")] public string? ChainId { get; set; }
        /// <summary>HTTP(S) or WS(S) node URI.</summary>
        [JsonPropertyName("rpcURI")] public string RpcURI { get; set; } = "";
        /// <summary>Fallback node, used when the primary is unavailable or on the wrong chain.</summary>
        [JsonPropertyName("rpcPublicURI")] public string? RpcPublicURI { get; set; }
        [JsonPropertyName("nativeCurrency")] public NativeCurrency? NativeCurrency { get; set; }
        [JsonPropertyName("chainName")] public string? ChainName { get; set; }
        [JsonPropertyName("blockExplorerURI")] public string? BlockExplorerURI { get; set; }
    }

    /// <summary>Initialization input for Algorand. Only <see cref="RpcURI"/> is required; omitted fields use Algorand MainNet defaults.</summary>
    public sealed class AvmProvider : IAvmProvider
    {
        /// <summary>Genesis ID of the network, e.g. <c>mainnet-v1.0</c> or <c>testnet-v1.0</c>. Default: <c>mainnet-v1.0</c>.</summary>
        [JsonPropertyName("chainId")] public string? ChainId { get; set; }
        /// <summary>HTTP(S) algod URI, e.g. https://mainnet-api.algonode.cloud.</summary>
        [JsonPropertyName("rpcURI")] public string RpcURI { get; set; } = "";
        [JsonPropertyName("rpcPublicURI")] public string? RpcPublicURI { get; set; }
        [JsonPropertyName("token")] public string? Token { get; set; }
        [JsonPropertyName("indexerURI")] public string? IndexerURI { get; set; }
        [JsonPropertyName("indexerToken")] public string? IndexerToken { get; set; }
        [JsonPropertyName("nativeCurrency")] public NativeCurrency? NativeCurrency { get; set; }
        [JsonPropertyName("chainName")] public string? ChainName { get; set; }
        [JsonPropertyName("blockExplorerURI")] public string? BlockExplorerURI { get; set; }
    }

    /// <summary>Initialization input for Bitcoin. Only <see cref="RpcURI"/> is required; omitted fields use Bitcoin mainnet defaults.</summary>
    public sealed class BitcoinProvider : IBitcoinProvider
    {
        /// <summary><c>mainnet</c>, <c>testnet</c>, <c>testnet4</c>, <c>signet</c> or <c>regtest</c>. Default: <c>mainnet</c>.</summary>
        [JsonPropertyName("chainId")] public string? ChainId { get; set; }
        /// <summary>HTTP(S) Esplora API URI.</summary>
        [JsonPropertyName("rpcURI")] public string RpcURI { get; set; } = "";
        [JsonPropertyName("rpcPublicURI")] public string? RpcPublicURI { get; set; }
        [JsonPropertyName("nativeCurrency")] public NativeCurrency? NativeCurrency { get; set; }
        [JsonPropertyName("chainName")] public string? ChainName { get; set; }
        [JsonPropertyName("blockExplorerURI")] public string? BlockExplorerURI { get; set; }
    }

    /// <summary>Configuration with the defaults applied, as exposed by <see cref="Web3Service.Provider"/>.</summary>
    public abstract class ResolvedProvider
    {
        private protected ResolvedProvider(string kind, IBlockchainProvider input, string chainId, string chainName, (string Name, string Symbol, int Decimals) currency)
        {
            Kind = kind; ChainId = chainId; RpcURI = input.RpcURI; RpcPublicURI = input.RpcPublicURI; ChainName = chainName;
            CurrencyName = currency.Name; Symbol = currency.Symbol; Decimals = currency.Decimals; BlockExplorerURI = input.BlockExplorerURI;
        }

        /// <summary><c>evm</c>, <c>avm</c> or <c>bitcoin</c>.</summary>
        public string Kind { get; }
        /// <summary>Normalized hex chain ID on EVM networks (e.g. <c>0x539</c>), genesis ID on Algorand, network name on Bitcoin.</summary>
        public string ChainId { get; }
        public string RpcURI { get; }
        public string? RpcPublicURI { get; }
        public string ChainName { get; }
        public string CurrencyName { get; }
        public string Symbol { get; }
        public int Decimals { get; }
        public string? BlockExplorerURI { get; }
    }

    /// <summary>An EVM network with the defaults applied.</summary>
    public sealed class EthereumProvider : ResolvedProvider
    {
        internal EthereumProvider(IEvmProvider input, string chainId, string chainName, (string Name, string Symbol, int Decimals) currency)
            : base("evm", input, chainId, chainName, currency) { }
    }

    /// <summary>An Algorand network with the defaults applied.</summary>
    public sealed class AlgorandNetwork : ResolvedProvider
    {
        internal AlgorandNetwork(IAvmProvider input, string chainId, string chainName, (string Name, string Symbol, int Decimals) currency)
            : base("avm", input, chainId, chainName, currency)
        {
            Token = input.Token; IndexerURI = input.IndexerURI; IndexerToken = input.IndexerToken;
        }

        public string? Token { get; }
        public string? IndexerURI { get; }
        public string? IndexerToken { get; }
    }

    /// <summary>A Bitcoin network with the defaults applied.</summary>
    public sealed class BitcoinNetwork : ResolvedProvider
    {
        internal BitcoinNetwork(IBitcoinProvider input, string chainId, string chainName, (string Name, string Symbol, int Decimals) currency)
            : base("bitcoin", input, chainId, chainName, currency) { }
    }

    /// <summary>Signing credentials and timeouts. Decrypting and storing secrets belongs to the application.</summary>
    public sealed class Web3ServiceOptions
    {
        /// <summary>
        /// Hex private key. On Algorand: the 32-byte Ed25519 seed as 0x hex, or the 64-byte secret key as base64.
        /// Use either this or <see cref="Mnemonic"/>.
        /// </summary>
        public string? PrivateKey { get; set; }
        /// <summary>BIP-39 phrase; the key is derived at <c>m/44'/60'/0'/0/{WalletIndex}</c>. On Algorand: the account's 25-word mnemonic.</summary>
        public string? Mnemonic { get; set; }
        /// <summary>EVM networks only.</summary>
        public string MnemonicPassword { get; set; } = "";
        /// <summary>EVM networks only. Default: 1, as in the original CERTIFIE service.</summary>
        public int WalletIndex { get; set; } = 1;
        /// <summary>Timeout for each RPC request, including provider selection. Default: 30 seconds.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
        /// <summary>Time to wait for mining after broadcast. Default: 120 seconds.</summary>
        public TimeSpan TransactionTimeout { get; set; } = TimeSpan.FromSeconds(120);
        /// <summary>Receipt polling interval. Default: 1 second.</summary>
        public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);
        /// <summary>EVM networks only: added to the estimated gas limit. Default: 10000.</summary>
        public BigInteger GasBuffer { get; set; } = 10_000;

        internal Web3ServiceOptions Copy() => (Web3ServiceOptions)MemberwiseClone();
    }

    /// <summary>On Algorand <see cref="GasPrice"/> is the flat fee in microAlgos and <see cref="Value"/> the microAlgos paid to the application; gas and nonce do not apply.</summary>
    public sealed class TransactionOptions
    {
        public BigInteger? Gas { get; set; }
        public BigInteger? GasPrice { get; set; }
        public BigInteger? Nonce { get; set; }
        public BigInteger? Value { get; set; }
    }

    public sealed class NotarizationHash
    {
        [JsonPropertyName("data")] public string Data { get; set; } = "";
        [JsonPropertyName("algorithm")] public string Algorithm { get; set; } = "";
    }

    public sealed class NotarizationDocument
    {
        [JsonPropertyName("hash")] public NotarizationHash Hash { get; set; } = new NotarizationHash();
        [JsonPropertyName("metadata")] public string? Metadata { get; set; }
    }

    public sealed class WalletInfo
    {
        [JsonPropertyName("address")] public string Address { get; set; } = "";
        [JsonPropertyName("publicKey")] public string PublicKey { get; set; } = "";
        [JsonPropertyName("privateKey")] public string PrivateKey { get; set; } = "";
        [JsonPropertyName("path")] public string? Path { get; set; }
        [JsonPropertyName("index")] public string Index { get; set; } = "";
    }

    /// <summary>Latest block and current fee: the gas price in wei on EVM networks, the minimum fee in microAlgos on Algorand, sat/vB on Bitcoin.</summary>
    public sealed class CheckResult
    {
        [JsonPropertyName("blockNumber")] public string BlockNumber { get; set; } = "";
        [JsonPropertyName("gasPrice")] public string GasPrice { get; set; } = "";
    }

    /// <summary>All numeric RPC fields are decimal integer strings, including zero.</summary>
    public sealed class TransactionLog
    {
        [JsonPropertyName("address")] public string? Address { get; set; }
        [JsonPropertyName("data")] public string? Data { get; set; }
        [JsonPropertyName("topics")] public List<string>? Topics { get; set; }
        [JsonPropertyName("logIndex")] public string? LogIndex { get; set; }
        [JsonPropertyName("transactionIndex")] public string? TransactionIndex { get; set; }
        [JsonPropertyName("transactionHash")] public string? TransactionHash { get; set; }
        [JsonPropertyName("blockHash")] public string? BlockHash { get; set; }
        [JsonPropertyName("blockNumber")] public string? BlockNumber { get; set; }
        [JsonPropertyName("removed")] public bool? Removed { get; set; }
    }

    /// <summary>All numeric RPC fields are decimal integer strings, including zero.</summary>
    public sealed class TransactionReceipt
    {
        [JsonPropertyName("transactionHash")] public string TransactionHash { get; set; } = "";
        [JsonPropertyName("blockHash")] public string BlockHash { get; set; } = "";
        [JsonPropertyName("blockNumber")] public string BlockNumber { get; set; } = "";
        [JsonPropertyName("transactionIndex")] public string TransactionIndex { get; set; } = "";
        [JsonPropertyName("from")] public string From { get; set; } = "";
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("contractAddress")] public string? ContractAddress { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("cumulativeGasUsed")] public string CumulativeGasUsed { get; set; } = "";
        [JsonPropertyName("gasUsed")] public string GasUsed { get; set; } = "";
        [JsonPropertyName("effectiveGasPrice")] public string? EffectiveGasPrice { get; set; }
        [JsonPropertyName("logsBloom")] public string LogsBloom { get; set; } = "";
        [JsonPropertyName("logs")] public List<TransactionLog> Logs { get; set; } = new List<TransactionLog>();
    }

    /// <summary>Exact gas cost in the native token's smallest unit (wei on Ethereum).</summary>
    public sealed class NativeFee
    {
        [JsonPropertyName("amount")] public string Amount { get; set; } = "";
        [JsonPropertyName("symbol")] public string Symbol { get; set; } = "";
        [JsonPropertyName("decimals")] public string Decimals { get; set; } = "";
    }

    /// <summary>Same shape and JSON names as <c>ITransaction</c> in the JavaScript package.</summary>
    public sealed class TransactionResult
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("requestedAt")] public string? RequestedAt { get; set; }
        [JsonPropertyName("completedAt")] public string? CompletedAt { get; set; }
        [JsonPropertyName("rejectedAt")] public string? RejectedAt { get; set; }
        [JsonPropertyName("tx")] public TransactionReceipt? Tx { get; set; }
        [JsonPropertyName("error")] public Web3ErrorInfo? Error { get; set; }
        [JsonPropertyName("nativeFee")] public NativeFee? NativeFee { get; set; }

        internal TransactionResult Copy() => (TransactionResult)MemberwiseClone();
    }

    /// <summary>A contract function as <see cref="Web3Contract.Methods"/> lists it; same JSON as <c>IContractMethod</c> in the JavaScript package.</summary>
    public sealed class ContractMethod
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        /// <summary>Canonical signature, e.g. <c>transfer(address,uint256)</c>; pass it to <c>CallAsync</c> to pick one of several functions with the same name.</summary>
        [JsonPropertyName("signature")] public string Signature { get; set; } = "";
        /// <summary><c>read</c>: returns data, free, no transaction. <c>write</c>: changes the contract with a transaction. <c>payable</c>: a write that can also receive coins.</summary>
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";
        /// <summary><c>pure</c>, <c>view</c>, <c>nonpayable</c> or <c>payable</c>.</summary>
        [JsonPropertyName("stateMutability")] public string StateMutability { get; set; } = "";
        /// <summary>Parameter name → JSON type to pass: string, number, boolean, array or object. Unnamed parameters are arg0, arg1, …</summary>
        [JsonPropertyName("parameters")] public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
        /// <summary>Parameter name → Solidity type, e.g. address or uint256.</summary>
        [JsonPropertyName("inputs")] public Dictionary<string, string> Inputs { get; set; } = new Dictionary<string, string>();
        /// <summary>Output name → Solidity type; unnamed outputs are result0, result1, …</summary>
        [JsonPropertyName("outputs")] public Dictionary<string, string> Outputs { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>Token data read from the contract or the blockchain. Only the fields the token offers are set.</summary>
    public sealed class TokenInfo
    {
        /// <summary><c>ERC-20</c>, <c>ERC-721</c>, <c>ERC-1155</c>, <c>ARC-200</c>, <c>ARC-72</c> or <c>ASA</c>.</summary>
        [JsonPropertyName("standard")] public string Standard { get; set; } = "";
        [JsonPropertyName("address")] public string Address { get; set; } = "";
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("symbol")] public string? Symbol { get; set; }
        [JsonPropertyName("decimals")] public string? Decimals { get; set; }
        [JsonPropertyName("totalSupply")] public string? TotalSupply { get; set; }
        /// <summary>NFTs only, when a token ID is passed to <c>GetTokenInfoAsync</c>.</summary>
        [JsonPropertyName("tokenId")] public string? TokenId { get; set; }
        [JsonPropertyName("owner")] public string? Owner { get; set; }
        [JsonPropertyName("tokenURI")] public string? TokenURI { get; set; }
        /// <summary>The account that created an Algorand Standard Asset.</summary>
        [JsonPropertyName("creator")] public string? Creator { get; set; }
    }

    public sealed class NftMint
    {
        /// <summary><c>ERC-721</c>, <c>ERC-1155</c>, <c>ARC-72</c> or <c>ASA</c>.</summary>
        [JsonPropertyName("standard")] public string Standard { get; set; } = "";
        /// <summary>
        /// IDs of the created tokens: from the Transfer, TransferSingle and TransferBatch events on EVM networks,
        /// from arc72_Transfer events or the created assets on Algorand.
        /// </summary>
        [JsonPropertyName("tokenIds")] public List<string> TokenIds { get; set; } = new List<string>();
        [JsonPropertyName("transaction")] public TransactionResult Transaction { get; set; } = new TransactionResult();
    }

    /// <summary>Compiled Algorand programs as base64 or 0x hex, or TEAL source that the node compiles.</summary>
    public sealed class AvmPrograms
    {
        [JsonPropertyName("approval")] public string Approval { get; set; } = "";
        [JsonPropertyName("clear")] public string Clear { get; set; } = "";
    }

    /// <summary>A token without a contract, such as an Algorand Standard Asset.</summary>
    public sealed class TokenDefinition
    {
        /// <summary>At most 32 bytes on Algorand.</summary>
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        /// <summary>The unit name; at most 8 bytes on Algorand.</summary>
        [JsonPropertyName("symbol")] public string Symbol { get; set; } = "";
        /// <summary>Number of base units, between 1 and 2^64 - 1 on Algorand.</summary>
        [JsonPropertyName("totalSupply")] public BigInteger TotalSupply { get; set; }
        /// <summary>0 to 19. Default: 0.</summary>
        [JsonPropertyName("decimals")] public int? Decimals { get; set; }
        /// <summary>At most 96 bytes on Algorand.</summary>
        [JsonPropertyName("uri")] public string? Uri { get; set; }
        /// <summary>32 bytes as 0x hex, e.g. the SHA-256 of the metadata the URI points to.</summary>
        [JsonPropertyName("metadataHash")] public string? MetadataHash { get; set; }
    }

    /// <summary>A token created with <see cref="Web3Service.CreateTokenAsync"/>, and the transaction that created it.</summary>
    public sealed class TokenCreation
    {
        /// <summary><c>ASA</c>.</summary>
        [JsonPropertyName("standard")] public string Standard { get; set; } = "";
        /// <summary>The asset ID on Algorand.</summary>
        [JsonPropertyName("tokenId")] public string TokenId { get; set; } = "";
        [JsonPropertyName("transaction")] public TransactionResult Transaction { get; set; } = new TransactionResult();
    }

    /// <summary>The contract function a transaction called, with its arguments as JSON-safe values.</summary>
    public sealed class DecodedCall
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("signature")] public string Signature { get; set; } = "";
        [JsonPropertyName("parameters")] public JsonObject Parameters { get; set; } = new JsonObject();
    }

    /// <summary>A transaction read by hash; same JSON as <c>ITransactionDetails</c> in the JavaScript package. Numeric fields are decimal strings.</summary>
    public sealed class TransactionDetails
    {
        [JsonPropertyName("hash")] public string Hash { get; set; } = "";
        [JsonPropertyName("from")] public string From { get; set; } = "";
        /// <summary>Null for contract deployments.</summary>
        [JsonPropertyName("to")] public string? To { get; set; }
        /// <summary>Native currency sent, in the smallest unit (wei on Ethereum).</summary>
        [JsonPropertyName("value")] public string Value { get; set; } = "";
        [JsonPropertyName("nonce")] public string Nonce { get; set; } = "";
        [JsonPropertyName("gas")] public string Gas { get; set; } = "";
        [JsonPropertyName("gasPrice")] public string? GasPrice { get; set; }
        [JsonPropertyName("maxFeePerGas")] public string? MaxFeePerGas { get; set; }
        [JsonPropertyName("maxPriorityFeePerGas")] public string? MaxPriorityFeePerGas { get; set; }
        [JsonPropertyName("input")] public string Input { get; set; } = "";
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("blockHash")] public string? BlockHash { get; set; }
        [JsonPropertyName("blockNumber")] public string? BlockNumber { get; set; }
        [JsonPropertyName("transactionIndex")] public string? TransactionIndex { get; set; }
        /// <summary>True while the transaction waits to be mined.</summary>
        [JsonPropertyName("pending")] public bool Pending { get; set; }
        /// <summary>Block time in milliseconds, once mined.</summary>
        [JsonPropertyName("timestamp")] public string? Timestamp { get; set; }
        [JsonPropertyName("receipt")] public TransactionReceipt? Receipt { get; set; }
        [JsonPropertyName("nativeFee")] public NativeFee? NativeFee { get; set; }
        /// <summary>The called function and its arguments, when the ABI passed to <c>GetTransactionAsync</c> contains it.</summary>
        [JsonPropertyName("method")] public DecodedCall? Method { get; set; }
    }
}
