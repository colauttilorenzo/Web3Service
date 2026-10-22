using System.Text.Json;
using System.Text.Json.Nodes;
using Certifie.Web3;
using Service = Certifie.Web3.Web3Service;

namespace Certifie.Web3Service.Tests;

/// <summary>The same calls as typescript/test/fixtures/ParityScenario.mjs, so the two outputs can be compared field by field.</summary>
public static class ParityScenario
{
    public static async Task<JsonObject> RunAsync(Chain chain)
    {
        var service = new Service(chain.Provider(), chain.Signer());
        var deployed = await service.DeployAsync(chain.Abi, chain.Bytecode, new object[] { "Parity" }, new TransactionResult { Name = "parity", RequestedAt = "123" });
        var address = deployed.Tx!.ContractAddress!;
        var document = new NotarizationDocument { Hash = new NotarizationHash { Data = new string('f', 64), Algorithm = "sha2" }, Metadata = "ipfs://parity" };
        var minted = await service.MintAsync(chain.Abi, address, document, new TransactionResult { RequestedAt = "456" });
        var receipt = await service.GetTransactionReceiptAsync(minted.Tx!.TransactionHash, new TransactionResult { Name = "lookup", RequestedAt = "789" });
        return new JsonObject
        {
            ["deployed"] = JsonSerializer.SerializeToNode(deployed), ["minted"] = JsonSerializer.SerializeToNode(minted), ["receipt"] = JsonSerializer.SerializeToNode(receipt),
            ["label"] = await service.CallContractMethodAsync("label", chain.Abi, address),
            ["metadata"] = await service.CallContractMethodAsync("metadata", chain.Abi, address, new object[] { document.Hash.Data }),
            ["numbers"] = await service.CallContractMethodAsync("numbers", chain.Abi, address),
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
