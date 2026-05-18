namespace InfoExeCore;

/// <summary>
/// Centralized constants to eliminate magic strings across the codebase.
/// </summary>
public static class Constants
{
    // File types
    public const string FileTypeDotNetManaged = "dotnet-managed";
    public const string FileTypePythonArtifact = "python-artifact";
    public const string FileTypeUnknown = "unknown";

    // Scan file statuses
    public const string StatusProcessed = "processed";
    public const string StatusPartial = "partial";
    public const string StatusFailed = "failed";

    // Vendor statuses
    public const string VendorStatusAttributed = "attributed";
    public const string VendorStatusInconclusive = "inconclusive";

    // Decompile statuses
    public const string DecompileStatusDecompiled = "decompiled";
    public const string DecompileStatusFailed = "decompilation_failed";
    public const string DecompileStatusSkipped = "skipped";

    // Reason codes
    public const string ReasonIlSpyNotFound = "ilspy_not_found";
    public const string ReasonIlSpyError = "ilspy_error";
    public const string ReasonNotManaged = "not_managed";
    public const string ReasonCorrupt = "corrupt";
    public const string ReasonUnknown = "unknown";

    // Default values
    public const string DefaultDbFileName = "infoexe.db";
    public const string DefaultAppDataFolder = "InfoExe";

    // Limits
    public const int MaxRetryCount = 3;
    public const int MaxPathLength = 260;
}