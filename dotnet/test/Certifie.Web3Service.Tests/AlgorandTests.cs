using System.Text.Json;
using System.Text.Json.Nodes;
using Certifie.Web3;
using Certifie.Web3.Algorand;
using Xunit;
using static Certifie.Web3Service.Tests.TestVectors;
using Service = Certifie.Web3.Web3Service;

namespace Certifie.Web3Service.Tests;

/// <summary>
/// Mirrors typescript/test/AlgorandTests.test.mjs: same test names, same node, same assertions. The wallet signer exists only in
/// the JavaScript package (it signs in the browser), so .NET signs with a private key in both of its forms instead.
/// </summary>
[Collection(AlgorandCollection.Name)]
public class AlgorandTests
{
    /// <summary>Funds each application account for the minimum balance of its boxes.</summary>
    private static readonly TransactionOptions Funding = new() { Value = 1_000_000 };
    private static readonly string NotATransaction = new('A', 52);
    private static readonly string Notarizer = AlgorandNode.Contracts["Notarizer"];
    private static readonly string Recipient = Text(Root["algorand"]!["wallets"]![1]!["address"]);
    private readonly AlgorandNode _node;

    public AlgorandTests(AlgorandNode node) => _node = node;

    /// <summary>Notarization is a call to mint(address,string,string,string) on the test application, whose own account is the recipient.</summary>
    private static object Notarization(string address, char letter, string uri = "") =>
        new { recipient = AlgorandWallets.GetApplicationAddress(address), hash = new string(letter, 64), algorithm = "sha2", uri };

    private static async Task<Web3Contract> DeployContractAsync(Service service, string name, params object[] constructorArguments)
    {
        var deployed = await service.DeployAsync(AlgorandNode.Contracts[name], "", constructorArguments, null, Funding);
        return service.GetContract(AlgorandNode.Contracts[name], deployed.Tx!.ContractAddress!);
    }

    [AlgorandFact]
    public async Task AlgorandDeploysNotarizesReadsAndReturnsJsonSafeReceipts()
    {
        var service = new Service(_node.Provider(), _node.Signer());
        var deployed = await service.DeployAsync(Notarizer, "", new object[] { Text(Root["contract"]!["label"]) },
            new TransactionResult { RequestedAt = "123", RejectedAt = "999" }, Funding);
        Assert.Equal(("deploy", "1", "appl", "123", null), (deployed.Name, deployed.Tx!.Status, deployed.Tx.Type, deployed.RequestedAt, deployed.RejectedAt));
        Assert.Matches("^[0-9]+$", deployed.CompletedAt);
        var address = deployed.Tx.ContractAddress!;
        Assert.Matches("^[0-9]+$", address);
        Assert.Equal(Text(Root["contract"]!["label"]), (await service.CallContractMethodAsync("label", Notarizer, address))!.GetValue<string>());

        var hash = new string('a', 64);
        var notarized = Notarization(address, 'a', "ipfs://example");
        var minted = await service.GetContract(Notarizer, address).SendAsync("mint", notarized);
        Assert.Equal(("mint", "1"), (minted.Name, minted.Tx!.Status));
        Assert.Equal((1, "0", "0"), (minted.Tx.Logs.Count, minted.Tx.Logs[0].LogIndex, minted.Tx.GasUsed));
        Assert.Equal(("1000", "ALGO", "6"), (minted.NativeFee!.Amount, minted.NativeFee.Symbol, minted.NativeFee.Decimals));

        var metadata = await service.CallContractMethodAsync("metadata", Notarizer, address, new object[] { hash });
        Assert.Equal(new JsonArray(hash, "sha2", "ipfs://example").ToJsonString(), metadata!.ToJsonString());
        Assert.Equal(Root["algorand"]!["numbers"]!.ToJsonString(), (await service.CallContractMethodAsync("numbers", Notarizer, address))!.ToJsonString());

        var confirmed = await service.GetTransactionReceiptAsync(minted.Tx.TransactionHash, new TransactionResult { RejectedAt = "1" });
        Assert.Equal(("1000", null, minted.Tx.BlockNumber), (confirmed.NativeFee!.Amount, confirmed.RejectedAt, confirmed.Tx!.BlockNumber));

        Assert.Equal("TRANSACTION_NOT_FOUND", (await Assert.ThrowsAsync<Web3Exception>(() => service.GetTransactionReceiptAsync(NotATransaction))).Code);
        Assert.Equal("METHOD_NOT_FOUND", (await Assert.ThrowsAsync<Web3Exception>(() => service.CallContractMethodAsync("missing", Notarizer, address))).Code);
        Assert.Equal("SIGNER_REQUIRED", (await Assert.ThrowsAsync<Web3Exception>(() => new Service(_node.Provider()).GetContract(Notarizer, address).SendAsync("mint", notarized))).Code);
    }

    [AlgorandFact]
    public async Task AlgorandQueuesConcurrentWritesAndRejectsFailingCalls()
    {
        var service = new Service(_node.Provider(), _node.Signer());
        var address = (await service.DeployAsync(Notarizer, "", new object[] { "Queue" }, null, Funding)).Tx!.ContractAddress!;
        var contract = service.GetContract(Notarizer, address);
        var results = await Task.WhenAll(new[] { 'b', 'c' }.Select(letter => contract.SendAsync("mint", Notarization(address, letter))));
        Assert.Equal(new[] { "1", "1" }, results.Select(result => result.Tx!.Status));

        var failure = await Assert.ThrowsAsync<Web3Exception>(() => contract.SendAsync("mint", Notarization(address, 'b')));
        Assert.Equal("TRANSACTION_REVERTED", failure.Code);
        Assert.Matches("^[A-Z2-7]{52}$", failure.TransactionHash);
        Assert.Contains("already notarized", failure.Message);
        Assert.Equal("TRANSACTION_NOT_FOUND", (await Assert.ThrowsAsync<Web3Exception>(() => service.GetTransactionReceiptAsync(failure.TransactionHash!))).Code);
    }

    [AlgorandFact]
    public async Task AlgorandFallsBackToTheSecondEndpointAndValidatesTheNetwork()
    {
        var fallback = _node.Provider("http://127.0.0.1:1");
        fallback.RpcPublicURI = _node.Uri;
        var check = await new Service(fallback).CheckAsync();
        Assert.Matches("^[0-9]+$", check.BlockNumber);
        Assert.Equal("1000", check.GasPrice);
        var mainnet = _node.Provider();
        mainnet.ChainId = "mainnet-v1.0";
        Assert.Equal("CHAIN_ID_MISMATCH", (await Assert.ThrowsAsync<Web3Exception>(() => new Service(mainnet).CheckAsync())).Code);
        Assert.Equal("PROVIDER_UNAVAILABLE", (await Assert.ThrowsAsync<Web3Exception>(() => new Service(_node.Provider("http://127.0.0.1:1")).CheckAsync())).Code);
    }

    [AlgorandFact]
    public async Task AlgorandSignsWithAPrivateKey()
    {
        var seed = AlgorandWallets.GetWallet(_node.Mnemonic).PrivateKey;
        var secretKey = Convert.ToBase64String(Convert.FromHexString(seed[2..] + AlgorandWallets.GetWallet(_node.Mnemonic).PublicKey[2..]));
        foreach (var (privateKey, label) in new[] { (seed, "Seed"), (secretKey, "SecretKey") })
        {
            var service = new Service(_node.Provider(), new Web3ServiceOptions { PrivateKey = privateKey, PollingInterval = TimeSpan.FromMilliseconds(100) });
            var address = (await service.DeployAsync(Notarizer, "", new object[] { label })).Tx!.ContractAddress!;
            Assert.Equal(label, (await service.CallContractMethodAsync("label", Notarizer, address))!.GetValue<string>());
        }
        var both = new Web3ServiceOptions { PrivateKey = seed, Mnemonic = _node.Mnemonic };
        Assert.Equal("INVALID_ARGUMENT", (await Assert.ThrowsAsync<Web3Exception>(() => new Service(_node.Provider(), both).DeployAsync(Notarizer, "", new object[] { "No" }))).Code);
    }

    [AlgorandFact]
    public async Task AlgorandCallsContractMethodsWithNamedParameters()
    {
        var service = new Service(_node.Provider(), _node.Signer());
        var token = await DeployContractAsync(service, "TestToken", "Coin", "CN", "1000000");
        await token.SendAsync("mintInitialSupply");
        Assert.Equal(new[] { "ARC-200" }, token.Standards);
        Assert.Equal("payable", token.Methods.Single(method => method.Name == "deposit").Kind);
        Assert.Equal(_node.Address, await token.OwnerAsync());
        Assert.Equal("1000000", (await token.CallAsync("arc200_balanceOf", new { owner = _node.Address }))!.GetValue<string>());

        var transfer = (await token.CallAsync("arc200_transfer", new { to = Recipient, value = 250 }))!;
        Assert.Equal(("arc200_transfer", "1"), (transfer["name"]!.GetValue<string>(), transfer["tx"]!["status"]!.GetValue<string>()));
        Assert.Equal("250", (await token.ReadAsync("arc200_balanceOf", new { owner = Recipient }))!.GetValue<string>());
        Assert.True((await token.ReadAsync("arc200_approve", new { spender = Recipient, value = "1" }))!.GetValue<bool>());
        await token.SendAsync("deposit", new { note = "tip" }, new TransactionOptions { Value = 12345 });
        Assert.Equal("12345", (await token.ReadAsync("deposits"))!.GetValue<string>());
        var info = new TokenInfo { Standard = "ARC-200", Address = token.Address, Name = "Coin", Symbol = "CN", Decimals = "6", TotalSupply = "1000000" };
        Assert.Equal(JsonSerializer.Serialize(info), JsonSerializer.Serialize(await token.GetTokenInfoAsync()));

        var hash = transfer["tx"]!["transactionHash"]!.GetValue<string>();
        var details = await token.GetTransactionAsync(hash);
        var method = new { name = "arc200_transfer", signature = "arc200_transfer(address,uint256)bool", parameters = new { to = Recipient, value = "250" } };
        Assert.Equal(JsonSerializer.Serialize(method), JsonSerializer.Serialize(details.Method));
        Assert.Equal((false, "0", _node.Address, "1"), (details.Pending, details.Value, details.From, details.Receipt!.Status));
        Assert.Equal(transfer["nativeFee"]!["amount"]!.GetValue<string>(), details.NativeFee!.Amount);
        Assert.Matches("^[0-9]+$", details.Timestamp);
        Assert.Null((await service.GetTransactionAsync(hash)).Method);
        Assert.Null(await service.GetContract(Notarizer, token.Address).OwnerAsync());

        Assert.Equal("TRANSACTION_NOT_FOUND", (await Assert.ThrowsAsync<Web3Exception>(() => service.GetTransactionAsync(NotATransaction))).Code);
        Assert.Equal("INVALID_ARGUMENT", (await Assert.ThrowsAsync<Web3Exception>(() => token.CallAsync("arc200_transfer", new { to = Recipient }))).Code);
        Assert.Equal("NOT_SUPPORTED", (await Assert.ThrowsAsync<Web3Exception>(() => service.GetContract(Notarizer, token.Address).GetTokenInfoAsync())).Code);
    }

    [AlgorandFact]
    public async Task AlgorandMintsNftsAndReadsTokenData()
    {
        var service = new Service(_node.Provider(), _node.Signer());
        var nft = await DeployContractAsync(service, "TestNft");
        Assert.Equal(new[] { "ARC-72", "ARC-72 Enumeration", "ARC-72 Management", "ARC-73" }, nft.Standards);
        var minted = await nft.MintNftAsync(new { to = _node.Address, uri = "ipfs://art/1" });
        Assert.Equal(("ARC-72", "1", "arc72_mint", "1"), (minted.Standard, string.Join(",", minted.TokenIds), minted.Transaction.Name, minted.Transaction.Tx!.Status));
        var info = new TokenInfo { Standard = "ARC-72", Address = nft.Address, TokenId = "1", TotalSupply = "1", Owner = _node.Address, TokenURI = "ipfs://art/1" };
        Assert.Equal(JsonSerializer.Serialize(info), JsonSerializer.Serialize(await nft.GetTokenInfoAsync("1")));
        await nft.CallAsync("arc72_transferFrom", new { from_ = _node.Address, to = Recipient, tokenId = "1" });
        Assert.Equal(Recipient, (await nft.ReadAsync("arc72_ownerOf", new { tokenId = 1 }))!.GetValue<string>());
        Assert.True((await nft.ReadAsync("supportsInterface", new { interfaceId = "0x53f02a40" }))!.GetValue<bool>());

        var minter = await DeployContractAsync(service, "TestAssetMinter");
        var asset = await minter.MintNftAsync(new { uri = "ipfs://asset/1" });
        Assert.Equal(("ASA", "1"), (asset.Standard, asset.Transaction.Tx!.Status));
        var assetInfo = await service.GetTokenInfoAsync(asset.TokenIds[0]);
        Assert.Equal(("Minted", "MNT", "1", "ipfs://asset/1"), (assetInfo.Name, assetInfo.Symbol, assetInfo.TotalSupply, assetInfo.TokenURI));

        Assert.Equal("INVALID_ARGUMENT", (await Assert.ThrowsAsync<Web3Exception>(() => nft.MintNftAsync(new { to = _node.Address }))).Code);
        Assert.Equal("NOT_SUPPORTED", (await Assert.ThrowsAsync<Web3Exception>(() => service.GetContract(AlgorandNode.Contracts["TestToken"], nft.Address).MintNftAsync(new { }))).Code);
    }

    [AlgorandFact]
    public async Task AlgorandCreatesStandardAssets()
    {
        var service = new Service(_node.Provider(), _node.Signer());
        var token = new TokenDefinition { Name = "Certifie Coin", Symbol = "CRT", TotalSupply = 1_000_000, Decimals = 2, Uri = "https://certifie.example" };
        var created = await service.CreateTokenAsync(token, new TransactionResult { RequestedAt = "1" });
        Assert.Equal(("ASA", "createToken", "1", "acfg"), (created.Standard, created.Transaction.Name, created.Transaction.Tx!.Status, created.Transaction.Tx.Type));
        var info = new TokenInfo
        {
            Standard = "ASA", Address = created.TokenId, Name = token.Name, Symbol = token.Symbol, Decimals = "2", TotalSupply = "1000000", TokenURI = token.Uri, Creator = _node.Address,
        };
        Assert.Equal(JsonSerializer.Serialize(info), JsonSerializer.Serialize(await service.GetTokenInfoAsync(created.TokenId)));

        Assert.Equal("TOKEN_NOT_FOUND", (await Assert.ThrowsAsync<Web3Exception>(() => service.GetTokenInfoAsync("999999999"))).Code);
        token.Symbol = "TOO-LONG-SYMBOL";
        Assert.Equal("INVALID_ARGUMENT", (await Assert.ThrowsAsync<Web3Exception>(() => service.CreateTokenAsync(token))).Code);
    }
}
