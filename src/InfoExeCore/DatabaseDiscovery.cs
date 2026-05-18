using System.Diagnostics;
using InfoExeCore.Models;

namespace InfoExeCore;

/// <summary>
/// Discovers local databases: MySQL, SQL Server, SQLite, PostgreSQL, Oracle, Firebird, etc.
/// Scans for database files, service instances, and well-known data directories.
/// </summary>
public static class DatabaseDiscoveryService
{
    public static List<DatabaseDiscovery> DiscoverAll(Action<string, bool>? log = null)
    {
        var results = new List<DatabaseDiscovery>();
        var discovered = DateTime.UtcNow;

        // 1. SQLite databases (file-based)
        DiscoverSqliteDatabases(results, discovered, log);

        // 2. MySQL databases
        DiscoverMySqlDatabases(results, discovered, log);

        // 3. SQL Server local instances
        DiscoverSqlServerInstances(results, discovered, log);

        // 4. PostgreSQL instances
        DiscoverPostgreSqlInstances(results, discovered, log);

        // 5. Firebird databases
        DiscoverFirebirdDatabases(results, discovered, log);

        // 6. Oracle databases
        DiscoverOracleInstances(results, discovered, log);

        // 7. Generic database files by extension
        DiscoverGenericDbFiles(results, discovered, log);

        return results;
    }

    private static void DiscoverSqliteDatabases(List<DatabaseDiscovery> results, DateTime discovered, Action<string, bool>? log)
    {
        log?.Invoke("Scanning for SQLite databases...", false);

        var searchPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            @"C:\ProgramData",
        };

        foreach (var searchPath in searchPaths)
        {
            if (!Directory.Exists(searchPath)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(searchPath, "*.db", SearchOption.AllDirectories)
                    .Concat(Directory.EnumerateFiles(searchPath, "*.sqlite", SearchOption.AllDirectories))
                    .Concat(Directory.EnumerateFiles(searchPath, "*.sqlite3", SearchOption.AllDirectories))
                    .Take(500)) // limit per path
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Length < 100) continue; // skip empty/tiny DBs

                        var dbName = Path.GetFileNameWithoutExtension(file);
                        results.Add(new DatabaseDiscovery(
                            $"sqlite-{Guid.NewGuid():N}",
                            "sqlite",
                            file,
                            dbName,
                            null,
                            null,
                            info.Length,
                            DetectSqliteVersion(file),
                            "file_only",
                            null,
                            discovered
                        ));
                    }
                    catch { /* skip inaccessible files */ }
                }
            }
            catch { /* skip inaccessible directories */ }
        }
    }

    private static string? DetectSqliteVersion(string dbPath)
    {
        try
        {
            using var fs = File.OpenRead(dbPath);
            var header = new byte[96];
            if (fs.Read(header, 0, 96) >= 96)
            {
                // SQLite header: "SQLite format 3\0" at offset 0
                if (header[0] == 'S' && header[1] == 'Q' && header[2] == 'L')
                {
                    var version = System.Text.Encoding.ASCII.GetString(header, 0, 16).Trim('\0');
                    return version;
                }
            }
        }
        catch { }
        return null;
    }

    private static void DiscoverMySqlDatabases(List<DatabaseDiscovery> results, DateTime discovered, Action<string, bool>? log)
    {
        log?.Invoke("Scanning for MySQL instances...", false);

        // Check for MySQL service
        if (IsServiceRunning("MySQL") || IsServiceRunning("MySQL80") || IsServiceRunning("MariaDB"))
        {
            results.Add(new DatabaseDiscovery(
                "mysql-service",
                "mysql",
                "localhost:3306",
                "MySQL Service",
                "Server=localhost;Port=3306;",
                null, null, GetMySqlVersion(),
                "accessible",
                null, discovered
            ));
        }

        // Check well-known data directories
        var mySqlDataDirs = new[]
        {
            @"C:\ProgramData\MySQL\MySQL Server 8.0\Data",
            @"C:\ProgramData\MySQL\MySQL Server 5.7\Data",
            @"C:\Program Files\MySQL\MySQL Server 8.0\data",
            @"C:\xampp\mysql\data",
            @"C:\wamp64\bin\mysql\mysql8.0.31\data",
            @"C:\wamp\bin\mysql\mysql5.7.36\data",
        };

        foreach (var dir in mySqlDataDirs)
        {
            if (!Directory.Exists(dir)) continue;
            results.Add(new DatabaseDiscovery(
                $"mysql-dir-{Guid.NewGuid():N}",
                "mysql",
                dir,
                Path.GetFileName(Path.GetDirectoryName(dir)),
                null, null, GetDirectorySize(dir),
                GetMySqlVersion(), "file_only", null, discovered
            ));
        }

        // Check for my.ini / my.cnf
        var configPaths = new[]
        {
            @"C:\ProgramData\MySQL\MySQL Server 8.0\my.ini",
            @"C:\ProgramData\MySQL\MySQL Server 5.7\my.ini",
            @"C:\Windows\my.ini",
            @"C:\my.cnf",
        };
        foreach (var config in configPaths)
        {
            if (File.Exists(config))
                log?.Invoke($"Found MySQL config: {config}", false);
        }
    }

    private static void DiscoverSqlServerInstances(List<DatabaseDiscovery> results, DateTime discovered, Action<string, bool>? log)
    {
        log?.Invoke("Scanning for SQL Server instances...", false);

        // Check for SQL Server services
        var serviceNames = new[] { "MSSQLSERVER", "MSSQL$SQLEXPRESS", "SQLSERVERAGENT" };
        foreach (var svc in serviceNames)
        {
            if (IsServiceRunning(svc))
            {
                results.Add(new DatabaseDiscovery(
                    $"mssql-{svc.ToLower()}",
                    "mssql",
                    $".\\{svc.Replace("MSSQL$", "")}",
                    svc,
                    $"Server=.\\{svc.Replace("MSSQL$", "")};Trusted_Connection=True;",
                    null, null, null, "accessible", null, discovered
                ));
            }
        }

        // Check for .mdf files in common locations
        var mdfPaths = new[]
        {
            @"C:\Program Files\Microsoft SQL Server",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Microsoft\Microsoft SQL Server",
        };
        foreach (var path in mdfPaths)
        {
            if (!Directory.Exists(path)) continue;
            try
            {
                foreach (var mdf in Directory.EnumerateFiles(path, "*.mdf", SearchOption.AllDirectories).Take(100))
                {
                    var info = new FileInfo(mdf);
                    results.Add(new DatabaseDiscovery(
                        $"mssql-file-{Guid.NewGuid():N}",
                        "mssql",
                        mdf,
                        Path.GetFileNameWithoutExtension(mdf),
                        null, mdf, info.Length, null,
                        "file_only", null, discovered
                    ));
                }
            }
            catch { }
        }
    }

    private static void DiscoverPostgreSqlInstances(List<DatabaseDiscovery> results, DateTime discovered, Action<string, bool>? log)
    {
        log?.Invoke("Scanning for PostgreSQL instances...", false);

        if (IsServiceRunning("postgresql"))
        {
            results.Add(new DatabaseDiscovery(
                "postgresql-service",
                "postgresql",
                "localhost:5432",
                "PostgreSQL Service",
                "Host=localhost;Port=5432;",
                null, null, GetPostgreSqlVersion(),
                "accessible", null, discovered
            ));
        }

        var pgDataDirs = new[]
        {
            @"C:\Program Files\PostgreSQL\16\data",
            @"C:\Program Files\PostgreSQL\15\data",
            @"C:\Program Files\PostgreSQL\14\data",
            @"C:\Program Files\PostgreSQL\13\data",
        };
        foreach (var dir in pgDataDirs)
        {
            if (Directory.Exists(dir))
                results.Add(new DatabaseDiscovery($"pg-dir-{Guid.NewGuid():N}", "postgresql", dir, "PostgreSQL Data", null, null, GetDirectorySize(dir), null, "file_only", null, discovered));
        }
    }

    private static void DiscoverFirebirdDatabases(List<DatabaseDiscovery> results, DateTime discovered, Action<string, bool>? log)
    {
        log?.Invoke("Scanning for Firebird databases...", false);

        if (IsServiceRunning("Firebird"))
        {
            results.Add(new DatabaseDiscovery("firebird-service", "firebird", "localhost:3050", "Firebird Service", "Database=localhost/3050:", null, null, null, "accessible", null, discovered));
        }

        try
        {
            foreach (var fdb in Directory.EnumerateFiles(@"C:\", "*.fdb", SearchOption.AllDirectories).Take(100))
            {
                var info = new FileInfo(fdb);
                results.Add(new DatabaseDiscovery($"fdb-{Guid.NewGuid():N}", "firebird", fdb, Path.GetFileNameWithoutExtension(fdb), null, null, info.Length, null, "file_only", null, discovered));
            }
        }
        catch { }
    }

    private static void DiscoverOracleInstances(List<DatabaseDiscovery> results, DateTime discovered, Action<string, bool>? log)
    {
        log?.Invoke("Scanning for Oracle instances...", false);

        var oracleServices = new[] { "OracleServiceORCL", "OracleServiceXE", "OracleOraDB21Home1TNSListener" };
        foreach (var svc in oracleServices)
        {
            if (IsServiceRunning(svc))
                results.Add(new DatabaseDiscovery($"oracle-{svc.ToLower()}", "oracle", "localhost:1521", svc, "Data Source=localhost:1521/ORCL;", null, null, null, "accessible", null, discovered));
        }

        if (Directory.Exists(@"C:\app"))
            results.Add(new DatabaseDiscovery("oracle-home", "oracle", @"C:\app", "Oracle Home", null, null, GetDirectorySize(@"C:\app"), null, "file_only", null, discovered));
    }

    private static void DiscoverGenericDbFiles(List<DatabaseDiscovery> results, DateTime discovered, Action<string, bool>? log)
    {
        log?.Invoke("Scanning for generic database files...", false);

        var extensions = new Dictionary<string, string>
        {
            [".mdb"] = "msaccess",
            [".accdb"] = "msaccess",
            [".dbf"] = "dbase",
            [".dat"] = "generic",
            [".ib"] = "interbase",
            [".gdb"] = "interbase",
        };

        var searchDirs = new[] { @"C:\ProgramData", @"C:\Program Files", @"C:\Program Files (x86)" };
        foreach (var searchDir in searchDirs)
        {
            if (!Directory.Exists(searchDir)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(searchDir, "*", SearchOption.AllDirectories).Take(500))
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (!extensions.TryGetValue(ext, out var dbType)) continue;

                    var info = new FileInfo(file);
                    if (info.Length < 1024) continue;

                    results.Add(new DatabaseDiscovery(
                        $"gen-{dbType}-{Guid.NewGuid():N}",
                        dbType,
                        file,
                        Path.GetFileNameWithoutExtension(file),
                        null, null, info.Length, null,
                        "file_only", null, discovered
                    ));
                }
            }
            catch { }
        }
    }

    // ── Helpers ──

    private static bool IsServiceRunning(string serviceName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query \"{serviceName}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            process.WaitForExit(3000);
            var output = process.StandardOutput.ReadToEnd();
            return output.Contains("RUNNING");
        }
        catch { return false; }
    }

    private static string? GetMySqlVersion()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "mysql",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return null;
            process.WaitForExit(3000);
            return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim() : null;
        }
        catch { return null; }
    }

    private static string? GetPostgreSqlVersion()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "psql",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return null;
            process.WaitForExit(3000);
            return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim() : null;
        }
        catch { return null; }
    }

    private static long GetDirectorySize(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return 0;
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
        }
        catch { return 0; }
    }
}