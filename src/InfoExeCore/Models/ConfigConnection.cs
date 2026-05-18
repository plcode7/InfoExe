namespace InfoExeCore.Models;

/// <summary>
/// Represents a connection string or database configuration found in a file.
/// </summary>
public sealed record ConfigConnection(
    string ConfigId,
    string FilePath,           // config file where it was found
    string FileType,           // appsettings.json, web.config, .env, ini, yaml, xml, php, python
    string DatabaseType,       // mysql, mssql, sqlite, postgresql, oracle, firebird
    string? Server,            // host:port
    string? Database,          // database name
    string? UserId,            // username (masked if --mask-credentials)
    string? PasswordHash,      // SHA256 hash of password for uniqueness comparison (not the password itself)
    string? ConnectionString,  // full connection string (with password masked)
    string? ExtraParameters,   // additional parameters
    DateTime DiscoveredAtUtc
);