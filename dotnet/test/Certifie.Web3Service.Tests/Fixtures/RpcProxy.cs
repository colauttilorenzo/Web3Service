using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;

namespace Certifie.Web3Service.Tests;

/// <summary>
/// An HTTP JSON-RPC proxy in front of the chain that simulates faulty nodes; the TypeScript tests have the same fixture (RpcProxy.mjs).
/// Stall: never answers. LoseBroadcast: forwards eth_sendRawTransaction, then answers HTTP 500. LargeFees: rewrites receipt fees.
/// </summary>
public sealed class RpcProxy : IDisposable
{
    public enum Mode { Stall, LoseBroadcast, LargeFees }

    private static readonly HttpClient Forwarder = new();
    private readonly HttpListener _listener = new();
    private readonly string _target;
    private readonly Mode _mode;
    private int _broadcasts;

    private RpcProxy(string target, Mode mode)
    {
        _target = target;
        _mode = mode;
        Uri = $"http://127.0.0.1:{FreePort()}/";
        _listener.Prefixes.Add(Uri);
    }

    public string Uri { get; }

    public int Broadcasts => _broadcasts;

    public static RpcProxy Start(string target, Mode mode)
    {
        var proxy = new RpcProxy(target, mode);
        proxy._listener.Start();
        _ = Task.Run(proxy.ServeAsync);
        return proxy;
    }

    /// <summary>Accepts requests until the proxy is disposed; a stalled proxy reads them and never answers.</summary>
    private async Task ServeAsync()
    {
        try
        {
            while (true)
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                if (_mode != Mode.Stall) _ = Task.Run(() => HandleAsync(context));
            }
        }
        catch (Exception error) when (error is HttpListenerException || error is ObjectDisposedException) { /* Disposed. */ }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var body = await new StreamReader(context.Request.InputStream).ReadToEndAsync().ConfigureAwait(false);
        var payload = JsonNode.Parse(body)!;
        var forwarded = await Forwarder.PostAsync(_target, new StringContent(body, Encoding.UTF8, "application/json")).ConfigureAwait(false);
        var result = JsonNode.Parse(await forwarded.Content.ReadAsStringAsync().ConfigureAwait(false))!;
        var lost = _mode == Mode.LoseBroadcast && payload["method"]!.GetValue<string>() == "eth_sendRawTransaction";
        await RespondAsync(context, lost ? 500 : 200, Rewrite(payload, result)).ConfigureAwait(false);
    }

    private JsonNode Rewrite(JsonNode payload, JsonNode result)
    {
        var method = payload["method"]!.GetValue<string>();
        if (_mode == Mode.LoseBroadcast && method == "eth_sendRawTransaction")
        {
            Interlocked.Increment(ref _broadcasts);
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = payload["id"]!.DeepClone(), ["error"] = new JsonObject { ["code"] = -32603, ["message"] = "Lost response after submission" } };
        }
        if (_mode == Mode.LargeFees && method == "eth_getTransactionReceipt" && result["result"] is JsonObject receipt)
        {
            receipt["gasUsed"] = ToHex(TestVectors.Text(TestVectors.Root["largeFee"]!["gasUsed"]));
            receipt["effectiveGasPrice"] = ToHex(TestVectors.Text(TestVectors.Root["largeFee"]!["effectiveGasPrice"]));
        }
        return result;
    }

    private static async Task RespondAsync(HttpListenerContext context, int status, JsonNode body)
    {
        var bytes = Encoding.UTF8.GetBytes(body.ToJsonString());
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        context.Response.Close();
    }

    private static string ToHex(string decimalValue) => "0x" + BigInteger.Parse(decimalValue).ToString("x").TrimStart('0');

    private static int FreePort()
    {
        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    public void Dispose() => _listener.Close();
}
