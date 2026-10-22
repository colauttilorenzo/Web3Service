using System.Text.Json.Nodes;
using Certifie.Web3;

namespace Certifie.Web3Service.Tests;

/// <summary>Local Ganache chain started by <c>npm run test:dotnet</c>; the same Notarizer contract as the TypeScript tests (Chain.mjs).</summary>
public sealed class Chain
{
    public Chain()
    {
        var port = Environment.GetEnvironmentVariable("WEB3_TEST_RPC_PORT")
            ?? throw new InvalidOperationException("Run the .NET tests with `npm run test:dotnet`, which starts the local chain.");
        Http = $"http://127.0.0.1:{port}";
        WebSocket = $"ws://127.0.0.1:{port}";
        var artifact = JsonNode.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("WEB3_TEST_ARTIFACT")!))!;
        Abi = artifact["abi"]!.ToJsonString();
        Bytecode = artifact["bytecode"]!.GetValue<string>();
    }

    public string Http { get; }
    public string WebSocket { get; }
    public string Mnemonic => TestVectors.Mnemonic;
    public string Abi { get; }
    public string Bytecode { get; }

    public EvmProvider Provider(string? rpcURI = null) => new() { RpcURI = rpcURI ?? Http, ChainId = "1337" };

    public Web3ServiceOptions Signer() => new() { Mnemonic = Mnemonic, PollingInterval = TimeSpan.FromMilliseconds(100) };
}
