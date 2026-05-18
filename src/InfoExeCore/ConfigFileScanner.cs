using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using InfoExeCore.Models;

namespace InfoExeCore;

/// <summary>
/// Scans configuration files for database connection strings, credentials,
/// and other database-related settings.
/// Supports: appsettings.json, web.config, .env, .ini, .yaml, .xml, .php, .py
/// </summary>
public static class ConfigFileScanner
{
    private static readonly string[] ConfigExtensions =
        { ".json", ".config", ".env", ".ini", ".yaml", ".yml", ".xml", ".php", ".py", ".cfg", ".conf", ".settings" };

    private static readonly string[] ConfigFileNames =
        { "appsettings", "web.config", "app.config", ".env", "config", "settings", "database", "connection" };

    public static List<ConfigConnection> ScanDirectory(string searchPath, bool maskCredentials, Action<string, bool>? log = null)
    {
        var results = new List<ConfigConnection>();
        var discovered = DateTime.UtcNow;

        log?.Invoke($"Scanning for configuration files in: {searchPath}", false);

        if (!Directory.Exists(searchPath))
        {
            log?.Invoke($"Directory not found: {searchPath}", true);
            return results;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(searchPath, "*", SearchOption.AllDirectories).Take(2000))
            {
                var fileName = Path.GetFileName(file);
                var ext = Path.GetExtension(file).ToLowerInvariant();

                // Quick filter: check extension OR filename
                if (!ConfigExtensions.Contains(ext) && !ConfigFileNames.Any(n => fileName.StartsWith(n, StringComparison.OrdinalIgnoreCase)))
                    continue;

                try
                {
                    var connections = ParseConfigFile(file, maskCredentials, discovered, log);
                    results.AddRange(connections);
                }
                catch { /* skip unreadable files */ }
            }
        }
        catch { }

        log?.Invoke($"Found {results.Count} database connections in config files", false);
        return results;
    }

    private static List<ConfigConnection> ParseConfigFile(string filePath, bool maskCredentials,
        DateTime discovered, Action<string, bool>? log)
    {
        var results = new List<ConfigConnection>();
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        try
        {
            var content = File.ReadAllText(filePath);
            if (content.Length > 1_000_000) return results; // skip huge files

            switch (ext)
            {
                case ".json":
                    results.AddRange(ParseJsonConfig(filePath, content, maskCredentials, discovered));
                    break;
                case ".config":
                case ".xml":
                    results.AddRange(ParseXmlConfig(filePath, content, maskCredentials, discovered));
                    break;
                case ".env":
                    results.AddRange(ParseEnvConfig(filePath, content, maskCredentials, discovered));
                    break;
                case ".ini":
                case ".cfg":
                case ".conf":
                    results.AddRange(ParseIniConfig(filePath, content, maskCredentials, discovered));
                    break;
                case ".php":
                    results.AddRange(ParsePhpConfig(filePath, content, maskCredentials, discovered));
                    break;
                case ".py":
                    results.AddRange(ParsePythonConfig(filePath, content, maskCredentials, discovered));
                    break;
                case ".yaml":
                case ".yml":
                    results.AddRange(ParseYamlConfig(filePath, content, maskCredentials, discovered));
                    break;
                default:
                    results.AddRange(ParseGenericConfig(filePath, content, maskCredentials, discovered));
                    break;
            }
        }
        catch { }

        return results;
    }

    private static List<ConfigConnection> ParseJsonConfig(string filePath, string content,
        bool maskCredentials, DateTime discovered)
    {
        var results = new List<ConfigConnection>();

        // Look for ConnectionStrings section
        var csMatches = Regex.Matches(content,
            @"""([^""]*ConnectionString[^""]*)""\s*:\s*""([^""]+)""",
            RegexOptions.IgnoreCase);

        foreach (Match m in csMatches)
        {
            var key = m.Groups[1].Value;
            var value = m.Groups[2].Value;
            var parsed = ParseConnectionString(value, maskCredentials);
            if (parsed is null) continue;

            results.Add(new ConfigConnection(
                $"cfg-{Guid.NewGuid():N}", filePath, "json",
                parsed.Value.dbType, parsed.Value.server, parsed.Value.database,
                parsed.Value.userId, parsed.Value.passwordHash, parsed.Value.masked,
                null, discovered
            ));
        }

        return results;
    }

    private static List<ConfigConnection> ParseXmlConfig(string filePath, string content,
        bool maskCredentials, DateTime discovered)
    {
        var results = new List<ConfigConnection>();

        // connectionStrings section
        var csMatches = Regex.Matches(content,
            @"<add\s+[^>]*?(?:name|connectionString)\s*=\s*""([^""]*)""[^>]*?(?:connectionString|name)\s*=\s*""([^""]*)""[^>]*?>",
            RegexOptions.IgnoreCase);

        foreach (Match m in csMatches)
        {
            var attr1 = m.Groups[1].Value;
            var attr2 = m.Groups[2].Value;

            // Determine which is the connection string
            var cs = attr1.Contains('=') && attr1.Contains(';') ? attr1
                : attr2.Contains('=') && attr2.Contains(';') ? attr2
                : attr1.Contains("Server=") || attr1.Contains("Data Source=") ? attr1
                : attr2;

            if (!cs.Contains('=')) continue;

            var parsed = ParseConnectionString(cs, maskCredentials);
            if (parsed is null) continue;

            results.Add(new ConfigConnection(
                $"cfg-{Guid.NewGuid():N}", filePath, "xml",
                parsed.Value.dbType, parsed.Value.server, parsed.Value.database,
                parsed.Value.userId, parsed.Value.passwordHash, parsed.Value.masked,
                null, discovered
            ));
        }

        return results;
    }

    private static List<ConfigConnection> ParseEnvConfig(string filePath, string content,
        bool maskCredentials, DateTime discovered)
    {
        var results = new List<ConfigConnection>();

        var lines = content.Split('\n');
        var dbConfig = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;

            var eq = trimmed.IndexOf('=');
            if (eq < 0) continue;

            var key = trimmed[..eq].Trim();
            var value = trimmed[(eq + 1)..].Trim().Trim('"', '\'');

            if (key.Contains("DB_", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("DATABASE_", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("CONNECTION_", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("MYSQL_", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("MSSQL_", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("PG_", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("SQLITE_", StringComparison.OrdinalIgnoreCase))
            {
                dbConfig[key] = value;
            }
        }

        if (dbConfig.Count > 0)
        {
            var dbType = DetectDbTypeFromEnv(dbConfig);
            var server = dbConfig.FirstOrDefault(kv =>
                kv.Key.Contains("HOST", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Contains("SERVER", StringComparison.OrdinalIgnoreCase)).Value;
            var database = dbConfig.FirstOrDefault(kv =>
                kv.Key.Contains("DATABASE", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Contains("NAME", StringComparison.OrdinalIgnoreCase)).Value;
            var userId = dbConfig.FirstOrDefault(kv =>
                kv.Key.Contains("USER", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Contains("UID", StringComparison.OrdinalIgnoreCase)).Value;
            var password = dbConfig.FirstOrDefault(kv =>
                kv.Key.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Contains("PWD", StringComparison.OrdinalIgnoreCase)).Value;

            var passwordHash = !string.IsNullOrWhiteSpace(password) ? HashPassword(password) : null;
            var maskedCs = BuildMaskedConnectionString(dbType, server, database, userId, maskCredentials);

            results.Add(new ConfigConnection(
                $"cfg-{Guid.NewGuid():N}", filePath, "env",
                dbType, server, database,
                maskCredentials ? "***" : userId,
                passwordHash, maskedCs, null, discovered
            ));
        }

        return results;
    }

    private static List<ConfigConnection> ParseIniConfig(string filePath, string content,
        bool maskCredentials, DateTime discovered)
    {
        // Same approach as env but for ini format
        return ParseGenericConfig(filePath, content, maskCredentials, discovered);
    }

    private static List<ConfigConnection> ParsePhpConfig(string filePath, string content,
        bool maskCredentials, DateTime discovered)
    {
        var results = new List<ConfigConnection>();

        // Look for database configuration arrays
        var dbMatches = Regex.Matches(content,
            @"\$(?:db|database|DB)\w*\s*=\s*\[([^\]]+)\]",
            RegexOptions.Singleline);

        foreach (Match m in dbMatches)
        {
            var configBlock = m.Groups[1].Value;
            var dbType = ExtractPhpValue(configBlock, "driver|type|dbms") ?? "unknown";
            var server = ExtractPhpValue(configBlock, "host|server|hostname");
            var database = ExtractPhpValue(configBlock, "database|dbname|db|name");
            var userId = ExtractPhpValue(configBlock, "username|user|uid|login");
            var password = ExtractPhpValue(configBlock, "password|pass|pwd");

            var passwordHash = !string.IsNullOrWhiteSpace(password) ? HashPassword(password) : null;
            var maskedCs = BuildMaskedConnectionString(dbType, server, database, userId, maskCredentials);

            results.Add(new ConfigConnection(
                $"cfg-{Guid.NewGuid():N}", filePath, "php",
                dbType, server, database,
                maskCredentials ? "***" : userId,
                passwordHash, maskedCs, null, discovered
            ));
        }

        return results;
    }

    private static string? ExtractPhpValue(string block, string keyPattern)
    {
        var match = Regex.Match(block, $@"'(?:{keyPattern})'\s*=>\s*'(?:[^']*)'|\""(?:{keyPattern})\""\s*=>\s*\""([^\""]*)\""",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static List<ConfigConnection> ParsePythonConfig(string filePath, string content,
        bool maskCredentials, DateTime discovered)
    {
        var results = new List<ConfigConnection>();

        // Python config patterns: dictionaries with database keys
        var dictMatches = Regex.Matches(content,
            @"""?(?:host|server|database|user|password|port|engine)""?\s*[:=]\s*""?([^"",\n)]+)""?",
            RegexOptions.IgnoreCase);

        if (dictMatches.Count >= 3) // at least 3 DB-related keys found
        {
            var dbType = DetectDbTypeFromPython(content);
            var server = ExtractPythonValue(content, "host|server|hostname");
            var database = ExtractPythonValue(content, "database|dbname|db|name");
            var userId = ExtractPythonValue(content, "user|username|uid|login");
            var password = ExtractPythonValue(content, "password|passwd|pwd");

            var passwordHash = !string.IsNullOrWhiteSpace(password) ? HashPassword(password) : null;
            var maskedCs = BuildMaskedConnectionString(dbType, server, database, userId, maskCredentials);

            results.Add(new ConfigConnection(
                $"cfg-{Guid.NewGuid():N}", filePath, "python",
                dbType, server, database,
                maskCredentials ? "***" : userId,
                passwordHash, maskedCs, null, discovered
            ));
        }

        return results;
    }

    private static string? ExtractPythonValue(string content, string keyPattern)
    {
        var match = Regex.Match(content,
            $@"[""']?(?:{keyPattern})[""']?\s*[:=]\s*[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string DetectDbTypeFromPython(string content)
    {
        if (content.Contains("mysql", StringComparison.OrdinalIgnoreCase)) return "mysql";
        if (content.Contains("postgresql", StringComparison.OrdinalIgnoreCase) || content.Contains("psycopg")) return "postgresql";
        if (content.Contains("mssql", StringComparison.OrdinalIgnoreCase) || content.Contains("pyodbc")) return "mssql";
        if (content.Contains("sqlite", StringComparison.OrdinalIgnoreCase)) return "sqlite";
        if (content.Contains("oracle", StringComparison.OrdinalIgnoreCase) || content.Contains("cx_oracle")) return "oracle";
        return "unknown";
    }

    private static List<ConfigConnection> ParseYamlConfig(string filePath, string content,
        bool maskCredentials, DateTime discovered)
    {
        // Parse YAML key-value pairs for database config
        var dbType = ExtractYamlValue(content, "driver|type|engine|adapter") ?? "unknown";
        var server = ExtractYamlValue(content, "host|server|hostname");
        var database = ExtractYamlValue(content, "database|dbname|db|name");
        var userId = ExtractYamlValue(content, "username|user|uid|login");
        var password = ExtractYamlValue(content, "password|passwd|pwd|pass");

        if (server is not null || database is not null)
        {
            var passwordHash = !string.IsNullOrWhiteSpace(password) ? HashPassword(password) : null;
            var maskedCs = BuildMaskedConnectionString(dbType, server, database, userId, maskCredentials);

            return new List<ConfigConnection>
            {
                new($"cfg-{Guid.NewGuid():N}", filePath, "yaml",
                    dbType, server, database,
                    maskCredentials ? "***" : userId,
                    passwordHash, maskedCs, null, discovered)
            };
        }

        return new List<ConfigConnection>();
    }

    private static string? ExtractYamlValue(string content, string keyPattern)
    {
        var match = Regex.Match(content,
            $@"(?:{keyPattern})\s*:\s*(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim().Trim('"', '\'') : null;
    }

    private static List<ConfigConnection> ParseGenericConfig(string filePath, string content,
        bool maskCredentials, DateTime discovered)
    {
        var results = new List<ConfigConnection>();

        // Search for common connection string patterns
        var patterns = new[]
        {
            @"Server\s*=\s*([^;\s]+)",
            @"Data Source\s*=\s*([^;\s]+)",
            @"Host\s*=\s*([^;\s]+)",
            @"Database\s*=\s*([^;\s]+)",
            @"Initial Catalog\s*=\s*([^;\s]+)",
        };

        var foundServer = false;
        var foundDb = false;
        string? server = null, database = null;

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
            if (!match.Success) continue;

            if (pattern.Contains("Server") || pattern.Contains("Host") || pattern.Contains("Data Source"))
            {
                server = match.Groups[1].Value;
                foundServer = true;
            }
            else
            {
                database = match.Groups[1].Value;
                foundDb = true;
            }
        }

        if (foundServer || foundDb)
        {
            var dbType = DetectDbType(content);
            results.Add(new ConfigConnection(
                $"cfg-{Guid.NewGuid():N}", filePath, "generic",
                dbType, server, database, null, null,
                BuildMaskedConnectionString(dbType, server, database, null, maskCredentials),
                null, discovered
            ));
        }

        return results;
    }

    // ── Connection string parsing ──

    private static (string dbType, string? server, string? database, string? userId, string? passwordHash, string? masked)?
        ParseConnectionString(string cs, bool maskCredentials)
    {
        var dbType = DetectDbType(cs);
        var server = ExtractParam(cs, "Server", "Data Source", "Host", "Addr");
        var database = ExtractParam(cs, "Database", "Initial Catalog", "Db");
        var userId = ExtractParam(cs, "User ID", "Uid", "User", "Username");
        var password = ExtractParam(cs, "Password", "Pwd", "Pass");

        var passwordHash = !string.IsNullOrWhiteSpace(password) ? HashPassword(password) : null;
        var masked = MaskConnectionString(cs, maskCredentials);

        return (dbType, server, database, userId, passwordHash, masked);
    }

    private static string? ExtractParam(string cs, params string[] keys)
    {
        foreach (var key in keys)
        {
            var match = Regex.Match(cs, $@"{key}\s*=\s*([^;]+)", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value.Trim();
        }
        return null;
    }

    private static string DetectDbType(string cs)
    {
        if (cs.Contains("mysql", StringComparison.OrdinalIgnoreCase)) return "mysql";
        if (cs.Contains("sqlite", StringComparison.OrdinalIgnoreCase)) return "sqlite";
        if (cs.Contains("postgresql", StringComparison.OrdinalIgnoreCase) || cs.Contains("npgsql", StringComparison.OrdinalIgnoreCase)) return "postgresql";
        if (cs.Contains("oracle", StringComparison.OrdinalIgnoreCase)) return "oracle";
        if (cs.Contains("firebird", StringComparison.OrdinalIgnoreCase) || cs.Contains("fbclient", StringComparison.OrdinalIgnoreCase)) return "firebird";
        if (cs.Contains("mssql", StringComparison.OrdinalIgnoreCase) || cs.Contains("sqlclient", StringComparison.OrdinalIgnoreCase) ||
            cs.Contains("SqlConnection", StringComparison.OrdinalIgnoreCase) || cs.Contains("Server=")) return "mssql";
        return "unknown";
    }

    private static string DetectDbTypeFromEnv(Dictionary<string, string> config)
    {
        foreach (var kv in config)
        {
            if (kv.Key.Contains("MYSQL", StringComparison.OrdinalIgnoreCase)) return "mysql";
            if (kv.Key.Contains("MSSQL", StringComparison.OrdinalIgnoreCase)) return "mssql";
            if (kv.Key.Contains("PG", StringComparison.OrdinalIgnoreCase) || kv.Key.Contains("POSTGRES", StringComparison.OrdinalIgnoreCase)) return "postgresql";
            if (kv.Key.Contains("SQLITE", StringComparison.OrdinalIgnoreCase)) return "sqlite";
            if (kv.Key.Contains("ORACLE", StringComparison.OrdinalIgnoreCase)) return "oracle";
        }
        return "unknown";
    }

    private static string? MaskConnectionString(string cs, bool mask)
    {
        if (!mask) return cs;
        return Regex.Replace(cs, @"(?i)(Password|Pwd|Pass)\s*=\s*[^;]+", "$1=***");
    }

    private static string? BuildMaskedConnectionString(string dbType, string? server, string? database,
        string? userId, bool mask)
    {
        if (string.IsNullOrWhiteSpace(server) && string.IsNullOrWhiteSpace(database)) return null;

        return dbType switch
        {
            "mssql" => $"Server={server};Database={database};User Id={(mask ? "***" : userId)};Password=***",
            "mysql" => $"Server={server};Database={database};Uid={(mask ? "***" : userId)};Pwd=***",
            "postgresql" => $"Host={server};Database={database};Username={(mask ? "***" : userId)};Password=***",
            "oracle" => $"Data Source={server}/{database};User Id={(mask ? "***" : userId)};Password=***",
            "firebird" => $"DataSource={server};Database={database};User={(mask ? "***" : userId)};Password=***",
            _ => $"Server={server};Database={database};User={(mask ? "***" : userId)};Password=***"
        };
    }

    private static string HashPassword(string password)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexStringLower(hash);
    }
}