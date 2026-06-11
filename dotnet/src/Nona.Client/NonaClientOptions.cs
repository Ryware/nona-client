using System;

namespace Nona.Client;

public sealed class NonaClientOptions
{
    public Uri? BaseAddress { get; set; }

    public string? ApiKey { get; set; }
}
