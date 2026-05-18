using Microsoft.Data.Sqlite;
using InfoExeCore.Models;

namespace InfoExeCore;

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

    // Database discovery types
    public const string DbTypeMySql = "mysql";
    public const string DbTypeSqlite = "sqlite";
    public const string DbTypeMsSql = "mssql";
    public const string DbTypePostgreSql = "postgresql";
    public const string DbTypeOracle = "oracle";
    public const string DbTypeFirebird = "firebird";
    public const string DbTypeMsAccess = "msaccess";
    public const string DbTypeDBase = "dbase";
    public const string DbTypeInterbase = "interbase";
    public const string DbTypeGeneric = "generic";
    public const string DbTypeUnknown = "unknown";

    // Registry severities
    public const string RegSeverityInfo = "info";
    public const string RegSeverityWarning = "warning";
    public const string RegSeverityError = "error";
    public const string RegSeveritySuspicious = "suspicious";

    // Registry categories
    public const string RegCategoryStartup = "startup";
    public const string RegCategoryComRegistration = "com_registration";
    public const string RegCategoryShellExtension = "shell_extension";
    public const string RegCategoryAppConfig = "app_config";
    public const string RegCategoryBrokenPath = "broken_path";
    public const string RegCategoryOrphaned = "orphaned";
    public const string RegCategoryLicense = "license";
    public const string RegCategoryConnectionString = "connection_string";
    public const string RegCategoryUnknown = "unknown";

    // Config file types
    public const string ConfigTypeJson = "json";
    public const string ConfigTypeXml = "xml";
    public const string ConfigTypeEnv = "env";
    public const string ConfigTypeIni = "ini";
    public const string ConfigTypeYaml = "yaml";
    public const string ConfigTypePhp = "php";
    public const string ConfigTypePython = "python";
    public const string ConfigTypeGeneric = "generic";
}