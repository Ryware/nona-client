using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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

    public NonaClient(string baseAddress, string? apiKey = null, string? bearerToken = null)
        : this(new NonaClientOptions
        {
            BaseAddress = new Uri(baseAddress, UriKind.Absolute),
            ApiKey = apiKey,
            BearerToken = bearerToken
        })
    {
    }

    public NonaClient(Uri baseAddress, string? apiKey = null, string? bearerToken = null)
        : this(new NonaClientOptions
        {
            BaseAddress = baseAddress,
            ApiKey = apiKey,
            BearerToken = bearerToken
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

    public string? BearerToken
    {
        get => _options.BearerToken;
        set => _options.BearerToken = value;
    }

    public async Task<NonaConfigValue> GetConfigValueAsync(
        string environmentId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var path = $"api/{Segment(environmentId, nameof(environmentId))}/{Segment(key, nameof(key))}";
        return await SendAsync<NonaConfigValue>(
            HttpMethod.Get,
            path,
            body: null,
            AuthMode.ApiKey,
            cancellationToken).ConfigureAwait(false);
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

    public async Task<NonaLoginResponse> LoginAsync(
        string email,
        string password,
        bool storeToken = true,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<NonaLoginResponse>(
            HttpMethod.Post,
            "auth/login",
            new NonaLoginRequest(email, password),
            AuthMode.None,
            cancellationToken).ConfigureAwait(false);

        if (storeToken)
        {
            BearerToken = response.Token;
        }

        return response;
    }

    public async Task<NonaRegisterResult> RegisterAsync(
        string email,
        string password,
        bool storeToken = true,
        CancellationToken cancellationToken = default)
    {
        var result = await SendAsync<NonaRegisterResult>(
            HttpMethod.Post,
            "auth/register",
            new NonaLoginRequest(email, password),
            AuthMode.None,
            cancellationToken).ConfigureAwait(false);

        if (storeToken && result.Response is not null)
        {
            BearerToken = result.Response.Token;
        }

        return result;
    }

    public Task<bool> AnyUsersExistAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync<bool>(
            HttpMethod.Get,
            "auth/first-time",
            body: null,
            AuthMode.None,
            cancellationToken);
    }

    public Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        return SendNoContentAsync(
            HttpMethod.Post,
            "auth/forgot-password",
            new NonaRequestPasswordResetRequest(email),
            AuthMode.None,
            cancellationToken);
    }

    public Task<IReadOnlyList<NonaProject>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync<IReadOnlyList<NonaProject>>(
            HttpMethod.Get,
            "admin/projects",
            body: null,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task<NonaProject> CreateProjectAsync(string name, CancellationToken cancellationToken = default)
    {
        return SendAsync<NonaProject>(
            HttpMethod.Post,
            "admin/projects",
            new NonaCreateProjectRequest(name),
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        return SendNoContentAsync(
            HttpMethod.Delete,
            $"admin/projects/{Segment(projectId, nameof(projectId))}",
            body: null,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task<NonaProject> RerollApiKeysAsync(
        string projectId,
        string keyType,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<NonaProject>(
            HttpMethod.Post,
            $"admin/projects/{Segment(projectId, nameof(projectId))}/reroll-keys",
            new NonaRerollApiKeysRequest(keyType),
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task<IReadOnlyList<NonaApiKey>> ListApiKeysAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<IReadOnlyList<NonaApiKey>>(
            HttpMethod.Get,
            $"admin/projects/{Segment(projectId, nameof(projectId))}/api-keys",
            body: null,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task<NonaApiKey> CreateApiKeyAsync(
        string projectId,
        string name,
        string? environment = null,
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        return CreateApiKeyAsync(
            projectId,
            new NonaCreateApiKeyRequest(name, environment, scope),
            cancellationToken);
    }

    public Task<NonaApiKey> CreateApiKeyAsync(
        string projectId,
        NonaCreateApiKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<NonaApiKey>(
            HttpMethod.Post,
            $"admin/projects/{Segment(projectId, nameof(projectId))}/api-keys",
            request,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task DeleteApiKeyAsync(
        string projectId,
        long apiKeyId,
        CancellationToken cancellationToken = default)
    {
        return SendNoContentAsync(
            HttpMethod.Delete,
            $"admin/projects/{Segment(projectId, nameof(projectId))}/api-keys/{apiKeyId}",
            body: null,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task<NonaDashboardCounts> GetDashboardCountsAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync<NonaDashboardCounts>(
            HttpMethod.Get,
            "admin/dashboard/counts",
            body: null,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task<IReadOnlyList<NonaAuditLog>> ListAuditLogsAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync<IReadOnlyList<NonaAuditLog>>(
            HttpMethod.Get,
            "admin/audit-logs",
            body: null,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task<IReadOnlyList<NonaUser>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync<IReadOnlyList<NonaUser>>(
            HttpMethod.Get,
            "admin/users",
            body: null,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task<NonaUser> GetUserAsync(long id, CancellationToken cancellationToken = default)
    {
        return SendAsync<NonaUser>(
            HttpMethod.Get,
            $"admin/users/{id}",
            body: null,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task<NonaUser> CreateUserAsync(
        string name,
        string email,
        string? role = null,
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        return CreateUserAsync(
            new NonaCreateUserRequest(name, email, role, scope),
            cancellationToken);
    }

    public Task<NonaUser> CreateUserAsync(
        NonaCreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<NonaUser>(
            HttpMethod.Post,
            "admin/users",
            request,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task<NonaUser> UpdateUserAsync(
        long id,
        string name,
        string? role = null,
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        return UpdateUserAsync(
            id,
            new NonaUpdateUserRequest(name, role, scope),
            cancellationToken);
    }

    public Task<NonaUser> UpdateUserAsync(
        long id,
        NonaUpdateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<NonaUser>(
            HttpMethod.Put,
            $"admin/users/{id}",
            request,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task DeleteUserAsync(long id, CancellationToken cancellationToken = default)
    {
        return SendNoContentAsync(
            HttpMethod.Delete,
            $"admin/users/{id}",
            body: null,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task<IReadOnlyList<NonaProjectAccess>> GetUserProjectsAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<IReadOnlyList<NonaProjectAccess>>(
            HttpMethod.Get,
            $"admin/users/{id}/projects",
            body: null,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task<NonaProjectAccess> SetProjectAccessAsync(
        long id,
        string projectName,
        string role,
        CancellationToken cancellationToken = default)
    {
        return SetProjectAccessAsync(
            id,
            projectName,
            new NonaProjectAccessRequest(role),
            cancellationToken);
    }

    public Task<NonaProjectAccess> SetProjectAccessAsync(
        long id,
        string projectName,
        NonaProjectAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<NonaProjectAccess>(
            HttpMethod.Put,
            $"admin/users/{id}/projects/{Segment(projectName, nameof(projectName))}",
            request,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task RemoveProjectAccessAsync(
        long id,
        string projectName,
        CancellationToken cancellationToken = default)
    {
        return SendNoContentAsync(
            HttpMethod.Delete,
            $"admin/users/{id}/projects/{Segment(projectName, nameof(projectName))}",
            body: null,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task<IReadOnlyList<NonaEnvironment>> ListEnvironmentsAsync(
        string projectId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<IReadOnlyList<NonaEnvironment>>(
            HttpMethod.Get,
            $"admin/projects/{Segment(projectId, nameof(projectId))}/environments",
            body: null,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task<NonaEnvironment> CreateEnvironmentAsync(
        string projectId,
        string name,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<NonaEnvironment>(
            HttpMethod.Post,
            $"admin/projects/{Segment(projectId, nameof(projectId))}/environments",
            new NonaCreateEnvironmentRequest(name),
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task DeleteEnvironmentAsync(
        string projectId,
        string environmentId,
        CancellationToken cancellationToken = default)
    {
        return SendNoContentAsync(
            HttpMethod.Delete,
            $"admin/projects/{Segment(projectId, nameof(projectId))}/environments/{Segment(environmentId, nameof(environmentId))}",
            body: null,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task<IReadOnlyList<NonaConfigEntry>> ListConfigEntriesAsync(
        string projectId,
        string environmentName,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<IReadOnlyList<NonaConfigEntry>>(
            HttpMethod.Get,
            $"admin/projects/{Segment(projectId, nameof(projectId))}/environments/{Segment(environmentName, nameof(environmentName))}/config-entries",
            body: null,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task<NonaConfigEntry> GetConfigEntryAsync(
        string projectId,
        string environmentName,
        string key,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<NonaConfigEntry>(
            HttpMethod.Get,
            $"admin/projects/{Segment(projectId, nameof(projectId))}/environments/{Segment(environmentName, nameof(environmentName))}/config-entries/{Segment(key, nameof(key))}",
            body: null,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task<NonaConfigEntry> UpsertConfigEntryAsync(
        string projectId,
        string environmentName,
        string key,
        string value,
        string? contentType = null,
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        return UpsertConfigEntryAsync(
            projectId,
            environmentName,
            key,
            new NonaUpsertConfigEntryRequest(value, contentType, scope),
            cancellationToken);
    }

    public Task<NonaConfigEntry> UpsertConfigEntryAsync(
        string projectId,
        string environmentName,
        string key,
        NonaUpsertConfigEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<NonaConfigEntry>(
            HttpMethod.Put,
            $"admin/projects/{Segment(projectId, nameof(projectId))}/environments/{Segment(environmentName, nameof(environmentName))}/config-entries/{Segment(key, nameof(key))}",
            request,
            AuthMode.Bearer,
            cancellationToken);
    }

    public Task DeleteConfigEntryAsync(
        string projectId,
        string environmentName,
        string key,
        CancellationToken cancellationToken = default)
    {
        return SendNoContentAsync(
            HttpMethod.Delete,
            $"admin/projects/{Segment(projectId, nameof(projectId))}/environments/{Segment(environmentName, nameof(environmentName))}/config-entries/{Segment(key, nameof(key))}",
            body: null,
            AuthMode.Bearer,
            cancellationToken);
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
        object? body,
        AuthMode authMode,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, body, authMode);
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

    private async Task SendNoContentAsync(
        HttpMethod method,
        string path,
        object? body,
        AuthMode authMode,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, body, authMode);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            ThrowResponseException(response, request, responseBody);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body, AuthMode authMode)
    {
        var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        ApplyAuthentication(request, authMode);

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, _jsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private void ApplyAuthentication(HttpRequestMessage request, AuthMode authMode)
    {
        switch (authMode)
        {
            case AuthMode.None:
                return;
            case AuthMode.ApiKey:
                if (string.IsNullOrWhiteSpace(ApiKey))
                {
                    throw new InvalidOperationException("Nona API-key calls require NonaClient.ApiKey.");
                }

                request.Headers.TryAddWithoutValidation(ApiKeyHeaderName, ApiKey);
                return;
            case AuthMode.Bearer:
                if (string.IsNullOrWhiteSpace(BearerToken))
                {
                    throw new InvalidOperationException("Nona admin calls require NonaClient.BearerToken.");
                }

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BearerToken);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(authMode), authMode, "Unknown authentication mode.");
        }
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

    private enum AuthMode
    {
        None,
        ApiKey,
        Bearer
    }
}
