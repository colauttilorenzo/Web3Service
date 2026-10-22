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

    /// <summary>Notarization is a call to mint(address,string,string,string) on the test contract, whose own address is the recipient.</summary>
    private static object Notarization(string address, char letter, string uri = "") =>
        new { recipient = address, hash = new string(letter, 64), algorithm = "sha2", uri };

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
        var deployed = await service.DeployAsync(_chain.Abi, _chain.Bytecode, new object[] { Text(Root["contract"]!["label"]) },
            new TransactionResult { RequestedAt = "123", RejectedAt = "999" });
        Assert.Equal(("deploy", "1", "0", "123"), (deployed.Name, deployed.Tx!.Status, deployed.Tx.Type, deployed.RequestedAt));
        Assert.Null(deployed.RejectedAt);
        Assert.Matches("^\\d+$", deployed.CompletedAt);
        var address = deployed.Tx.ContractAddress!;
        Assert.Equal(Text(Root["contract"]!["label"]), (await service.CallContractMethodAsync("label", _chain.Abi, address))!.GetValue<string>());

        var hash = new string('a', 64);
        var notarized = Notarization(address, 'a', "ipfs://example");
        var minted = await service.GetContract(_chain.Abi, address).SendAsync("mint", notarized);
        Assert.Equal(("mint", "1"), (minted.Name, minted.Tx!.Status));
        Assert.Equal("0", minted.Tx.Logs[0].LogIndex);
        Assert.Equal((BigInteger.Parse(minted.Tx.GasUsed) * BigInteger.Parse(minted.Tx.EffectiveGasPrice!)).ToString(), minted.NativeFee!.Amount);
        Assert.Equal(("ETH", "18"), (minted.NativeFee.Symbol, minted.NativeFee.Decimals));

        var metadata = await service.CallContractMethodAsync("metadata", _chain.Abi, address, new object[] { hash });
        Assert.Equal(new JsonArray(hash, "sha2", "ipfs://example").ToJsonString(), metadata!.ToJsonString());
        Assert.Equal(Root["contract"]!["numbers"]!.ToJsonString(), (await service.CallContractMethodAsync("numbers", _chain.Abi, address))!.ToJsonString());

        var confirmed = await service.GetTransactionReceiptAsync(minted.Tx.TransactionHash, new TransactionResult { RejectedAt = "1" });
        Assert.Equal(minted.NativeFee.Amount, confirmed.NativeFee!.Amount);
        Assert.Null(confirmed.RejectedAt);
        AssertIntegerStrings(JsonSerializer.SerializeToNode(confirmed));

        Assert.Equal("TRANSACTION_NOT_FOUND", (await Assert.ThrowsAsync<Web3Exception>(() => service.GetTransactionReceiptAsync("0x" + new string('0', 64)))).Code);
        Assert.Equal("METHOD_NOT_FOUND", (await Assert.ThrowsAsync<Web3Exception>(() => service.CallContractMethodAsync("missing", _chain.Abi, address))).Code);
        Assert.Equal("SIGNER_REQUIRED", (await Assert.ThrowsAsync<Web3Exception>(() => new Service(_chain.Provider()).GetContract(_chain.Abi, address).SendAsync("mint", notarized))).Code);
    }

    [Fact]
    public async Task ConcurrentWritesUseDistinctNoncesAndRevertsAreReported()
    {
        var service = new Service(_chain.Provider(), _chain.Signer());
        var address = (await service.DeployAsync(_chain.Abi, _chain.Bytecode, new object[] { "Nonces" })).Tx!.ContractAddress!;
        var contract = service.GetContract(_chain.Abi, address);
        var results = await Task.WhenAll(new[] { 'b', 'c' }.Select(letter => contract.SendAsync("mint", Notarization(address, letter))));
        Assert.All(results, result => Assert.Equal("1", result.Tx!.Status));

        var failure = await Assert.ThrowsAsync<Web3Exception>(() => contract.SendAsync("mint", Notarization(address, 'b'), new TransactionOptions { Gas = 300_000 }));
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

    private static readonly string Holder = Text(Root["wallets"]![0]!["address"]);
    private static readonly string Recipient = Text(Root["wallets"]![1]!["address"]);

    [Fact]
    public async Task CallsContractFunctionsWithNamedParameters()
    {
        var service = new Service(_chain.Provider(), _chain.Signer());
        var token = await _chain.DeployContractAsync(service, "TestToken", new object[] { "Coin", "CN", 1_000_000 });
        Assert.Equal(new[] { "ERC-20", "ERC-173" }, token.Standards);
        Assert.Equal("payable", token.Methods.First(method => method.Name == "deposit").Kind);
        Assert.Equal(Holder, await token.OwnerAsync());
        Assert.Equal("1000000", (await token.CallAsync("balanceOf", new { arg0 = Holder }))!.GetValue<string>());

        var transfer = (await token.CallAsync("transfer", new { to = Recipient, amount = 250 }))!;
        Assert.Equal(("transfer", "1"), (Text(transfer["name"]), Text(transfer["tx"]!["status"])));
        Assert.Equal("250", (await token.ReadAsync("balanceOf", new { arg0 = Recipient }))!.GetValue<string>());
        Assert.True((await token.ReadAsync("approve", new { spender = Recipient, amount = "1" }))!.GetValue<bool>());
        await token.SendAsync("deposit", new { note = "tip" }, new TransactionOptions { Value = 12345 });
        Assert.Equal("12345", (await token.ReadAsync("deposits"))!.GetValue<string>());
        var info = await token.GetTokenInfoAsync();
        Assert.Equal(("ERC-20", token.Address, "Coin", "CN", "18", "1000000"), (info.Standard, info.Address, info.Name, info.Symbol, info.Decimals, info.TotalSupply));

        var details = await token.GetTransactionAsync(Text(transfer["tx"]!["transactionHash"]));
        Assert.Equal(new JsonObject { ["name"] = "transfer", ["signature"] = "transfer(address,uint256)", ["parameters"] = new JsonObject { ["to"] = Recipient, ["amount"] = "250" } }.ToJsonString(),
            JsonSerializer.Serialize(details.Method));
        Assert.Equal((false, "0", Holder.ToLowerInvariant(), "1"), (details.Pending, details.Value, details.From.ToLowerInvariant(), details.Receipt!.Status));
        Assert.Equal(Text(transfer["nativeFee"]!["amount"]), details.NativeFee!.Amount);
        Assert.Matches("^\\d+$", details.Timestamp);
        Assert.Null((await service.GetTransactionAsync(Text(transfer["tx"]!["transactionHash"]))).Method);
        Assert.Null(await service.GetContract(_chain.Abi, token.Address).OwnerAsync());

        Assert.Equal("TRANSACTION_NOT_FOUND", (await Assert.ThrowsAsync<Web3Exception>(() => service.GetTransactionAsync("0x" + new string('0', 64)))).Code);
        Assert.Equal("INVALID_ARGUMENT", (await Assert.ThrowsAsync<Web3Exception>(() => token.CallAsync("transfer", new { to = Recipient }))).Code);
        Assert.Equal("NOT_SUPPORTED", (await Assert.ThrowsAsync<Web3Exception>(() => service.GetContract(_chain.Abi, token.Address).GetTokenInfoAsync())).Code);
    }

    [Fact]
    public async Task MintsNftsAndReadsTokenData()
    {
        var service = new Service(_chain.Provider(), _chain.Signer());
        var nft = await _chain.DeployContractAsync(service, "TestNft", new object[] { "Art", "ART" });
        Assert.Equal(new[] { "ERC-721", "ERC-721 Metadata", "ERC-165" }, nft.Standards);
        var minted = await nft.MintNftAsync(new { to = Holder, uri = "ipfs://art/1" });
        Assert.Equal(("ERC-721", "1", "safeMint", "1"), (minted.Standard, Assert.Single(minted.TokenIds), minted.Transaction.Name, minted.Transaction.Tx!.Status));
        var info = await nft.GetTokenInfoAsync("1");
        Assert.Equal(("ERC-721", nft.Address, "1", "Art", "ART", "1", Holder, "ipfs://art/1"),
            (info.Standard, info.Address, info.TokenId, info.Name, info.Symbol, info.TotalSupply, info.Owner, info.TokenURI));
        await nft.CallAsync("safeTransferFrom", new { from = Holder, to = Recipient, tokenId = "1" });
        Assert.Equal(Recipient, (await nft.ReadAsync("ownerOf", new { tokenId = 1 }))!.GetValue<string>());

        var multi = await _chain.DeployContractAsync(service, "TestMultiToken", new object[] { "ipfs://multi/{id}.json" });
        Assert.Equal(new[] { "ERC-1155", "ERC-1155 Metadata URI" }, multi.Standards);
        Assert.Equal(new[] { "7" }, (await multi.MintNftAsync(new { to = Holder, id = 7, amount = 3, arg3 = "0x" })).TokenIds);
        Assert.Equal(new[] { "8", "9" }, (await multi.MintNftAsync(new { to = Holder, ids = new[] { 8, 9 }, amounts = new[] { 1, 2 } })).TokenIds);
        Assert.Equal("[\"3\",\"2\"]", (await multi.ReadAsync("balanceOfBatch", new { accounts = new[] { Holder, Holder }, ids = new[] { 7, 9 } }))!.ToJsonString());
        Assert.Equal($"ipfs://multi/{"7".PadLeft(64, '0')}.json", (await multi.GetTokenInfoAsync("7")).TokenURI);

        Assert.Equal("INVALID_ARGUMENT", (await Assert.ThrowsAsync<Web3Exception>(() => nft.MintNftAsync(new { to = Holder }))).Code);
        Assert.Equal("NOT_SUPPORTED", (await Assert.ThrowsAsync<Web3Exception>(() => service.GetContract(_chain.Abi, nft.Address).MintNftAsync(new { }))).Code);
    }
}
