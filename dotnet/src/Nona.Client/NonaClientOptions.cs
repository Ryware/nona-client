using System;
using System.Text.Json;

namespace Nona.Client;

public sealed class NonaClientOptions
{
    public Uri? BaseAddress { get; set; }

    public string? ApiKey { get; set; }

    public string? BearerToken { get; set; }

    public JsonSerializerOptions? JsonSerializerOptions { get; set; }
}
