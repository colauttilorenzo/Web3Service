using System.Numerics;
using System.Text.Json.Nodes;
using Certifie.Web3.Algorand;

namespace Certifie.Web3Service.Tests;

/// <summary>Builds the transactions of the signed-transaction vectors (fields as algosdk names them) with the service's own encoder.</summary>
public static class AlgorandTransactionVectors
{
    internal static UnsignedTransaction Build(JsonNode fields)
    {
        var parameters = SuggestedParams.FromJson(new JsonObject
        {
            ["last-round"] = (long)fields["firstValid"]!, ["min-fee"] = 1000, ["genesis-id"] = fields["genesisId"]!.GetValue<string>(),
            ["genesis-hash"] = fields["genesisHash"]!.GetValue<string>(),
        });
        var type = fields["type"]!.GetValue<string>();
        return new UnsignedTransaction(type, fields["sender"]!.GetValue<string>(), (long)fields["fee"]!, parameters, FieldsOf(type, fields));
    }

    private static MsgpackMap FieldsOf(string type, JsonNode fields) => type switch
    {
        "pay" => TransactionFields.Payment(fields["receiver"]!.GetValue<string>(), (long)fields["amount"]!),
        "acfg" => TransactionFields.AssetCreation(new AssetFields
        {
            AssetName = Text(fields, "assetName"), UnitName = Text(fields, "unitName"), Total = (long)fields["total"]!, Decimals = (int)fields["decimals"]!,
            Url = Text(fields, "url"),
        }, fields["manager"]!.GetValue<string>()),
        _ => TransactionFields.ApplicationCall((long)fields["appId"]!, Bytes(fields["appArgs"]), ResourcesOf(fields), CreationOf(fields)),
    };

    private static Resources ResourcesOf(JsonNode fields) => new(
        Items(fields["accounts"]).Select(account => account.GetValue<string>()).ToList(), Items(fields["apps"]).Select(app => (BigInteger)(long)app).ToList(),
        Items(fields["assets"]).Select(asset => (BigInteger)(long)asset).ToList(),
        Items(fields["boxes"]).Select(box => new BoxReference((long)box["appIndex"]!, Hex(box["name"]!))).ToList());

    private static Creation? CreationOf(JsonNode fields) => fields["approvalProgram"] == null ? null : new Creation
    {
        ApprovalProgram = Hex(fields["approvalProgram"]!), ClearProgram = Hex(fields["clearProgram"]!), GlobalInts = (int)fields["globalInts"]!,
        GlobalBytes = (int)fields["globalBytes"]!, LocalInts = (int)fields["localInts"]!, LocalBytes = (int)fields["localBytes"]!, ExtraPages = (int)fields["extraPages"]!,
    };

    private static List<byte[]> Bytes(JsonNode? values) => Items(values).Select(Hex).ToList();

    private static IEnumerable<JsonNode> Items(JsonNode? values) => values?.AsArray().OfType<JsonNode>() ?? Enumerable.Empty<JsonNode>();

    private static byte[] Hex(JsonNode value) => Convert.FromHexString(value.GetValue<string>()[2..]);

    private static string Text(JsonNode fields, string key) => fields[key]!.GetValue<string>();
}
