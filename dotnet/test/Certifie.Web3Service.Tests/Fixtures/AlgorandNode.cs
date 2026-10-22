using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Certifie.Web3;
using Certifie.Web3.Algorand;
using Xunit;

namespace Certifie.Web3Service.Tests;

/// <summary>
/// The Algorand node of the Algorand tests, such as AlgoKit LocalNet; the same variables as typescript/test/fixtures/AlgorandNode.mjs.
/// <c>npm run test:dotnet</c> passes them, with the mnemonic of the LocalNet dispenser, when WEB3_TEST_ALGOD_URI is set.
/// </summary>
public sealed class AlgorandNode : IAsyncLifetime
{
    private static readonly string[] ContractNames = { "Notarizer", "TestToken", "TestNft", "TestAssetMinter" };

    /// <summary>Why the Algorand tests are skipped; null when a node is configured.</summary>
    public static string? SkipReason => Environment.GetEnvironmentVariable("WEB3_TEST_ALGOD_URI") != null && Environment.GetEnvironmentVariable("WEB3_TEST_ALGOD_MNEMONIC") != null
        ? null : "Set WEB3_TEST_ALGOD_URI and WEB3_TEST_ALGOD_MNEMONIC (npm run test:dotnet does) to run the Algorand tests.";

    /// <summary>Name → ARC-56 contract, compiled from shared/algorand/test_contracts.py.</summary>
    public static IReadOnlyDictionary<string, string> Contracts { get; } = ContractNames.ToDictionary(name => name,
        name => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "algorand", $"{name}.arc56.json")));

    public string Uri { get; } = Environment.GetEnvironmentVariable("WEB3_TEST_ALGOD_URI") ?? "";
    public string Token { get; } = Environment.GetEnvironmentVariable("WEB3_TEST_ALGOD_TOKEN") ?? new string('a', 64);
    public string Mnemonic { get; } = Environment.GetEnvironmentVariable("WEB3_TEST_ALGOD_MNEMONIC") ?? "";
    public string Address => AlgorandWallets.GetWallet(Mnemonic).Address;
    public string GenesisId { get; private set; } = "";

    public async Task InitializeAsync()
    {
        if (SkipReason != null) return;
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-Algo-API-Token", Token);
        GenesisId = (await http.GetFromJsonAsync<JsonObject>($"{Uri.TrimEnd('/')}/v2/transactions/params"))!["genesis-id"]!.GetValue<string>();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public AvmProvider Provider(string? rpcURI = null) => new() { RpcURI = rpcURI ?? Uri, Token = Token, ChainId = GenesisId };

    public Web3ServiceOptions Signer() => new() { Mnemonic = Mnemonic, PollingInterval = TimeSpan.FromMilliseconds(100) };
}

/// <summary>A test that needs the Algorand node; skipped when none is configured.</summary>
public sealed class AlgorandFactAttribute : FactAttribute
{
    public AlgorandFactAttribute() => Skip = AlgorandNode.SkipReason;
}

/// <summary>The Algorand tests share the node and its funded account.</summary>
[CollectionDefinition(Name)]
public sealed class AlgorandCollection : ICollectionFixture<AlgorandNode>
{
    public const string Name = "Algorand";
}
