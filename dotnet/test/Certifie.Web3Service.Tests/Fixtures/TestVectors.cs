using System.Text.Json.Nodes;

namespace Certifie.Web3Service.Tests;

/// <summary>The expected values shared with the TypeScript tests (shared/test-vectors.json at the repository root).</summary>
public static class TestVectors
{
    public static JsonNode Root { get; } = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "test-vectors.json")))!;

    public static string Mnemonic => Text(Root["mnemonic"]);

    public static string Text(JsonNode? node) => node!.GetValue<string>();

    public static IEnumerable<string> Texts(JsonNode? node) => node!.AsArray().Select(Text);
}
