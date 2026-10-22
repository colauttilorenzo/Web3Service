using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Certifie.Web3;

namespace Certifie.Web3Service.Tests;

/// <summary>A Bitcoin Esplora API serving the responses of the shared vectors; the TypeScript tests have the same fixture (EsploraServer.mjs).</summary>
public sealed class EsploraServer : IDisposable
{
    private static readonly JsonNode Esplora = TestVectors.Root["bitcoin"]!["esplora"]!;
    private readonly HttpListener _listener = new();
    private readonly Dictionary<string, JsonNode> _routes;

    private EsploraServer(int port)
    {
        Uri = $"http://127.0.0.1:{port}";
        var transaction = Esplora["transaction"]!;
        _routes = new Dictionary<string, JsonNode>
        {
            ["/block-height/0"] = Esplora["genesis"]!, ["/blocks/tip/height"] = Esplora["tipHeight"]!, ["/fee-estimates"] = Esplora["feeEstimates"]!,
            [$"/tx/{transaction["txid"]}"] = transaction, [$"/block/{transaction["status"]!["block_hash"]}/txids"] = Esplora["blockTxids"]!,
        };
        _listener.Prefixes.Add($"{Uri}/");
    }

    public string Uri { get; }

    public static EsploraServer Start()
    {
        var server = new EsploraServer(FreePort());
        server._listener.Start();
        _ = Task.Run(server.ServeAsync);
        return server;
    }

    public BitcoinProvider Provider(string? chainId = null) => new() { RpcURI = Uri, ChainId = chainId ?? Esplora["chainId"]!.GetValue<string>() };

    public void Dispose() => _listener.Close();

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception) { return; }
            Respond(context.Response, _routes.GetValueOrDefault(context.Request.RawUrl ?? ""));
        }
    }

    /// <summary>Text resources are answered as plain text, the others as JSON.</summary>
    private static void Respond(HttpListenerResponse response, JsonNode? body)
    {
        var text = body == null ? "Not found" : body is JsonValue value && value.TryGetValue<string>(out var plain) ? plain : body.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(text);
        (response.StatusCode, response.ContentType) = (body == null ? 404 : 200, body is JsonValue ? "text/plain" : "application/json");
        response.OutputStream.Write(bytes);
        response.Close();
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
