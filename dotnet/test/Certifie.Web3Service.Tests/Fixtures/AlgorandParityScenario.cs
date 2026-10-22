using System.Text.Json;
using System.Text.Json.Nodes;
using Certifie.Web3;
using Service = Certifie.Web3.Web3Service;

namespace Certifie.Web3Service.Tests;

/// <summary>The same calls as typescript/test/fixtures/AlgorandParityScenario.mjs, so the two outputs can be compared field by field.</summary>
public static class AlgorandParityScenario
{
    private static readonly TransactionOptions Funding = new() { Value = 1_000_000 };
    private static readonly string Recipient = TestVectors.Text(TestVectors.Root["algorand"]!["wallets"]![1]!["address"]);
    private static readonly string Notarizer = AlgorandNode.Contracts["Notarizer"];

    public static async Task<JsonObject> RunAsync(AlgorandNode node)
    {
        var service = new Service(node.Provider(), node.Signer());
        var result = await NotarizationAsync(service);
        foreach (var (key, value) in await TokensAsync(service)) result[key] = value?.DeepClone();
        return result;
    }

    private static async Task<JsonObject> NotarizationAsync(Service service)
    {
        var deployed = await service.DeployAsync(Notarizer, "", new object[] { "Parity" }, new TransactionResult { Name = "parity", RequestedAt = "123" }, Funding);
        var address = deployed.Tx!.ContractAddress!;
        var document = new NotarizationDocument { Hash = new NotarizationHash { Data = new string('f', 64), Algorithm = "sha2" }, Metadata = "ipfs://parity" };
        var minted = await service.MintAsync(Notarizer, address, document, new TransactionResult { RequestedAt = "456" });
        var receipt = await service.GetTransactionReceiptAsync(minted.Tx!.TransactionHash, new TransactionResult { Name = "lookup", RequestedAt = "789" });
        return new JsonObject
        {
            ["deployed"] = JsonSerializer.SerializeToNode(deployed), ["minted"] = JsonSerializer.SerializeToNode(minted), ["receipt"] = JsonSerializer.SerializeToNode(receipt),
            ["label"] = await service.CallContractMethodAsync("label", Notarizer, address),
            ["metadata"] = await service.CallContractMethodAsync("metadata", Notarizer, address, new object[] { document.Hash.Data }),
            ["numbers"] = await service.CallContractMethodAsync("numbers", Notarizer, address),
        };
    }

    /// <summary>Transactions sent through Web3Contract are requested now, so their requestedAt is replaced before comparing.</summary>
    private static async Task<JsonObject> TokensAsync(Service service)
    {
        var token = await DeployContractAsync(service, "TestToken", "Parity", "PAR", "1000");
        await token.SendAsync("mintInitialSupply");
        var transfer = (await token.CallAsync("arc200_transfer", new { to = Recipient, value = "10" }))!;
        transfer["requestedAt"] = "now";
        var nft = await DeployContractAsync(service, "TestNft");
        var nftMint = await nft.MintNftAsync(new { to = Recipient, uri = "ipfs://parity/1" });
        nftMint.Transaction.RequestedAt = "now";
        var asset = await service.CreateTokenAsync(new TokenDefinition { Name = "Parity Coin", Symbol = "PAR", TotalSupply = 1000, Decimals = 2 }, new TransactionResult { RequestedAt = "1" });
        return new JsonObject
        {
            ["notarizerStandards"] = JsonSerializer.SerializeToNode(Service.DetectStandards(Notarizer)), ["tokenMethods"] = JsonSerializer.SerializeToNode(token.Methods),
            ["tokenStandards"] = JsonSerializer.SerializeToNode(token.Standards), ["tokenOwner"] = await token.OwnerAsync(), ["transfer"] = transfer,
            ["transferDetails"] = JsonSerializer.SerializeToNode(await token.GetTransactionAsync(transfer["tx"]!["transactionHash"]!.GetValue<string>())),
            ["tokenInfo"] = JsonSerializer.SerializeToNode(await token.GetTokenInfoAsync()), ["nftMint"] = JsonSerializer.SerializeToNode(nftMint),
            ["nftInfo"] = JsonSerializer.SerializeToNode(await nft.GetTokenInfoAsync("1")), ["asset"] = JsonSerializer.SerializeToNode(asset),
            ["assetInfo"] = JsonSerializer.SerializeToNode(await service.GetTokenInfoAsync(asset.TokenId)),
        };
    }

    private static async Task<Web3Contract> DeployContractAsync(Service service, string name, params object[] constructorArguments)
    {
        var deployed = await service.DeployAsync(AlgorandNode.Contracts[name], "", constructorArguments, new TransactionResult { RequestedAt = "1" }, Funding);
        return service.GetContract(AlgorandNode.Contracts[name], deployed.Tx!.ContractAddress!);
    }
}
