using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Certifie.Web3;
using Xunit;
using static Certifie.Web3Service.Tests.TestVectors;
using Service = Certifie.Web3.Web3Service;

namespace Certifie.Web3Service.Tests;

/// <summary>Mirrors typescript/test/UnitTests.test.mjs: same test names, same shared vectors.</summary>
public class UnitTests
{
    [Fact]
    public void AppliesEthereumDefaultsAndPartialOverrides()
    {
        var (defaults, custom) = (Root["defaults"]!, Root["customProvider"]!);
        var service = new Service("https://node.example");
        Assert.Equal(Text(defaults["chainId"]), service.ChainId);
        Assert.Equal(Text(defaults["chainName"]), service.Provider.ChainName);
        Assert.Equal((Text(defaults["currencyName"]), Text(defaults["symbol"]), (int)defaults["decimals"]!), (service.Provider.CurrencyName, service.Provider.Symbol, service.Provider.Decimals));
        var currency = new NativeCurrency { Symbol = Text(custom["symbol"]), Decimals = (int)custom["decimals"]! };
        var customized = new Service(new EvmProvider { RpcURI = Text(custom["rpcURI"]), ChainId = Text(custom["chainId"]), NativeCurrency = currency });
        Assert.Equal(Text(custom["normalizedChainId"]), customized.ChainId);
        Assert.Equal((Text(defaults["currencyName"]), currency.Symbol, currency.Decimals.Value), (customized.Provider.CurrencyName, customized.Provider.Symbol, customized.Provider.Decimals));
        Assert.Equal("0x0", new Service(new EvmProvider { RpcURI = "http://localhost", ChainId = "0" }).ChainId);
    }

    [Fact]
    public void NormalizesChainIdsAndValidatesInput()
    {
        foreach (var vector in Root["chainIds"]!.AsArray()) Assert.Equal(Text(vector!["hex"]), Service.NormalizeChainId(Text(vector["input"])));
        Assert.Equal("0x89", Service.NormalizeChainId(new BigInteger(137)));
        Assert.Null(Service.NormalizeChainId((string?)null));
        foreach (var id in Texts(Root["invalidChainIds"])) Assert.Equal("INVALID_ARGUMENT", Assert.Throws<Web3Exception>(() => Service.NormalizeChainId(id)).Code);
        foreach (var uri in Texts(Root["validUris"])) Assert.Equal(uri, new Service(uri).Provider.RpcURI);
        foreach (var uri in Texts(Root["invalidUris"])) Assert.Equal("INVALID_ARGUMENT", Assert.Throws<Web3Exception>(() => new Service(uri)).Code);
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<Web3Exception>(() => new Service("http://localhost", new Web3ServiceOptions { Timeout = TimeSpan.Zero })).Code);
        Assert.Equal(Text(Root["shrinkAddress"]!["output"]), Service.ShrinkAddress(Text(Root["shrinkAddress"]!["input"])));
    }

    [Fact]
    public void DerivesTheSharedTestVectorWallets()
    {
        foreach (var expected in Root["wallets"]!.AsArray())
        {
            var wallet = Service.GetWallet(Mnemonic, (int)expected!["index"]!, Text(expected["password"]));
            Assert.Equal((Text(expected["address"]), Text(expected["publicKey"]), Text(expected["privateKey"]), Text(expected["path"]), expected["index"]!.ToString()),
                (wallet.Address, wallet.PublicKey, wallet.PrivateKey, wallet.Path, wallet.Index));
        }
        Assert.Equal(Text(Root["wallets"]![0]!["privateKey"]), Service.GetPrivateKey(Mnemonic));
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<Web3Exception>(() => Service.GetWallet(Mnemonic, -1)).Code);
        Assert.Equal(12, Service.CreateMnemonic().Split(' ').Length);
    }

    [Fact]
    public void SerializedErrorsUseTheSharedShape()
    {
        var error = Root["error"]!;
        var exception = new Web3Exception(Text(error["code"]), Text(error["message"]), new Exception(Text(error["secret"])));
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        Assert.Equal(error["json"]!.ToJsonString(), JsonSerializer.Serialize(exception.ToInfo(), options));
    }

    /// <summary>One function per signature, as the standards vectors list them.</summary>
    private static string AbiOf(IEnumerable<string> signatures) => new JsonArray(signatures.Select(signature =>
    {
        var (name, types) = (signature[..signature.IndexOf('(')], signature[(signature.IndexOf('(') + 1)..^1]);
        var inputs = types.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(type => (JsonNode)new JsonObject { ["name"] = "", ["type"] = type });
        return (JsonNode)new JsonObject { ["type"] = "function", ["name"] = name, ["inputs"] = new JsonArray(inputs.ToArray()), ["outputs"] = new JsonArray() };
    }).ToArray()).ToJsonString();

    private static Web3Contract ContractOf(string abi) => new Service("http://localhost").GetContract(abi, Text(Root["contractAbi"]!["address"]));

    [Fact]
    public void DescribesContractMethodsWithJsonParameterTypes()
    {
        var vector = Root["contractAbi"]!;
        var contract = ContractOf(vector["abi"]!.ToJsonString());
        Assert.Equal(vector["methods"]!.ToJsonString(), JsonSerializer.Serialize(contract.Methods));
        Assert.Equal(Text(vector["address"]), contract.Address);
        Assert.Empty(contract.Standards);
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<Web3Exception>(() => new Service("http://localhost").GetContract(vector["abi"]!.ToJsonString(), "0x1234")).Code);
    }

    [Fact]
    public void DetectsErcStandardsFromTheAbi()
    {
        foreach (var vector in Root["standards"]!.AsArray())
        {
            var abi = AbiOf(Texts(vector!["functions"]));
            Assert.Equal(Texts(vector["expected"]), Service.DetectStandards(abi));
            Assert.Equal(Texts(vector["expected"]), ContractOf(abi).Standards);
        }
    }

    [Fact]
    public void EncodesNamedParametersInAbiOrder()
    {
        var contract = ContractOf(Root["contractAbi"]!["abi"]!.ToJsonString());
        foreach (var vector in Root["contractAbi"]!["encoded"]!.AsArray())
            Assert.Equal(Text(vector!["data"]), contract.Encode(Text(vector["method"]), vector["parameters"]!.DeepClone()));
        foreach (var vector in Root["contractAbi"]!["invalid"]!.AsArray())
            Assert.Equal("INVALID_ARGUMENT", Assert.Throws<Web3Exception>(() => contract.Encode(Text(vector!["method"]), vector["parameters"]!.DeepClone())).Code);
        Assert.Equal("METHOD_NOT_FOUND", Assert.Throws<Web3Exception>(() => contract.Encode("missing")).Code);
    }
}
