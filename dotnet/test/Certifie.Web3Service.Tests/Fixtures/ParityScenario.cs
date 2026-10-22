using System.Text.Json;
using System.Text.Json.Nodes;
using Certifie.Web3;
using Service = Certifie.Web3.Web3Service;

namespace Certifie.Web3Service.Tests;

/// <summary>The same calls as typescript/test/fixtures/ParityScenario.mjs, so the two outputs can be compared field by field.</summary>
public static class ParityScenario
{
    private static readonly string Recipient = TestVectors.Text(TestVectors.Root["wallets"]![1]!["address"]);

    public static async Task<JsonObject> RunAsync(Chain chain)
    {
        var service = new Service(chain.Provider(), chain.Signer());
        var result = await NotarizationAsync(service, chain);
        foreach (var (key, value) in await ContractsAsync(service, chain)) result[key] = value?.DeepClone();
        return result;
    }

    private static async Task<JsonObject> NotarizationAsync(Service service, Chain chain)
    {
        var deployed = await service.DeployAsync(chain.Abi, chain.Bytecode, new object[] { "Parity" }, new TransactionResult { Name = "parity", RequestedAt = "123" });
        var address = deployed.Tx!.ContractAddress!;
        var hash = new string('f', 64);
        var minted = await service.GetContract(chain.Abi, address).SendAsync("mint", new { recipient = address, hash, algorithm = "sha2", uri = "ipfs://parity" });
        minted.RequestedAt = "now";
        var receipt = await service.GetTransactionReceiptAsync(minted.Tx!.TransactionHash, new TransactionResult { Name = "lookup", RequestedAt = "789" });
        return new JsonObject
        {
            ["deployed"] = JsonSerializer.SerializeToNode(deployed), ["minted"] = JsonSerializer.SerializeToNode(minted), ["receipt"] = JsonSerializer.SerializeToNode(receipt),
            ["label"] = await service.CallContractMethodAsync("label", chain.Abi, address),
            ["metadata"] = await service.CallContractMethodAsync("metadata", chain.Abi, address, new object[] { hash }),
            ["numbers"] = await service.CallContractMethodAsync("numbers", chain.Abi, address),
        };
    }

    /// <summary>Transactions sent through Web3Contract are requested now, so their requestedAt is replaced before comparing.</summary>
    private static async Task<JsonObject> ContractsAsync(Service service, Chain chain)
    {
        var token = await chain.DeployContractAsync(service, "TestToken", new object[] { "Parity", "PAR", 1000 }, new TransactionResult { RequestedAt = "1" });
        var transfer = (await token.CallAsync("transfer", new { to = Recipient, amount = "10" }))!;
        transfer["requestedAt"] = "now";
        var nft = await chain.DeployContractAsync(service, "TestNft", new object[] { "Parity Art", "PART" }, new TransactionResult { RequestedAt = "1" });
        var nftMint = await nft.MintNftAsync(new { to = Recipient, uri = "ipfs://parity/1" });
        nftMint.Transaction.RequestedAt = "now";
        var multi = await chain.DeployContractAsync(service, "TestMultiToken", new object[] { "ipfs://parity/{id}.json" }, new TransactionResult { RequestedAt = "1" });
        var multiMint = await multi.MintNftAsync(new { to = Recipient, ids = new[] { 4, 5 }, amounts = new[] { 1, 2 } });
        return new JsonObject
        {
            ["notarizerStandards"] = JsonSerializer.SerializeToNode(Service.DetectStandards(chain.Abi)), ["tokenMethods"] = JsonSerializer.SerializeToNode(token.Methods),
            ["tokenStandards"] = JsonSerializer.SerializeToNode(token.Standards), ["tokenOwner"] = await token.OwnerAsync(), ["transfer"] = transfer,
            ["transferDetails"] = JsonSerializer.SerializeToNode(await token.GetTransactionAsync(transfer["tx"]!["transactionHash"]!.GetValue<string>())),
            ["tokenInfo"] = JsonSerializer.SerializeToNode(await token.GetTokenInfoAsync()), ["nftMint"] = JsonSerializer.SerializeToNode(nftMint),
            ["nftInfo"] = JsonSerializer.SerializeToNode(await nft.GetTokenInfoAsync("1")), ["multiMint"] = JsonSerializer.SerializeToNode(multiMint.TokenIds),
            ["multiInfo"] = JsonSerializer.SerializeToNode(await multi.GetTokenInfoAsync("5")),
        };
    }

    /// <summary>Sorted keys, no nulls, and the fields that differ between runs replaced by a placeholder.</summary>
    public static JsonNode? Canonical(JsonNode? node, ISet<string> variableFields) => node switch
    {
        JsonObject obj => new JsonObject(obj.Where(item => item.Value != null).OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => KeyValuePair.Create(item.Key, variableFields.Contains(item.Key) ? JsonValue.Create("<variable>") : Canonical(item.Value, variableFields)))),
        JsonArray array => new JsonArray(array.Select(item => Canonical(item, variableFields)).ToArray()),
        _ => node?.DeepClone(),
    };
}
