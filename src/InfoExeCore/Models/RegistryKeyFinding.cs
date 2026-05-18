namespace InfoExeCore.Models;

/// <summary>
/// Represents a Windows Registry key finding — can be suspicious, broken, or informational.
/// </summary>
public sealed record RegistryKeyFinding(
    string FindingId,
    string Hive,               // HKLM, HKCU, HKU, HKCR, HKCC
    string KeyPath,            // full registry path
    string? ValueName,         // value name (null for default)
    string? ValueData,         // value data as string
    string? ValueType,         // REG_SZ, REG_DWORD, REG_BINARY, REG_EXPAND_SZ, REG_MULTI_SZ
    string Severity,           // info, warning, error, suspicious
    string Category,           // startup, com_registration, shell_extension, app_config, broken_path, orphaned, license, connection_string, unknown
    string? Description,       // human-readable description of the finding
    string? Recommendation,    // suggested action
    DateTime DiscoveredAtUtc
);