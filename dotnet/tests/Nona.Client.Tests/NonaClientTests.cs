using System.Net;
using Nona.Client;

namespace Nona.Client.Tests;

public sealed class NonaClientTests
{
    [Fact]
    public async Task GetConfigValueAsync_SendsApiKeyAndParsesValue()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            {"value":"enabled","contentType":"string"}
            """));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://nona.test/")
        };

        using var client = new NonaClient(httpClient, new NonaClientOptions
        {
            ApiKey = "api-key"
        });

        var value = await client.GetConfigValueAsync("production", "Features:Checkout");

        Assert.Equal("enabled", value.Value);
        Assert.Equal("string", value.ContentType);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://nona.test/api/production/Features%3ACheckout", request.Uri.AbsoluteUri);
        Assert.Equal("api-key", request.GetHeader("X-Api-Key"));
    }

    [Fact]
    public async Task TryGetConfigValueAsync_ReturnsNullForNotFound()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """{"error":"Config entry not found"}""",
            HttpStatusCode.NotFound));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://nona.test/")
        };

        using var client = new NonaClient(httpClient, new NonaClientOptions
        {
            ApiKey = "api-key"
        });

        var value = await client.TryGetConfigValueAsync("production", "missing");

        Assert.Null(value);
    }

    [Fact]
    public async Task GetStringValueAsync_ReturnsRawConfigValue()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            {"value":"enabled","contentType":"string"}
            """));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://nona.test/")
        };

        using var client = new NonaClient(httpClient, new NonaClientOptions
        {
            ApiKey = "api-key"
        });

        Assert.Equal("enabled", await client.GetStringValueAsync("production", "flag"));
    }

    [Fact]
    public async Task GetJsonValueAsync_DeserializesConfigValue()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            {"value":"{\"enabled\":true}","contentType":"json"}
            """));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://nona.test/")
        };

        using var client = new NonaClient(httpClient, new NonaClientOptions
        {
            ApiKey = "api-key"
        });

        var value = await client.GetJsonValueAsync<JsonFlag>("production", "settings");

        Assert.NotNull(value);
        Assert.True(value.Enabled);
    }

    [Fact]
    public async Task FailedRequest_ThrowsNonaClientExceptionWithServerError()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(
            """{"error":"Config entry not found"}""",
            HttpStatusCode.NotFound));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://nona.test/")
        };

        using var client = new NonaClient(httpClient, new NonaClientOptions
        {
            ApiKey = "api-key"
        });

        var ex = await Assert.ThrowsAsync<NonaClientException>(() =>
            client.GetConfigValueAsync("production", "missing"));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal("Config entry not found", ex.Message);
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handle;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handle)
        {
            _handle = handle;
        }

        public List<CapturedRequest> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase);

            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri ?? throw new InvalidOperationException("Request URI was not set."),
                headers));

            return Task.FromResult(_handle(request));
        }
    }

    private sealed class CapturedRequest
    {
        public CapturedRequest(
            HttpMethod method,
            Uri uri,
            IReadOnlyDictionary<string, string[]> headers)
        {
            Method = method;
            Uri = uri;
            Headers = headers;
        }

        public HttpMethod Method { get; }

        public Uri Uri { get; }

        public IReadOnlyDictionary<string, string[]> Headers { get; }

        public string? GetHeader(string name)
        {
            return Headers.TryGetValue(name, out var values) ? values.SingleOrDefault() : null;
        }
    }

    private sealed class JsonFlag
    {
        public bool Enabled { get; set; }
    }
}
