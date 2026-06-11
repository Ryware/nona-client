using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Nona.Client;

public sealed class NonaClient : IDisposable
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly NonaClientOptions _options;

    public NonaClient(string baseAddress, string? apiKey = null)
        : this(new NonaClientOptions
        {
            BaseAddress = new Uri(baseAddress, UriKind.Absolute),
            ApiKey = apiKey
        })
    {
    }

    public NonaClient(Uri baseAddress, string? apiKey = null)
        : this(new NonaClientOptions
        {
            BaseAddress = baseAddress,
            ApiKey = apiKey
        })
    {
    }

    public NonaClient(NonaClientOptions options)
        : this(new HttpClient(), options, disposeHttpClient: true)
    {
    }

    public NonaClient(HttpClient httpClient)
        : this(httpClient, new NonaClientOptions(), disposeHttpClient: false)
    {
    }

    public NonaClient(HttpClient httpClient, NonaClientOptions options, bool disposeHttpClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _disposeHttpClient = disposeHttpClient;
        _jsonOptions = options.JsonSerializerOptions ?? CreateDefaultJsonOptions();

        if (_options.BaseAddress is not null)
        {
            _httpClient.BaseAddress = EnsureTrailingSlash(_options.BaseAddress);
        }
    }

    public string? ApiKey
    {
        get => _options.ApiKey;
        set => _options.ApiKey = value;
    }

    public async Task<NonaConfigValue> GetConfigValueAsync(
        string environmentId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var path = $"api/{Segment(environmentId, nameof(environmentId))}/{Segment(key, nameof(key))}";
        return await SendAsync<NonaConfigValue>(HttpMethod.Get, path, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NonaConfigValue?> TryGetConfigValueAsync(
        string environmentId,
        string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetConfigValueAsync(environmentId, key, cancellationToken).ConfigureAwait(false);
        }
        catch (NonaClientException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<string> GetStringValueAsync(
        string environmentId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var configValue = await GetConfigValueAsync(environmentId, key, cancellationToken).ConfigureAwait(false);
        return configValue.Value;
    }

    public async Task<T?> GetJsonValueAsync<T>(
        string environmentId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var configValue = await GetConfigValueAsync(environmentId, key, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(configValue.Value, _jsonOptions);
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        var responseBody = response.Content is null
            ? null
            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            ThrowResponseException(response, request, responseBody);
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            throw new NonaClientException(
                "Nona returned an empty response body.",
                response.StatusCode,
                method.Method,
                request.RequestUri,
                responseBody);
        }

        try
        {
            var result = JsonSerializer.Deserialize<T>(responseBody!, _jsonOptions);
            if (result is null)
            {
                throw new JsonException("The response JSON deserialized to null.");
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new NonaClientException(
                "Nona returned a response that could not be deserialized.",
                response.StatusCode,
                method.Method,
                request.RequestUri,
                responseBody,
                ex);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        ApplyApiKey(request);
        return request;
    }

    private void ApplyApiKey(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Nona API calls require NonaClient.ApiKey.");
        }

        request.Headers.TryAddWithoutValidation(ApiKeyHeaderName, ApiKey);
    }

    private Uri BuildUri(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri;
        }

        var baseAddress = _httpClient.BaseAddress;
        if (baseAddress is null)
        {
            throw new InvalidOperationException("NonaClient requires a BaseAddress on NonaClientOptions or HttpClient.");
        }

        return new Uri(EnsureTrailingSlash(baseAddress), path.TrimStart('/'));
    }

    private static void ThrowResponseException(
        HttpResponseMessage response,
        HttpRequestMessage request,
        string? responseBody)
    {
        var message = TryReadErrorMessage(responseBody);
        if (string.IsNullOrWhiteSpace(message))
        {
            message = $"Nona request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
        }

        throw new NonaClientException(
            message!,
            response.StatusCode,
            request.Method.Method,
            request.RequestUri,
            responseBody);
    }

    private static string? TryReadErrorMessage(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody!);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString();
                }

                if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string Segment(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        return Uri.EscapeDataString(value);
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var value = uri.ToString();
        return value.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(value + "/", UriKind.Absolute);
    }

    private static JsonSerializerOptions CreateDefaultJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
