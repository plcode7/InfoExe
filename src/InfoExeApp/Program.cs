using Microsoft.Data.Sqlite;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0].ToLowerInvariant();
var dbPath = GetOption(args, "--db") ?? Path.Combine(Environment.CurrentDirectory, "infoexe.db");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
InitializeDatabase(dbPath);

return command switch
{
    "scan" => RunScan(args, dbPath),
    "status" => RunStatus(args, dbPath),
    _ => UnknownCommand(command)
};

static int RunScan(string[] args, string dbPath)
{
    var rootPath = GetOption(args, "--root");
    if (string.IsNullOrWhiteSpace(rootPath))
    {
        Console.Error.WriteLine("Missing required option: --root <path>");
        return 2;
    }

    var absoluteRoot = Path.GetFullPath(rootPath);
    if (!Directory.Exists(absoluteRoot))
    {
        Console.Error.WriteLine($"Directory not found: {absoluteRoot}");
        return 2;
    }

    var scanId = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
    using var connection = OpenConnection(dbPath);
    using var tx = connection.BeginTransaction();

    using (var cmd = connection.CreateCommand())
    {
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO scan_jobs(scan_id, root_path, status, started_at_utc, finished_at_utc)
            VALUES($scanId, $rootPath, 'running', $startedAt, NULL);
            """;
        cmd.Parameters.AddWithValue("$scanId", scanId);
        cmd.Parameters.AddWithValue("$rootPath", absoluteRoot);
        cmd.Parameters.AddWithValue("$startedAt", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    var files = EnumerateCandidateFiles(absoluteRoot);
    foreach (var file in files)
    {
        var fileType = ClassifyFile(file);
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO scan_files(scan_id, file_path, file_type, status, reason_code)
            VALUES($scanId, $filePath, $fileType, $status, $reasonCode);
            """;
        cmd.Parameters.AddWithValue("$scanId", scanId);
        cmd.Parameters.AddWithValue("$filePath", file);
        cmd.Parameters.AddWithValue("$fileType", fileType.FileType);
        cmd.Parameters.AddWithValue("$status", fileType.Status);
        cmd.Parameters.AddWithValue("$reasonCode", fileType.ReasonCode ?? string.Empty);
        cmd.ExecuteNonQuery();
    }

    using (var cmd = connection.CreateCommand())
    {
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE scan_jobs
            SET status = 'completed', finished_at_utc = $finishedAt
            WHERE scan_id = $scanId;
            """;
        cmd.Parameters.AddWithValue("$finishedAt", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$scanId", scanId);
        cmd.ExecuteNonQuery();
    }

    tx.Commit();

    Console.WriteLine($"scanId: {scanId}");
    Console.WriteLine($"rootPath: {absoluteRoot}");
    return 0;
}

static int RunStatus(string[] args, string dbPath)
{
    var scanId = GetOption(args, "--id");
    if (string.IsNullOrWhiteSpace(scanId))
    {
        Console.Error.WriteLine("Missing required option: --id <scanId>");
        return 2;
    }

    using var connection = OpenConnection(dbPath);
    using var jobCmd = connection.CreateCommand();
    jobCmd.CommandText = """
        SELECT scan_id, root_path, status, started_at_utc, finished_at_utc
        FROM scan_jobs
        WHERE scan_id = $scanId;
        """;
    jobCmd.Parameters.AddWithValue("$scanId", scanId);

    using var reader = jobCmd.ExecuteReader();
    if (!reader.Read())
    {
        Console.Error.WriteLine($"scanId not found: {scanId}");
        return 3;
    }

    Console.WriteLine($"scanId: {reader.GetString(0)}");
    Console.WriteLine($"rootPath: {reader.GetString(1)}");
    Console.WriteLine($"status: {reader.GetString(2)}");
    Console.WriteLine($"startedAtUtc: {reader.GetString(3)}");
    Console.WriteLine($"finishedAtUtc: {(reader.IsDBNull(4) ? "-" : reader.GetString(4))}");

    using var statsCmd = connection.CreateCommand();
    statsCmd.CommandText = """
        SELECT
            COUNT(*) AS discovered,
            SUM(CASE WHEN status = 'processed' THEN 1 ELSE 0 END) AS processed,
            SUM(CASE WHEN status = 'partial' THEN 1 ELSE 0 END) AS partial,
            SUM(CASE WHEN status = 'failed' THEN 1 ELSE 0 END) AS failed
        FROM scan_files
        WHERE scan_id = $scanId;
        """;
    statsCmd.Parameters.AddWithValue("$scanId", scanId);

    using var statsReader = statsCmd.ExecuteReader();
    if (statsReader.Read())
    {
        Console.WriteLine($"discovered: {statsReader.GetInt64(0)}");
        Console.WriteLine($"processed: {statsReader.GetInt64(1)}");
        Console.WriteLine($"partial: {statsReader.GetInt64(2)}");
        Console.WriteLine($"failed: {statsReader.GetInt64(3)}");
    }

    return 0;
}

static (string FileType, string Status, string? ReasonCode) ClassifyFile(string path)
{
    var ext = Path.GetExtension(path).ToLowerInvariant();
    if (ext is ".py" or ".whl")
    {
        return ("python-artifact", "processed", null);
    }

    if (ext is ".dll" or ".exe")
    {
        try
        {
            // Passive metadata check only: no execution of target binary.
            _ = System.Reflection.AssemblyName.GetAssemblyName(path);
            return ("dotnet-managed", "processed", null);
        }
        catch (BadImageFormatException)
        {
            return ("native-or-unsupported", "partial", "unsupported-format");
        }
        catch (FileLoadException)
        {
            return ("native-or-unsupported", "partial", "metadata-unreadable");
        }
        catch (IOException)
        {
            return ("native-or-unsupported", "failed", "io-error");
        }
        catch (UnauthorizedAccessException)
        {
            return ("native-or-unsupported", "failed", "access-denied");
        }
    }

    return ("ignored", "partial", "unsupported-extension");
}

static IEnumerable<string> EnumerateCandidateFiles(string rootPath)
{
    var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".py", ".whl"
    };

    foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
    {
        var ext = Path.GetExtension(file);
        if (allowedExtensions.Contains(ext))
        {
            yield return file;
        }
    }
}

static SqliteConnection OpenConnection(string dbPath)
{
    var builder = new SqliteConnectionStringBuilder
    {
        DataSource = dbPath,
        Mode = SqliteOpenMode.ReadWriteCreate
    };
    var connection = new SqliteConnection(builder.ToString());
    connection.Open();
    return connection;
}

static void InitializeDatabase(string dbPath)
{
    using var connection = OpenConnection(dbPath);
    using var cmd = connection.CreateCommand();
    cmd.CommandText = """
        CREATE TABLE IF NOT EXISTS scan_jobs(
            scan_id TEXT PRIMARY KEY,
            root_path TEXT NOT NULL,
            status TEXT NOT NULL,
            started_at_utc TEXT NOT NULL,
            finished_at_utc TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS scan_files(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            scan_id TEXT NOT NULL,
            file_path TEXT NOT NULL,
            file_type TEXT NOT NULL,
            status TEXT NOT NULL,
            reason_code TEXT NOT NULL,
            FOREIGN KEY(scan_id) REFERENCES scan_jobs(scan_id)
        );
        """;
    cmd.ExecuteNonQuery();
}

static string? GetOption(string[] args, string optionName)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
        {
            var next = i + 1;
            if (next < args.Length)
            {
                return args[next];
            }
        }
    }

    return null;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    PrintUsage();
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("InfoExe CLI");
    Console.WriteLine("Usage:");
    Console.WriteLine("  scan --root <path> [--db <file>]");
    Console.WriteLine("  status --id <scanId> [--db <file>]");
}
