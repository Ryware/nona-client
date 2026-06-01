using System.Net;
using System.Net.Http.Headers;
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
    public async Task ListConfigEntriesAsync_SendsBearerTokenAndParsesEntries()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            [{
              "project":"my-project",
              "environment":"production",
              "key":"Features:Checkout",
              "value":"true",
              "contentType":"boolean",
              "scope":"all",
              "createdAt":"2026-05-11T10:00:00Z",
              "updatedAt":"2026-05-11T10:00:00Z"
            }]
            """));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://nona.test")
        };

        using var client = new NonaClient(httpClient, new NonaClientOptions
        {
            BearerToken = "jwt-token"
        });

        var entries = await client.ListConfigEntriesAsync("my-project", "production");

        var entry = Assert.Single(entries);
        Assert.Equal("Features:Checkout", entry.Key);
        Assert.Equal("boolean", entry.ContentType);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://nona.test/admin/projects/my-project/environments/production/config-entries", request.Uri.AbsoluteUri);
        Assert.Equal("Bearer", request.Authorization?.Scheme);
        Assert.Equal("jwt-token", request.Authorization?.Parameter);
    }

    [Fact]
    public async Task UpsertConfigEntryAsync_SerializesCamelCaseBodyAndEscapesSegments()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            {
              "project":"my-project",
              "environment":"production",
              "key":"Connection:Default",
              "value":"{\"timeout\":10}",
              "contentType":"json",
              "scope":"server",
              "createdAt":"2026-05-11T10:00:00Z",
              "updatedAt":"2026-05-11T10:00:00Z"
            }
            """));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://nona.test/")
        };

        using var client = new NonaClient(httpClient, new NonaClientOptions
        {
            BearerToken = "jwt-token"
        });

        await client.UpsertConfigEntryAsync(
            "my-project",
            "production",
            "Connection:Default",
            "{\"timeout\":10}",
            NonaContentTypes.Json,
            NonaConfigScopes.Server);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://nona.test/admin/projects/my-project/environments/production/config-entries/Connection%3ADefault", request.Uri.AbsoluteUri);
        Assert.Contains("\"value\":\"{\\u0022timeout\\u0022:10}\"", request.Body);
        Assert.Contains("\"contentType\":\"json\"", request.Body);
        Assert.Contains("\"scope\":\"server\"", request.Body);
    }

    [Fact]
    public async Task CreateUserAsync_SerializesBodyAndParsesUser()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            {
              "id": 42,
              "email": "editor@example.com",
              "name": "Editor",
              "role": "editor",
              "scope": "client",
              "isAdmin": false,
              "projects": [{ "projectName": "my-project", "role": "viewer" }],
              "createdAt": "2026-05-11T10:00:00Z",
              "updatedAt": "2026-05-11T10:00:00Z",
              "resetPasswordToken": "reset-token"
            }
            """));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://nona.test/")
        };

        using var client = new NonaClient(httpClient, new NonaClientOptions
        {
            BearerToken = "jwt-token"
        });

        var user = await client.CreateUserAsync(
            "Editor",
            "editor@example.com",
            NonaUserRoles.Editor,
            NonaConfigScopes.Client);

        Assert.Equal(42, user.Id);
        Assert.Equal("editor@example.com", user.Email);
        Assert.Equal("reset-token", user.ResetPasswordToken);
        Assert.Equal("my-project", Assert.Single(user.Projects).ProjectName);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://nona.test/admin/users", request.Uri.AbsoluteUri);
        Assert.Contains("\"name\":\"Editor\"", request.Body);
        Assert.Contains("\"email\":\"editor@example.com\"", request.Body);
        Assert.Contains("\"role\":\"editor\"", request.Body);
        Assert.Contains("\"scope\":\"client\"", request.Body);
    }

    [Fact]
    public async Task SetProjectAccessAsync_EscapesProjectNameAndParsesAccess()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            { "projectName": "project:one", "role": "editor" }
            """));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://nona.test/")
        };

        using var client = new NonaClient(httpClient, new NonaClientOptions
        {
            BearerToken = "jwt-token"
        });

        var access = await client.SetProjectAccessAsync(7, "project:one", NonaUserRoles.Editor);

        Assert.Equal("project:one", access.ProjectName);
        Assert.Equal("editor", access.Role);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("https://nona.test/admin/users/7/projects/project%3Aone", request.Uri.AbsoluteUri);
        Assert.Contains("\"role\":\"editor\"", request.Body);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_HandlesNoContentResponse()
    {
        var handler = new StubHttpMessageHandler(_ => NoContentResponse());

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://nona.test/")
        };

        using var client = new NonaClient(httpClient);

        await client.RequestPasswordResetAsync("user@example.com");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://nona.test/auth/forgot-password", request.Uri.AbsoluteUri);
        Assert.Contains("\"email\":\"user@example.com\"", request.Body);
        Assert.Null(request.Authorization);
    }

    [Fact]
    public async Task DashboardAndAuditLogMethods_SendBearerTokenAndParseResponses()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/admin/dashboard/counts" => JsonResponse("""
                    { "users": 2, "projects": 3, "configEntries": 4 }
                    """),
                "/admin/audit-logs" => JsonResponse("""
                    [{
                      "id": 5,
                      "actor": "admin@example.com",
                      "actorIsSystem": false,
                      "action": "Created Project",
                      "target": "my-project",
                      "project": "my-project",
                      "environment": null,
                      "createdAt": "2026-05-11T10:00:00Z"
                    }]
                    """),
                _ => JsonResponse("""{"error":"not found"}""", HttpStatusCode.NotFound)
            };
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://nona.test/")
        };

        using var client = new NonaClient(httpClient, new NonaClientOptions
        {
            BearerToken = "jwt-token"
        });

        var counts = await client.GetDashboardCountsAsync();
        var logs = await client.ListAuditLogsAsync();

        Assert.Equal(2, counts.Users);
        Assert.Equal(3, counts.Projects);
        Assert.Equal(4, counts.ConfigEntries);
        Assert.Equal("Created Project", Assert.Single(logs).Action);
        Assert.All(handler.Requests, request => Assert.Equal("jwt-token", request.Authorization?.Parameter));
    }

    [Fact]
    public async Task ApiKeyManagementMethods_SendBearerTokenAndSerializeScope()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            return request.Method.Method switch
            {
                "POST" => JsonResponse("""
                    {
                      "id": 7,
                      "name": "Web Client",
                      "key": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                      "project": "my-project",
                      "environment": "production",
                      "scope": "client",
                      "createdAt": "2026-05-11T10:00:00Z",
                      "updatedAt": "2026-05-11T10:00:00Z"
                    }
                    """, HttpStatusCode.Created),
                "DELETE" => NoContentResponse(),
                _ => JsonResponse("[]")
            };
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://nona.test/")
        };

        using var client = new NonaClient(httpClient, new NonaClientOptions
        {
            BearerToken = "jwt-token"
        });

        await client.ListApiKeysAsync("my-project");
        var created = await client.CreateApiKeyAsync(
            "my-project",
            "Web Client",
            "production",
            NonaConfigScopes.Client);
        await client.DeleteApiKeyAsync("my-project", 7);

        Assert.Equal(64, created.Key.Length);
        Assert.Equal("https://nona.test/admin/projects/my-project/api-keys", handler.Requests[0].Uri.AbsoluteUri);
        Assert.Equal("https://nona.test/admin/projects/my-project/api-keys", handler.Requests[1].Uri.AbsoluteUri);
        Assert.Equal("https://nona.test/admin/projects/my-project/api-keys/7", handler.Requests[2].Uri.AbsoluteUri);
        Assert.All(handler.Requests, request => Assert.Equal("jwt-token", request.Authorization?.Parameter));
        Assert.Contains("\"environment\":\"production\"", handler.Requests[1].Body);
        Assert.Contains("\"scope\":\"client\"", handler.Requests[1].Body);
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

    private static HttpResponseMessage NoContentResponse()
    {
        return new HttpResponseMessage(HttpStatusCode.NoContent);
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
            var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? string.Empty;

            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri ?? throw new InvalidOperationException("Request URI was not set."),
                request.Headers.Authorization,
                headers,
                body));

            return Task.FromResult(_handle(request));
        }
    }

    private sealed class CapturedRequest
    {
        public CapturedRequest(
            HttpMethod method,
            Uri uri,
            AuthenticationHeaderValue? authorization,
            IReadOnlyDictionary<string, string[]> headers,
            string body)
        {
            Method = method;
            Uri = uri;
            Authorization = authorization;
            Headers = headers;
            Body = body;
        }

        public HttpMethod Method { get; }

        public Uri Uri { get; }

        public AuthenticationHeaderValue? Authorization { get; }

        public IReadOnlyDictionary<string, string[]> Headers { get; }

        public string Body { get; }

        public string? GetHeader(string name)
        {
            return Headers.TryGetValue(name, out var values) ? values.SingleOrDefault() : null;
        }
    }
}
