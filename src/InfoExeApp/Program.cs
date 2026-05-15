using System.Diagnostics;
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

    InsertScanJob(connection, tx, scanId, absoluteRoot);

    foreach (var file in EnumerateCandidateFiles(absoluteRoot))
    {
        var classification = ClassifyFile(file);
        var scanFileId = InsertScanFile(connection, tx, scanId, file, classification);

        if (classification.FileType == "dotnet-managed")
        {
            var metadata = ExtractManagedMetadata(file, classification);
            InsertAssemblyMetadata(connection, tx, scanFileId, metadata);

            var evidence = BuildVendorEvidence(file, metadata);
            InsertVendorEvidence(connection, tx, scanFileId, evidence);
            InsertVendorResult(connection, tx, scanFileId, evidence);
        }
    }

    CompleteScanJob(connection, tx, scanId);
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

    using var metaCmd = connection.CreateCommand();
    metaCmd.CommandText = """
        SELECT COUNT(*)
        FROM assembly_metadata m
        JOIN scan_files f ON f.id = m.scan_file_id
        WHERE f.scan_id = $scanId;
        """;
    metaCmd.Parameters.AddWithValue("$scanId", scanId);
    Console.WriteLine($"managedMetadata: {Convert.ToInt64(metaCmd.ExecuteScalar())}");

    using var vendorCmd = connection.CreateCommand();
    vendorCmd.CommandText = """
        SELECT
            SUM(CASE WHEN vr.status = 'attributed' THEN 1 ELSE 0 END),
            SUM(CASE WHEN vr.status = 'inconclusive' THEN 1 ELSE 0 END)
        FROM vendor_results vr
        JOIN scan_files f ON f.id = vr.scan_file_id
        WHERE f.scan_id = $scanId;
        """;
    vendorCmd.Parameters.AddWithValue("$scanId", scanId);
    using var vendorReader = vendorCmd.ExecuteReader();
    if (vendorReader.Read())
    {
        Console.WriteLine($"vendorAttributed: {vendorReader.GetInt64(0)}");
        Console.WriteLine($"vendorInconclusive: {vendorReader.GetInt64(1)}");
    }

    return 0;
}

static void InsertScanJob(SqliteConnection connection, SqliteTransaction tx, string scanId, string rootPath)
{
    using var cmd = connection.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = """
        INSERT INTO scan_jobs(scan_id, root_path, status, started_at_utc, finished_at_utc)
        VALUES($scanId, $rootPath, 'running', $startedAt, NULL);
        """;
    cmd.Parameters.AddWithValue("$scanId", scanId);
    cmd.Parameters.AddWithValue("$rootPath", rootPath);
    cmd.Parameters.AddWithValue("$startedAt", DateTime.UtcNow.ToString("O"));
    cmd.ExecuteNonQuery();
}

static void CompleteScanJob(SqliteConnection connection, SqliteTransaction tx, string scanId)
{
    using var cmd = connection.CreateCommand();
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

static long InsertScanFile(
    SqliteConnection connection,
    SqliteTransaction tx,
    string scanId,
    string filePath,
    ClassificationResult classification)
{
    using var cmd = connection.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = """
        INSERT INTO scan_files(scan_id, file_path, file_type, status, reason_code)
        VALUES($scanId, $filePath, $fileType, $status, $reasonCode);
        SELECT last_insert_rowid();
        """;
    cmd.Parameters.AddWithValue("$scanId", scanId);
    cmd.Parameters.AddWithValue("$filePath", filePath);
    cmd.Parameters.AddWithValue("$fileType", classification.FileType);
    cmd.Parameters.AddWithValue("$status", classification.Status);
    cmd.Parameters.AddWithValue("$reasonCode", classification.ReasonCode ?? string.Empty);
    return Convert.ToInt64(cmd.ExecuteScalar());
}

static void InsertAssemblyMetadata(
    SqliteConnection connection,
    SqliteTransaction tx,
    long scanFileId,
    ManagedMetadata metadata)
{
    using var cmd = connection.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = """
        INSERT INTO assembly_metadata(
            scan_file_id, assembly_name, assembly_version, public_key_token,
            is_single_file, is_ready_to_run, is_native_aot_limited, target_framework
        )
        VALUES(
            $scanFileId, $name, $version, $token,
            $isSingleFile, $isReadyToRun, $isNativeAot, $targetFramework
        );
        """;
    cmd.Parameters.AddWithValue("$scanFileId", scanFileId);
    cmd.Parameters.AddWithValue("$name", metadata.AssemblyName);
    cmd.Parameters.AddWithValue("$version", metadata.AssemblyVersion);
    cmd.Parameters.AddWithValue("$token", metadata.PublicKeyToken);
    cmd.Parameters.AddWithValue("$isSingleFile", metadata.IsSingleFile ? 1 : 0);
    cmd.Parameters.AddWithValue("$isReadyToRun", metadata.IsReadyToRun ? 1 : 0);
    cmd.Parameters.AddWithValue("$isNativeAot", metadata.IsNativeAotLimited ? 1 : 0);
    cmd.Parameters.AddWithValue("$targetFramework", metadata.TargetFramework);
    cmd.ExecuteNonQuery();
}

static void InsertVendorEvidence(
    SqliteConnection connection,
    SqliteTransaction tx,
    long scanFileId,
    IReadOnlyList<VendorEvidence> evidenceList)
{
    foreach (var evidence in evidenceList)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO vendor_evidence(scan_file_id, evidence_type, vendor_name, confidence)
            VALUES($scanFileId, $type, $vendorName, $confidence);
            """;
        cmd.Parameters.AddWithValue("$scanFileId", scanFileId);
        cmd.Parameters.AddWithValue("$type", evidence.EvidenceType);
        cmd.Parameters.AddWithValue("$vendorName", evidence.VendorName);
        cmd.Parameters.AddWithValue("$confidence", evidence.Confidence);
        cmd.ExecuteNonQuery();
    }
}

static void InsertVendorResult(
    SqliteConnection connection,
    SqliteTransaction tx,
    long scanFileId,
    IReadOnlyList<VendorEvidence> evidenceList)
{
    var distinctVendors = evidenceList
        .Select(e => e.VendorName)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    var status = "inconclusive";
    var vendorName = "inconclusive";
    var confidence = 0;

    if (distinctVendors.Count == 1)
    {
        status = "attributed";
        vendorName = distinctVendors[0];
        confidence = evidenceList.Max(e => e.Confidence);
    }

    using var cmd = connection.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = """
        INSERT INTO vendor_results(scan_file_id, status, vendor_name, confidence)
        VALUES($scanFileId, $status, $vendorName, $confidence);
        """;
    cmd.Parameters.AddWithValue("$scanFileId", scanFileId);
    cmd.Parameters.AddWithValue("$status", status);
    cmd.Parameters.AddWithValue("$vendorName", vendorName);
    cmd.Parameters.AddWithValue("$confidence", confidence);
    cmd.ExecuteNonQuery();
}

static ClassificationResult ClassifyFile(string path)
{
    var ext = Path.GetExtension(path).ToLowerInvariant();

    if (ext is ".py" or ".whl")
    {
        return new ClassificationResult("python-artifact", "processed", null, null);
    }

    if (ext is ".dll" or ".exe")
    {
        try
        {
            // Passive metadata check only: no execution of target binary.
            var assemblyName = System.Reflection.AssemblyName.GetAssemblyName(path);
            return new ClassificationResult("dotnet-managed", "processed", null, assemblyName);
        }
        catch (BadImageFormatException)
        {
            return new ClassificationResult("native-or-unsupported", "partial", "unsupported-format", null);
        }
        catch (FileLoadException)
        {
            return new ClassificationResult("native-or-unsupported", "partial", "metadata-unreadable", null);
        }
        catch (IOException)
        {
            return new ClassificationResult("native-or-unsupported", "failed", "io-error", null);
        }
        catch (UnauthorizedAccessException)
        {
            return new ClassificationResult("native-or-unsupported", "failed", "access-denied", null);
        }
    }

    return new ClassificationResult("ignored", "partial", "unsupported-extension", null);
}

static ManagedMetadata ExtractManagedMetadata(string path, ClassificationResult classification)
{
    var assembly = classification.AssemblyName!;
    var token = assembly.GetPublicKeyToken();
    var publicKeyToken = token is { Length: > 0 }
        ? BitConverter.ToString(token).Replace("-", "").ToLowerInvariant()
        : string.Empty;

    var fileVersion = FileVersionInfo.GetVersionInfo(path);
    var targetFramework = string.Empty;
    var isSingleFile = false;
    var isReadyToRun = false;
    var isNativeAotLimited = false;

    // Lightweight capability hints; deeper detection is handled in later phases.
    if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(publicKeyToken))
    {
        isNativeAotLimited = false;
    }

    return new ManagedMetadata(
        assembly.Name ?? Path.GetFileNameWithoutExtension(path),
        assembly.Version?.ToString() ?? string.Empty,
        publicKeyToken,
        isSingleFile,
        isReadyToRun,
        isNativeAotLimited,
        targetFramework,
        fileVersion.CompanyName ?? string.Empty);
}

static IReadOnlyList<VendorEvidence> BuildVendorEvidence(string path, ManagedMetadata metadata)
{
    var evidence = new List<VendorEvidence>();

    if (!string.IsNullOrWhiteSpace(metadata.CompanyName))
    {
        evidence.Add(new VendorEvidence("file-version-company", metadata.CompanyName, 60));
    }

    if (!string.IsNullOrWhiteSpace(metadata.PublicKeyToken))
    {
        evidence.Add(new VendorEvidence("assembly-public-key-token", metadata.PublicKeyToken, 35));
    }

    if (evidence.Count == 0)
    {
        var fallback = Path.GetFileNameWithoutExtension(path);
        evidence.Add(new VendorEvidence("filename-fallback", fallback, 20));
    }

    return evidence;
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

        CREATE TABLE IF NOT EXISTS assembly_metadata(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            scan_file_id INTEGER NOT NULL,
            assembly_name TEXT NOT NULL,
            assembly_version TEXT NOT NULL,
            public_key_token TEXT NOT NULL,
            is_single_file INTEGER NOT NULL,
            is_ready_to_run INTEGER NOT NULL,
            is_native_aot_limited INTEGER NOT NULL,
            target_framework TEXT NOT NULL,
            FOREIGN KEY(scan_file_id) REFERENCES scan_files(id)
        );

        CREATE TABLE IF NOT EXISTS vendor_evidence(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            scan_file_id INTEGER NOT NULL,
            evidence_type TEXT NOT NULL,
            vendor_name TEXT NOT NULL,
            confidence INTEGER NOT NULL,
            FOREIGN KEY(scan_file_id) REFERENCES scan_files(id)
        );

        CREATE TABLE IF NOT EXISTS vendor_results(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            scan_file_id INTEGER NOT NULL,
            status TEXT NOT NULL,
            vendor_name TEXT NOT NULL,
            confidence INTEGER NOT NULL,
            FOREIGN KEY(scan_file_id) REFERENCES scan_files(id)
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

internal sealed record ClassificationResult(
    string FileType,
    string Status,
    string? ReasonCode,
    System.Reflection.AssemblyName? AssemblyName);

internal sealed record ManagedMetadata(
    string AssemblyName,
    string AssemblyVersion,
    string PublicKeyToken,
    bool IsSingleFile,
    bool IsReadyToRun,
    bool IsNativeAotLimited,
    string TargetFramework,
    string CompanyName);

internal sealed record VendorEvidence(
    string EvidenceType,
    string VendorName,
    int Confidence);
