using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Certifie.Web3
{
    /// <summary>A REST API answered with an error status; the message is the API's own, when it gives one.</summary>
    internal sealed class HttpStatusException : Exception
    {
        public HttpStatusException(HttpStatusCode status, string message) : base($"HTTP {(int)status}: {message}") => Status = status;

        public HttpStatusCode Status { get; }
    }

    /// <summary>A JSON REST API (algod, indexer, Esplora) at a base URI, with an optional token header. Every request is bounded by the timeout.</summary>
    internal sealed class RestClient
    {
        /// <summary>One client for every API: HttpClient pools connections and is meant to be shared, not created per request.</summary>
        private static readonly HttpClient Http = new HttpClient(Handler()) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        private readonly string _baseUri;
        private readonly TimeSpan _timeout;
        private readonly CancellationToken _cancellationToken;
        private readonly KeyValuePair<string, string>? _token;

        public RestClient(string baseUri, TimeSpan timeout, CancellationToken cancellationToken, string? tokenHeader = null, string? token = null)
        {
            (_baseUri, _timeout, _cancellationToken) = (baseUri.TrimEnd('/'), timeout, cancellationToken);
            if (!string.IsNullOrEmpty(tokenHeader) && !string.IsNullOrEmpty(token)) _token = new KeyValuePair<string, string>(tokenHeader!, token!);
        }

        public async Task<JsonNode?> GetJsonAsync(string path) => JsonNode.Parse(await GetTextAsync(path).ConfigureAwait(false));

        /// <summary>The response, or null when the API answers 404 Not Found.</summary>
        public async Task<JsonNode?> OptionalJsonAsync(string path)
        {
            try { return await GetJsonAsync(path).ConfigureAwait(false); }
            catch (HttpStatusException error) when (error.Status == HttpStatusCode.NotFound) { return null; }
        }

        public Task<string> GetTextAsync(string path) => SendAsync(HttpMethod.Get, path, null);

        public async Task<JsonNode?> PostAsync(string path, HttpContent body, string contentType)
        {
            body.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return JsonNode.Parse(await SendAsync(HttpMethod.Post, path, body).ConfigureAwait(false));
        }

        /// <summary>The response body. The timeout covers the request and the download of the body, which SendAsync buffers.</summary>
        private async Task<string> SendAsync(HttpMethod method, string path, HttpContent? body)
        {
            using var request = new HttpRequestMessage(method, $"{_baseUri}/{path.TrimStart('/')}") { Content = body };
            if (_token is { } token) request.Headers.Add(token.Key, token.Value);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
            timeout.CancelAfter(_timeout);
            using var response = await Http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return response.IsSuccessStatusCode ? text : throw new HttpStatusException(response.StatusCode, MessageOf(text));
        }

        /// <summary>The <c>message</c> of a JSON error body, else the body itself.</summary>
        private static string MessageOf(string body)
        {
            try { return JsonValues.Text(JsonNode.Parse(body)?["message"]) ?? body; }
            catch (JsonException) { return body; }
        }

        /// <summary>Pooled connections are renewed every few minutes, so a long-lived process follows DNS changes.</summary>
        private static HttpMessageHandler Handler() =>
#if NET5_0_OR_GREATER
            new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) };
#else
            new HttpClientHandler();
#endif
    }
}
