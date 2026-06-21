# Nona.Client (.NET)

.NET client for reading Nona configuration values.

## Project

- Package ID: `Nona.Client`
- Source project: [dotnet/src/Nona.Client/Nona.Client.csproj](src/Nona.Client/Nona.Client.csproj)
- Test project: [dotnet/tests/Nona.Client.Tests/Nona.Client.Tests.csproj](tests/Nona.Client.Tests/Nona.Client.Tests.csproj)

## Target Frameworks

- `netstandard2.0`
- `net8.0`

## Basic Usage

```csharp
using Nona.Client;

var client = new NonaClient("https://nona.example.com", apiKey: "your-api-key");
var value = await client.GetConfigValueAsync("production", "Features:Checkout");
Console.WriteLine(value.Value);
```

API keys are bound to one project, so config reads only take an environment and key.

## Available Methods

- `GetConfigValueAsync(environmentId, key, cancellationToken)`
- `TryGetConfigValueAsync(environmentId, key, cancellationToken)`
- `GetStringValueAsync(environmentId, key, cancellationToken)`
- `GetJsonValueAsync<T>(environmentId, key, jsonTypeInfo, cancellationToken)`

## Options

Use `NonaClientOptions` to configure:

- `BaseAddress`
- `ApiKey`
- `CacheTtl`
- `CacheMemoryLimitMegabytes`
- `AllowStaleCache`
