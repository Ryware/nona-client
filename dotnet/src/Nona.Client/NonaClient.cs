using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace Nona.Client;

public sealed class NonaClient : IDisposable
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;
    private readonly NonaClientOptions _options;
    private readonly object _cacheLock = new object();
    private readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<NonaConfigValue>> _inFlightFetches = new Dictionary<string, Task<NonaConfigValue>>(StringComparer.Ordinal);
    private readonly TimeSpan _cacheTtl;
    private readonly long _cacheMemoryLimitBytes;
    private readonly bool _allowStaleCache;
    private long _cacheSizeBytes;

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
        _cacheTtl = ValidateCacheTtl(options.CacheTtl);
        _cacheMemoryLimitBytes = ConvertMegabytesToBytes(ValidateCacheMemoryLimitMegabytes(options.CacheMemoryLimitMegabytes));
        _allowStaleCache = options.AllowStaleCache;

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
        var cacheKey = CreateCacheKey(environmentId, key);
        var cachedValue = TryGetCachedValue(cacheKey, path);
        if (cachedValue is not null)
        {
            return cachedValue;
        }

        return await GetOrFetchConfigValueAsync(cacheKey, path, cancellationToken).ConfigureAwait(false);
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
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        if (jsonTypeInfo is null)
        {
            throw new ArgumentNullException(nameof(jsonTypeInfo));
        }

        var configValue = await GetConfigValueAsync(environmentId, key, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(configValue.Value, jsonTypeInfo);
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<NonaConfigValue> FetchConfigValueAsync(
        string path,
        CancellationToken cancellationToken)
    {
        return await SendAsync(HttpMethod.Get, path, cancellationToken).ConfigureAwait(false);
    }

    private async Task<NonaConfigValue> GetOrFetchConfigValueAsync(
        string cacheKey,
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task<NonaConfigValue>? fetchTask;
        lock (_cacheLock)
        {
            if (!_inFlightFetches.TryGetValue(cacheKey, out fetchTask))
            {
                fetchTask = FetchAndCacheConfigValueAsync(cacheKey, path);
                _inFlightFetches[cacheKey] = fetchTask;
                TrackInFlightFetch(cacheKey, fetchTask);
            }
        }

        var value = await WaitForFetchAsync(fetchTask, cancellationToken).ConfigureAwait(false);
        return Clone(value);
    }

    private void TrackInFlightFetch(string cacheKey, Task<NonaConfigValue> task)
    {
        _ = task.ContinueWith(
            CompleteInFlightFetch,
            new InFlightFetch(this, cacheKey, task),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task<NonaConfigValue> FetchAndCacheConfigValueAsync(string cacheKey, string path)
    {
        var value = await FetchConfigValueAsync(path, CancellationToken.None).ConfigureAwait(false);
        SetCachedValue(cacheKey, value);
        return value;
    }

    private static void CompleteInFlightFetch(Task<NonaConfigValue> completedTask, object? state)
    {
        var inFlightFetch = (InFlightFetch)state!;
        var client = inFlightFetch.Client;

        lock (client._cacheLock)
        {
            if (client._inFlightFetches.TryGetValue(inFlightFetch.CacheKey, out var currentTask) &&
                ReferenceEquals(currentTask, inFlightFetch.Task))
            {
                client._inFlightFetches.Remove(inFlightFetch.CacheKey);
            }
        }
    }

    private static async Task<NonaConfigValue> WaitForFetchAsync(
        Task<NonaConfigValue> fetchTask,
        CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled || fetchTask.IsCompleted)
        {
            return await fetchTask.ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var cancellationTaskSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(
            state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            cancellationTaskSource))
        {
            var completedTask = await Task.WhenAny(fetchTask, cancellationTaskSource.Task).ConfigureAwait(false);
            if (!ReferenceEquals(completedTask, fetchTask))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        return await fetchTask.ConfigureAwait(false);
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

    private NonaConfigValue? TryGetCachedValue(string cacheKey, string path)
    {
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(cacheKey, out var entry))
            {
                return null;
            }

            var now = DateTimeOffset.UtcNow;
            if (entry.ExpiresAt > now)
            {
                entry.Touch();
                return Clone(entry.Value);
            }

            if (_allowStaleCache)
            {
                entry.Touch();
                QueueRefresh(cacheKey, path, entry);
                return Clone(entry.Value);
            }

            RemoveCacheEntry(cacheKey);
            return null;
        }
    }

    private void QueueRefresh(string cacheKey, string path, CacheEntry entry)
    {
        if (entry.Refreshing)
        {
            return;
        }

        entry.Refreshing = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var value = await FetchConfigValueAsync(path, CancellationToken.None).ConfigureAwait(false);
                SetCachedValue(cacheKey, value);
            }
            catch
            {
                lock (_cacheLock)
                {
                    if (_cache.TryGetValue(cacheKey, out var current))
                    {
                        current.Refreshing = false;
                    }
                }
            }
        });
    }

    private void SetCachedValue(string cacheKey, NonaConfigValue value)
    {
        var cachedValue = Clone(value);
        var sizeBytes = EstimateCacheEntrySize(cacheKey, cachedValue);
        if (sizeBytes > _cacheMemoryLimitBytes)
        {
            lock (_cacheLock)
            {
                RemoveCacheEntry(cacheKey);
            }

            return;
        }

        lock (_cacheLock)
        {
            RemoveCacheEntry(cacheKey);
            _cache[cacheKey] = new CacheEntry(cachedValue, DateTimeOffset.UtcNow.Add(_cacheTtl), sizeBytes);
            _cacheSizeBytes += sizeBytes;
            CompactCache();
        }
    }

    private void CompactCache()
    {
        if (_cacheSizeBytes <= _cacheMemoryLimitBytes)
        {
            return;
        }

        var oldestKeys = new List<string>(_cache.Count);
        foreach (var item in _cache)
        {
            oldestKeys.Add(item.Key);
        }

        oldestKeys.Sort((left, right) => _cache[left].LastAccessed.CompareTo(_cache[right].LastAccessed));

        foreach (var key in oldestKeys)
        {
            if (_cacheSizeBytes <= _cacheMemoryLimitBytes)
            {
                return;
            }

            RemoveCacheEntry(key);
        }
    }

    private void RemoveCacheEntry(string cacheKey)
    {
        if (!_cache.TryGetValue(cacheKey, out var entry))
        {
            return;
        }

        _cache.Remove(cacheKey);
        _cacheSizeBytes -= entry.SizeBytes;
    }

    private static NonaConfigValue Clone(NonaConfigValue value)
    {
        return new NonaConfigValue
        {
            Value = value.Value,
            ContentType = value.ContentType
        };
    }

    private static NonaConfigValue DeserializeConfigValue(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The response JSON root must be an object.");
        }

        if (!root.TryGetProperty("value", out var valueProperty) || valueProperty.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("The response JSON must include a string 'value' property.");
        }

        if (!root.TryGetProperty("contentType", out var contentTypeProperty) || contentTypeProperty.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("The response JSON must include a string 'contentType' property.");
        }

        return new NonaConfigValue
        {
            Value = valueProperty.GetString() ?? string.Empty,
            ContentType = contentTypeProperty.GetString() ?? string.Empty
        };
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

    private static string CreateCacheKey(string environmentId, string key)
    {
        return environmentId + "\u001F" + key;
    }

    private static long EstimateCacheEntrySize(string cacheKey, NonaConfigValue value)
    {
        return 128L + (cacheKey.Length + value.Value.Length + value.ContentType.Length) * sizeof(char);
    }

    private static TimeSpan ValidateCacheTtl(TimeSpan cacheTtl)
    {
        if (cacheTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cacheTtl), cacheTtl, "Cache TTL must be greater than zero.");
        }

        return cacheTtl;
    }

    private static long ValidateCacheMemoryLimitMegabytes(long cacheMemoryLimitMegabytes)
    {
        if (cacheMemoryLimitMegabytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cacheMemoryLimitMegabytes), cacheMemoryLimitMegabytes, "Cache memory limit must be greater than zero.");
        }

        if (cacheMemoryLimitMegabytes > long.MaxValue / 1024 / 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(cacheMemoryLimitMegabytes), cacheMemoryLimitMegabytes, "Cache memory limit is too large.");
        }

        return cacheMemoryLimitMegabytes;
    }

    private static long ConvertMegabytesToBytes(long megabytes)
    {
        return megabytes * 1024 * 1024;
    }

    private sealed class CacheEntry
    {
        public CacheEntry(NonaConfigValue value, DateTimeOffset expiresAt, long sizeBytes)
        {
            Value = value;
            ExpiresAt = expiresAt;
            SizeBytes = sizeBytes;
            LastAccessed = DateTimeOffset.UtcNow;
        }

        public NonaConfigValue Value { get; }

        public DateTimeOffset ExpiresAt { get; }

        public long SizeBytes { get; }

        public DateTimeOffset LastAccessed { get; private set; }

        public bool Refreshing { get; set; }

        public void Touch()
        {
            LastAccessed = DateTimeOffset.UtcNow;
        }
    }

    private sealed class InFlightFetch
    {
        public InFlightFetch(NonaClient client, string cacheKey, Task<NonaConfigValue> task)
        {
            Client = client;
            CacheKey = cacheKey;
            Task = task;
        }

        public NonaClient Client { get; }

        public string CacheKey { get; }

        public Task<NonaConfigValue> Task { get; }
    }
}
