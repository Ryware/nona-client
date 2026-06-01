namespace Nona.Client;

public sealed class NonaUpsertConfigEntryRequest
{
    public NonaUpsertConfigEntryRequest()
    {
    }

    public NonaUpsertConfigEntryRequest(string value, string? contentType = null, string? scope = null)
    {
        Value = value;
        ContentType = contentType;
        Scope = scope;
    }

    public string Value { get; set; } = string.Empty;

    public string? ContentType { get; set; }

    public string? Scope { get; set; }
}

public sealed class NonaCreateProjectRequest
{
    public NonaCreateProjectRequest()
    {
    }

    public NonaCreateProjectRequest(string name)
    {
        Name = name;
    }

    public string Name { get; set; } = string.Empty;
}

public sealed class NonaCreateEnvironmentRequest
{
    public NonaCreateEnvironmentRequest()
    {
    }

    public NonaCreateEnvironmentRequest(string name)
    {
        Name = name;
    }

    public string Name { get; set; } = string.Empty;
}

public sealed class NonaCreateApiKeyRequest
{
    public NonaCreateApiKeyRequest()
    {
    }

    public NonaCreateApiKeyRequest(string name, string? environment = null, string? scope = null)
    {
        Name = name;
        Environment = environment;
        Scope = scope;
    }

    public string Name { get; set; } = string.Empty;

    public string? Environment { get; set; }

    public string? Scope { get; set; }
}

public sealed class NonaLoginRequest
{
    public NonaLoginRequest()
    {
    }

    public NonaLoginRequest(string email, string password)
    {
        Email = email;
        Password = password;
    }

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public sealed class NonaRequestPasswordResetRequest
{
    public NonaRequestPasswordResetRequest()
    {
    }

    public NonaRequestPasswordResetRequest(string email)
    {
        Email = email;
    }

    public string Email { get; set; } = string.Empty;
}

public sealed class NonaRerollApiKeysRequest
{
    public NonaRerollApiKeysRequest()
    {
    }

    public NonaRerollApiKeysRequest(string keyType)
    {
        KeyType = keyType;
    }

    public string KeyType { get; set; } = string.Empty;
}

public sealed class NonaCreateUserRequest
{
    public NonaCreateUserRequest()
    {
    }

    public NonaCreateUserRequest(string name, string email, string? role = null, string? scope = null)
    {
        Name = name;
        Email = email;
        Role = role;
        Scope = scope;
    }

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Role { get; set; }

    public string? Scope { get; set; }
}

public sealed class NonaUpdateUserRequest
{
    public NonaUpdateUserRequest()
    {
    }

    public NonaUpdateUserRequest(string name, string? role = null, string? scope = null)
    {
        Name = name;
        Role = role;
        Scope = scope;
    }

    public string Name { get; set; } = string.Empty;

    public string? Role { get; set; }

    public string? Scope { get; set; }
}

public sealed class NonaProjectAccessRequest
{
    public NonaProjectAccessRequest()
    {
    }

    public NonaProjectAccessRequest(string role)
    {
        Role = role;
    }

    public string Role { get; set; } = string.Empty;
}
