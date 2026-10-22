using System.Text.Json;
using System.Text.Json.Nodes;
using Certifie.Web3;
using Certifie.Web3.Algorand;
using Xunit;
using static Certifie.Web3Service.Tests.TestVectors;
using Service = Certifie.Web3.Web3Service;

namespace Certifie.Web3Service.Tests;

/// <summary>
/// Mirrors typescript/test/MultiChainTests.test.mjs: the provider selects the blockchain, and what each one offers without a node:
/// Algorand accounts and ARC-4 contracts, Bitcoin through a local Esplora API, and NotImplementedException.
/// </summary>
public class MultiChainTests
{
    private static readonly JsonNode Algorand = Root["algorand"]!;
    private static readonly JsonNode Bitcoin = Root["bitcoin"]!;

    private static Service AlgorandService() => new(new AvmProvider { RpcURI = "http://localhost" });

    private static Service BitcoinService() => new(new BitcoinProvider { RpcURI = "http://localhost" });

    [Fact]
    public void SelectsTheBlockchainFromTheProvider()
    {
        Assert.Equal("evm", new Service("http://localhost").Kind);
        Assert.Equal(Text(Root["defaults"]!["chainId"]), new Service(new EvmProvider { RpcURI = "http://localhost" }).ChainId);
        foreach (var (service, defaults, kind) in new[] { (AlgorandService(), Algorand["defaults"]!, "avm"), (BitcoinService(), Bitcoin["defaults"]!, "bitcoin") })
        {
            Assert.Equal((kind, Text(defaults["chainId"]), Text(defaults["chainName"])), (service.Kind, service.ChainId, service.Provider.ChainName));
            Assert.Equal((Text(defaults["currencyName"]), Text(defaults["symbol"]), (int)defaults["decimals"]!), (service.Provider.CurrencyName, service.Provider.Symbol, service.Provider.Decimals));
        }
        Assert.Equal("testnet-v1.0", new Service(new AvmProvider { RpcURI = "http://localhost", ChainId = "testnet-v1.0" }).ChainId);
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<Web3Exception>(() => new Service(new SolanaProvider())).Code);
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<Web3Exception>(() => new Service(new AvmProvider { RpcURI = "ws://localhost" })).Code);
    }

    [Fact]
    public async Task UnavailableOperationsThrowNotImplemented()
    {
        var evm = new Service("http://localhost");
        await Assert.ThrowsAsync<NotImplementedException>(() => evm.CreateTokenAsync(new TokenDefinition { Name = "Coin", Symbol = "CN", TotalSupply = 1 }));
        await Assert.ThrowsAsync<NotImplementedException>(() => evm.GetTokenInfoAsync("1"));
        var service = BitcoinService();
        var operations = new Dictionary<string, Func<Task>>
        {
            ["getContract"] = () => Task.FromResult(service.GetContract("{}", "1")), ["callContractMethod"] = () => service.CallContractMethodAsync("m", "{}", "1"),
            ["deploy"] = () => service.DeployAsync("{}", ""),
            ["createToken"] = () => service.CreateTokenAsync(new TokenDefinition()), ["getTokenInfo"] = () => service.GetTokenInfoAsync("1"),
        };
        foreach (var operation in Texts(Bitcoin["notImplemented"])) await Assert.ThrowsAsync<NotImplementedException>(operations[operation]);
    }

    [Fact]
    public void DerivesTheSharedAlgorandWallets()
    {
        foreach (var expected in Algorand["wallets"]!.AsArray())
        {
            var wallet = AlgorandWallets.GetWallet(Text(expected!["mnemonic"]));
            Assert.Equal((Text(expected["address"]), Text(expected["publicKey"]), Text(expected["privateKey"]), null, "0"),
                (wallet.Address, wallet.PublicKey, wallet.PrivateKey, wallet.Path, wallet.Index));
        }
        Assert.Equal(25, AlgorandWallets.CreateMnemonic().Split(' ').Length);
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<Web3Exception>(() => AlgorandWallets.GetWallet(Mnemonic)).Code);
    }

    [Fact]
    public void DescribesArc4MethodsWithJsonParameterTypes()
    {
        var (abi, address) = (Algorand["contract"]!["abi"]!.ToJsonString(), Text(Algorand["contract"]!["address"]));
        var contract = AlgorandService().GetContract(abi, address);
        Assert.Equal(Algorand["contract"]!["methods"]!.ToJsonString(), JsonSerializer.Serialize(contract.Methods));
        Assert.Equal((address, 0), (contract.Address, contract.Standards.Count));
        foreach (var invalid in new[] { "0", "-1", "1.5", Text(Algorand["wallets"]![0]!["address"]) })
            Assert.Equal("INVALID_ARGUMENT", Assert.Throws<Web3Exception>(() => AlgorandService().GetContract(abi, invalid)).Code);
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<Web3Exception>(() => AlgorandService().GetContract(Root["contractAbi"]!["abi"]!.ToJsonString(), address)).Code);
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<Web3Exception>(() => new Service("http://localhost").GetContract(abi, Text(Root["contractAbi"]!["address"]))).Code);
    }

    [Fact]
    public void DetectsArcStandardsFromTheMethods()
    {
        foreach (var vector in Algorand["standards"]!.AsArray())
        {
            var methods = new JsonArray(Texts(vector!["methods"]).Select(name => (JsonNode)new JsonObject { ["name"] = name, ["args"] = new JsonArray(), ["returns"] = new JsonObject { ["type"] = "void" } }).ToArray());
            var abi = new JsonObject { ["methods"] = methods }.ToJsonString();
            Assert.Equal(Texts(vector["expected"]), Service.DetectStandards(abi));
            Assert.Equal(Texts(vector["expected"]), AlgorandService().GetContract(abi, "1").Standards);
        }
    }

    [Fact]
    public void EncodesNamedParametersAsApplicationArguments()
    {
        var vectors = Algorand["contract"]!;
        var contract = AlgorandService().GetContract(vectors["abi"]!.ToJsonString(), Text(vectors["address"]));
        foreach (var vector in vectors["encoded"]!.AsArray())
            Assert.Equal(Text(vector!["data"]), contract.Encode(Text(vector["method"]), vector["parameters"]!.DeepClone().AsObject()));
        foreach (var vector in vectors["invalid"]!.AsArray())
            Assert.Equal("INVALID_ARGUMENT", Assert.Throws<Web3Exception>(() => contract.Encode(Text(vector!["method"]), vector["parameters"]!.DeepClone().AsObject())).Code);
        Assert.Equal("METHOD_NOT_FOUND", Assert.Throws<Web3Exception>(() => contract.Encode("missing")).Code);
    }

    [Fact]
    public async Task ReadsBitcoinThroughEsplora()
    {
        using var esplora = EsploraServer.Start();
        var service = new Service(esplora.Provider());
        Assert.Equal(Bitcoin["check"]!.ToJsonString(), JsonSerializer.Serialize(await service.CheckAsync()));
        var txid = Text(Bitcoin["esplora"]!["transaction"]!["txid"]);
        Assert.Equal(Canonical(Bitcoin["details"]), Canonical(JsonSerializer.SerializeToNode(await service.GetTransactionAsync(txid))));
        var receipt = await service.GetTransactionReceiptAsync(txid, new TransactionResult { Name = "lookup", RequestedAt = "1", RejectedAt = "2" });
        Assert.Equal(Canonical(Bitcoin["receipt"]), Canonical(JsonSerializer.SerializeToNode(receipt)));
        Assert.Equal("TRANSACTION_NOT_FOUND", (await Assert.ThrowsAsync<Web3Exception>(() => service.GetTransactionAsync(new string('0', 64)))).Code);
        Assert.Equal("CHAIN_ID_MISMATCH", (await Assert.ThrowsAsync<Web3Exception>(() => new Service(esplora.Provider("testnet")).CheckAsync())).Code);
    }

    /// <summary>.NET only: the transactions are encoded and signed natively, so they must match algosdk byte for byte.</summary>
    [Fact]
    public void SignsAlgorandTransactionsLikeAlgosdk()
    {
        var seed = Convert.FromHexString(Text(Algorand["wallets"]![0]!["privateKey"])[2..]);
        foreach (var vector in Algorand["signedTransactions"]!["transactions"]!.AsArray())
        {
            var transaction = AlgorandTransactionVectors.Build(vector!["fields"]!);
            Assert.Equal(Text(vector["txId"]), transaction.Id);
            Assert.Equal(Text(vector["signed"]), "0x" + Convert.ToHexString(transaction.Sign(seed)).ToLowerInvariant());
        }
        var group = Algorand["signedTransactions"]!["group"]!;
        var grouped = UnsignedTransaction.AssignGroup(group["transactions"]!.AsArray().Select(fields => AlgorandTransactionVectors.Build(fields!)).ToList());
        Assert.Equal(Text(group["groupId"]), Convert.ToBase64String(grouped[0].Group!));
        Assert.Equal(Texts(group["txIds"]), grouped.Select(item => item.Id));
        Assert.Equal(Text(Algorand["applicationAddress"]!["address"]), AlgorandAddress.OfApplication(ulong.Parse(Text(Algorand["applicationAddress"]!["appId"]))));
    }

    private static string Canonical(JsonNode? node) => ParityScenario.Canonical(node, new HashSet<string>())!.ToJsonString();

    /// <summary>A provider of a blockchain the service does not support.</summary>
    private sealed class SolanaProvider : IBlockchainProvider
    {
        public string RpcURI => "http://localhost";
        public string? RpcPublicURI => null;
        public string? ChainId => null;
        public string? ChainName => null;
        public NativeCurrency? NativeCurrency => null;
        public string? BlockExplorerURI => null;
    }
}
