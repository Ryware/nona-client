using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nona.Client;

public sealed partial class NonaClient
{
    private const string ApiKeyHeaderName = "X-Api-Key";

    private async Task<NonaConfigValue> FetchConfigValueAsync(
        string path,
        CancellationToken cancellationToken)
    {
        return await SendAsync(HttpMethod.Get, path, cancellationToken).ConfigureAwait(false);
    }

    private async Task<NonaConfigValue> SendAsync(
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
            return DeserializeConfigValue(responseBody!);
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
}
