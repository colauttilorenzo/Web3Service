using System.Text.Json.Nodes;
using Xunit;
using static Certifie.Web3Service.Tests.TestVectors;

namespace Certifie.Web3Service.Tests;

/// <summary>
/// Runs the parity scenario and compares it with the output of the TypeScript package for the same calls on the same chain,
/// written by <c>npm run test:dotnet</c> before the .NET tests start.
/// </summary>
[Collection(ChainCollection.Name)]
public class ParityTests
{
    private readonly Chain _chain;

    public ParityTests(Chain chain) => _chain = chain;

    [Fact]
    public async Task ProducesTheSameJsonAsTheTypeScriptPackage()
    {
        var path = Environment.GetEnvironmentVariable("WEB3_TEST_PARITY")
            ?? throw new InvalidOperationException("Run the .NET tests with `npm run test:dotnet`, which writes the TypeScript output.");
        var variableFields = new HashSet<string>(Texts(Root["parity"]!["variableFields"]));
        var typescript = ParityScenario.Canonical(JsonNode.Parse(File.ReadAllText(path)), variableFields)!.AsObject();
        var dotnet = ParityScenario.Canonical(await ParityScenario.RunAsync(_chain), variableFields)!.AsObject();
        foreach (var (key, expected) in typescript) Assert.Equal($"{key}: {expected!.ToJsonString()}", $"{key}: {dotnet[key]?.ToJsonString()}");
        Assert.Equal(typescript.Select(item => item.Key), dotnet.Select(item => item.Key));
    }
}
