using System;
using System.Collections.Generic;

namespace Nona.Client;

public sealed class NonaConfigValue
{
    public string Value { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;
}

public sealed class NonaConfigEntry
{
    public string Project { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public sealed class NonaProject
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? UrlSlug { get; set; }

    public string? ServerApiKey { get; set; }

    public string? ClientApiKey { get; set; }

    public List<string> Environments { get; set; } = new List<string>();

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public sealed class NonaEnvironment
{
    public string Name { get; set; } = string.Empty;

    public string Project { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public sealed class NonaLoginResponse
{
    public string Token { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
}

public sealed class NonaRegisterResult
{
    public bool Success { get; set; }

    public NonaLoginResponse? Response { get; set; }

    public string? Error { get; set; }
}

public sealed class NonaProjectAccess
{
    public string ProjectName { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;
}

public sealed class NonaUser
{
    public long Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }

    public List<NonaProjectAccess> Projects { get; set; } = new List<NonaProjectAccess>();

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? ResetPasswordToken { get; set; }
}

public sealed class NonaAuditLog
{
    public long Id { get; set; }

    public string Actor { get; set; } = string.Empty;

    public bool ActorIsSystem { get; set; }

    public string Action { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string? Project { get; set; }

    public string? Environment { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed class NonaDashboardCounts
{
    public int Users { get; set; }

    public int Projects { get; set; }

    public int ConfigEntries { get; set; }
}
