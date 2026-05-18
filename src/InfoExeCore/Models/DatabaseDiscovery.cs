namespace InfoExeCore.Models;

/// <summary>
/// Represents a discovered database instance or file.
/// </summary>
public sealed record DatabaseDiscovery(
    string DiscoveryId,
    string DatabaseType,       // mysql, sqlite, mssql, postgresql, oracle, firebird, unknown
    string Location,           // file path or connection string host
    string? DatabaseName,      // database/schema name
    string? ConnectionString,  // full connection string if found
    string? SourceFile,        // config file where the connection was found
    long? FileSizeBytes,       // size if it's a file-based DB
    string? Version,           // detected version
    string Status,             // accessible, inaccessible, file_only
    string? ErrorMessage,      // if inaccessible, why
    DateTime DiscoveredAtUtc
);