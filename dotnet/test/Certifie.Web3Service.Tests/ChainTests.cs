using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Certifie.Web3;
using Xunit;
using static Certifie.Web3Service.Tests.TestVectors;
using Service = Certifie.Web3.Web3Service;

namespace Certifie.Web3Service.Tests;

/// <summary>Mirrors typescript/test/ChainTests.test.mjs: same test names, same chain, same assertions.</summary>
[Collection(ChainCollection.Name)]
public class ChainTests
{
    private readonly Chain _chain;

    public ChainTests(Chain chain) => _chain = chain;

    private static NotarizationDocument Document(char letter) => new() { Hash = new NotarizationHash { Data = new string(letter, 64), Algorithm = "sha2" } };

    private static void AssertIntegerStrings(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj: foreach (var item in obj) AssertIntegerStrings(item.Value); break;
            case JsonArray array: foreach (var item in array) AssertIntegerStrings(item); break;
            case JsonValue value: Assert.NotEqual(JsonValueKind.Number, value.GetValueKind()); break;
        }
    }

    [Fact]
    public async Task DeploysNotarizesReadsAndReturnsJsonSafeReceipts()
    {
        var service = new Service(_chain.Provider(), _chain.Signer());
        var deployed = await service.DeployAsync(_chain.Abi, _chain.Bytecode, new object[] { Text(Root["contract"]!["label"]) });
        Assert.Equal(("deploy", "1", "0"), (deployed.Name, deployed.Tx!.Status, deployed.Tx.Type));
        Assert.Matches("^\\d+$", deployed.CompletedAt);
        var address = deployed.Tx.ContractAddress!;
        Assert.Equal(Text(Root["contract"]!["label"]), (await service.CallContractMethodAsync("label", _chain.Abi, address))!.GetValue<string>());

        var notarized = Document('a');
        notarized.Metadata = "ipfs://example";
        var minted = await service.MintAsync(_chain.Abi, address, notarized, new TransactionResult { RequestedAt = "123", RejectedAt = "999" });
        Assert.Equal(("mint", "1", "123"), (minted.Name, minted.Tx!.Status, minted.RequestedAt));
        Assert.Null(minted.RejectedAt);
        Assert.Equal("0", minted.Tx.Logs[0].LogIndex);
        Assert.Equal((BigInteger.Parse(minted.Tx.GasUsed) * BigInteger.Parse(minted.Tx.EffectiveGasPrice!)).ToString(), minted.NativeFee!.Amount);
        Assert.Equal(("ETH", "18"), (minted.NativeFee.Symbol, minted.NativeFee.Decimals));

        var metadata = await service.CallContractMethodAsync("metadata", _chain.Abi, address, new object[] { notarized.Hash.Data });
        Assert.Equal(new JsonArray(notarized.Hash.Data, "sha2", "ipfs://example").ToJsonString(), metadata!.ToJsonString());
        Assert.Equal(Root["contract"]!["numbers"]!.ToJsonString(), (await service.CallContractMethodAsync("numbers", _chain.Abi, address))!.ToJsonString());

        var confirmed = await service.GetTransactionReceiptAsync(minted.Tx.TransactionHash, new TransactionResult { RejectedAt = "1" });
        Assert.Equal(minted.NativeFee.Amount, confirmed.NativeFee!.Amount);
        Assert.Null(confirmed.RejectedAt);
        AssertIntegerStrings(JsonSerializer.SerializeToNode(confirmed));

        Assert.Equal("TRANSACTION_NOT_FOUND", (await Assert.ThrowsAsync<Web3Exception>(() => service.GetTransactionReceiptAsync("0x" + new string('0', 64)))).Code);
        Assert.Equal("METHOD_NOT_FOUND", (await Assert.ThrowsAsync<Web3Exception>(() => service.CallContractMethodAsync("missing", _chain.Abi, address))).Code);
        Assert.Equal("SIGNER_REQUIRED", (await Assert.ThrowsAsync<Web3Exception>(() => new Service(_chain.Provider()).MintAsync(_chain.Abi, address, notarized))).Code);
    }

    [Fact]
    public async Task ConcurrentWritesUseDistinctNoncesAndRevertsAreReported()
    {
        var service = new Service(_chain.Provider(), _chain.Signer());
        var address = (await service.DeployAsync(_chain.Abi, _chain.Bytecode, new object[] { "Nonces" })).Tx!.ContractAddress!;
        var results = await Task.WhenAll(new[] { 'b', 'c' }.Select(letter => service.MintAsync(_chain.Abi, address, Document(letter))));
        Assert.All(results, result => Assert.Equal("1", result.Tx!.Status));

        var failure = await Assert.ThrowsAsync<Web3Exception>(() => service.MintAsync(_chain.Abi, address, Document('b'), options: new TransactionOptions { Gas = 300_000 }));
        Assert.Equal("TRANSACTION_REVERTED", failure.Code);
        Assert.Matches("^0x[0-9a-f]{64}$", failure.TransactionHash);
        var reverted = await service.GetTransactionReceiptAsync(failure.TransactionHash!, new TransactionResult { CompletedAt = "1" });
        Assert.Equal("0", reverted.Tx!.Status);
        Assert.Null(reverted.CompletedAt);
        Assert.Matches("^\\d+$", reverted.RejectedAt);
        Assert.Equal("TRANSACTION_REVERTED", reverted.Error!.Code);
        Assert.NotNull(reverted.NativeFee);
    }

    [Fact]
    public async Task PrivateKeySignerAndWebSocketUriWork()
    {
        var privateKey = Service.GetPrivateKey(_chain.Mnemonic);
        var service = new Service(_chain.Provider(_chain.WebSocket), new Web3ServiceOptions { PrivateKey = privateKey, PollingInterval = TimeSpan.FromMilliseconds(100) });
        var deployed = await service.DeployAsync(_chain.Abi, _chain.Bytecode, new object[] { "WebSocket" });
        Assert.Equal("1", deployed.Tx!.Status);
        Assert.Equal("WebSocket", (await service.CallContractMethodAsync("label", _chain.Abi, deployed.Tx.ContractAddress!))!.GetValue<string>());
        Assert.Matches("^\\d+$", (await service.CheckAsync()).GasPrice);
    }

    [Fact]
    public async Task FallsBackToTheSecondEndpointAndValidatesTheChain()
    {
        var fallback = new Service(new EvmProvider { RpcURI = "http://127.0.0.1:1", RpcPublicURI = _chain.Http, ChainId = "1337" });
        Assert.Matches("^\\d+$", (await fallback.CheckAsync()).BlockNumber);
        Assert.Equal("CHAIN_ID_MISMATCH", (await Assert.ThrowsAsync<Web3Exception>(() => new Service(_chain.Http).CheckAsync())).Code);
        Assert.Equal("PROVIDER_UNAVAILABLE", (await Assert.ThrowsAsync<Web3Exception>(() => new Service("http://127.0.0.1:1").CheckAsync())).Code);
    }

    [Fact]
    public async Task AStalledEndpointTimesOutAndFallsBack()
    {
        using var stalled = RpcProxy.Start(_chain.Http, RpcProxy.Mode.Stall);
        var service = new Service(new EvmProvider { RpcURI = stalled.Uri, RpcPublicURI = _chain.Http, ChainId = "1337" }, new Web3ServiceOptions { Timeout = TimeSpan.FromMilliseconds(300) });
        Assert.Matches("^\\d+$", (await service.CheckAsync()).BlockNumber);
    }

    [Fact]
    public async Task RecoversAMinedTransactionAfterALostBroadcastResponse()
    {
        using var proxy = RpcProxy.Start(_chain.Http, RpcProxy.Mode.LoseBroadcast);
        var service = new Service(_chain.Provider(proxy.Uri), _chain.Signer());
        var deployed = await service.DeployAsync(_chain.Abi, _chain.Bytecode, new object[] { "Recovered" });
        Assert.Equal("1", deployed.Tx!.Status);
        Assert.Equal(1, proxy.Broadcasts);
    }

    [Fact]
    public async Task ComputesExactFeesBeyondSafeIntegers()
    {
        var service = new Service(_chain.Provider(), _chain.Signer());
        var deployed = await service.DeployAsync(_chain.Abi, _chain.Bytecode, new object[] { "Fees" });
        using var proxy = RpcProxy.Start(_chain.Http, RpcProxy.Mode.LargeFees);
        var receipt = await new Service(_chain.Provider(proxy.Uri)).GetTransactionReceiptAsync(deployed.Tx!.TransactionHash);
        Assert.Equal(Text(Root["largeFee"]!["amount"]), receipt.NativeFee!.Amount);
    }
}
