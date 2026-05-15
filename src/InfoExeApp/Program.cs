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
    "retry" => RunRetry(args, dbPath),
    "export" => RunExport(args, dbPath),
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

    var includePython = HasFlag(args, "--include-python");
    foreach (var file in EnumerateCandidateFiles(absoluteRoot, includePython))
    {
        var classification = ClassifyFile(file);
        var scanFileId = InsertScanFile(connection, tx, scanId, file, classification);

        if (classification.FileType != "dotnet-managed")
        {
            continue;
        }

        var metadata = ExtractManagedMetadata(file, classification);
        InsertAssemblyMetadata(connection, tx, scanFileId, metadata);

        var evidence = BuildVendorEvidence(file, metadata);
        InsertVendorEvidence(connection, tx, scanFileId, evidence);
        InsertVendorResult(connection, tx, scanFileId, evidence);

        var decompileResult = AttemptDecompile(file, scanId, scanFileId);
        InsertDecompileResult(connection, tx, scanFileId, decompileResult);

        if (decompileResult.Status != "decompiled")
        {
            MarkPartialWithReason(connection, tx, scanFileId, decompileResult.ReasonCode);
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

    Console.WriteLine($"managedMetadata: {ScalarLong(connection, """
        SELECT COUNT(*) FROM assembly_metadata m
        JOIN scan_files f ON f.id = m.scan_file_id
        WHERE f.scan_id = $scanId;
        """, scanId)}");

    Console.WriteLine($"vendorAttributed: {ScalarLong(connection, """
        SELECT SUM(CASE WHEN vr.status = 'attributed' THEN 1 ELSE 0 END)
        FROM vendor_results vr
        JOIN scan_files f ON f.id = vr.scan_file_id
        WHERE f.scan_id = $scanId;
        """, scanId)}");

    Console.WriteLine($"vendorInconclusive: {ScalarLong(connection, """
        SELECT SUM(CASE WHEN vr.status = 'inconclusive' THEN 1 ELSE 0 END)
        FROM vendor_results vr
        JOIN scan_files f ON f.id = vr.scan_file_id
        WHERE f.scan_id = $scanId;
        """, scanId)}");

    Console.WriteLine($"decompiled: {ScalarLong(connection, """
        SELECT SUM(CASE WHEN d.status = 'decompiled' THEN 1 ELSE 0 END)
        FROM decompilation_results d
        JOIN scan_files f ON f.id = d.scan_file_id
        WHERE f.scan_id = $scanId;
        """, scanId)}");

    return 0;
}

static int RunRetry(string[] args, string dbPath)
{
    var scanId = GetOption(args, "--id");
    if (string.IsNullOrWhiteSpace(scanId))
    {
        Console.Error.WriteLine("Missing required option: --id <scanId>");
        return 2;
    }

    using var connection = OpenConnection(dbPath);
    using var query = connection.CreateCommand();
    query.CommandText = """
        SELECT id, file_path, retry_count
        FROM scan_files
        WHERE scan_id = $scanId
          AND file_type = 'dotnet-managed'
          AND status IN ('partial','failed')
          AND retry_count < 3;
        """;
    query.Parameters.AddWithValue("$scanId", scanId);

    var candidates = new List<(long Id, string Path, long RetryCount)>();
    using (var reader = query.ExecuteReader())
    {
        while (reader.Read())
        {
            candidates.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2)));
        }
    }

    if (candidates.Count == 0)
    {
        Console.WriteLine("No retry candidates.");
        return 0;
    }

    using var tx = connection.BeginTransaction();
    var retried = 0;
    foreach (var candidate in candidates)
    {
        var result = AttemptDecompile(candidate.Path, scanId, candidate.Id);
        UpsertDecompileResult(connection, tx, candidate.Id, result);

        using var update = connection.CreateCommand();
        update.Transaction = tx;
        update.CommandText = """
            UPDATE scan_files
            SET status = $status,
                reason_code = $reason,
                retry_count = retry_count + 1
            WHERE id = $id;
            """;
        update.Parameters.AddWithValue("$status", result.Status == "decompiled" ? "processed" : "partial");
        update.Parameters.AddWithValue("$reason", result.ReasonCode ?? string.Empty);
        update.Parameters.AddWithValue("$id", candidate.Id);
        update.ExecuteNonQuery();
        retried++;
    }

    tx.Commit();
    Console.WriteLine($"retried: {retried}");
    return 0;
}

static int RunExport(string[] args, string dbPath)
{
    var scanId = GetOption(args, "--id");
    if (string.IsNullOrWhiteSpace(scanId))
    {
        Console.Error.WriteLine("Missing required option: --id <scanId>");
        return 2;
    }

    var formatArg = (GetOption(args, "--format") ?? "json").ToLowerInvariant();
    var formats = formatArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var outputArg = GetOption(args, "--output") ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "reports");
    var outputPath = Path.GetFullPath(outputArg);

    using var connection = OpenConnection(dbPath);
    var items = new List<ExportRow>();
    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = """
            SELECT
                f.file_path,
                f.file_type,
                f.status,
                f.reason_code,
                f.retry_count,
                COALESCE(m.assembly_name, ''),
                COALESCE(m.assembly_version, ''),
                COALESCE(m.public_key_token, ''),
                COALESCE(vr.vendor_name, ''),
                COALESCE(vr.status, ''),
                COALESCE(vr.confidence, 0),
                COALESCE(d.status, ''),
                COALESCE(d.artifact_path, ''),
                COALESCE(d.reason_code, '')
            FROM scan_files f
            LEFT JOIN assembly_metadata m ON m.scan_file_id = f.id
            LEFT JOIN vendor_results vr ON vr.scan_file_id = f.id
            LEFT JOIN decompilation_results d ON d.scan_file_id = f.id
            WHERE f.scan_id = $scanId
            ORDER BY f.id;
            """;
        cmd.Parameters.AddWithValue("$scanId", scanId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new ExportRow(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetString(8), reader.GetString(9), reader.GetInt64(10),
                reader.GetString(11), reader.GetString(12), reader.GetString(13)));
        }
    }

    if (items.Count == 0)
    {
        Console.Error.WriteLine($"No files found for scanId: {scanId}");
        return 3;
    }

    var writes = 0;
    foreach (var format in formats)
    {
        if (format == "json")
        {
            var path = ResolveOutputFile(outputPath, formats.Length > 1, $"{scanId}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var payload = new { scanId, generatedAtUtc = DateTime.UtcNow.ToString("O"), items };
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"exportJson: {path}");
            writes++;
        }
        else if (format == "csv")
        {
            var path = ResolveOutputFile(outputPath, formats.Length > 1, $"{scanId}.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var sw = new StreamWriter(path, false);
            sw.WriteLine("file_path,file_type,status,reason_code,retry_count,assembly_name,assembly_version,public_key_token,vendor_name,vendor_status,vendor_confidence,decompile_status,decompile_artifact,decompile_reason");
            foreach (var row in items)
            {
                sw.WriteLine(string.Join(',',
                    Csv(row.FilePath), Csv(row.FileType), Csv(row.Status), Csv(row.ReasonCode), row.RetryCount,
                    Csv(row.AssemblyName), Csv(row.AssemblyVersion), Csv(row.PublicKeyToken),
                    Csv(row.VendorName), Csv(row.VendorStatus), row.VendorConfidence,
                    Csv(row.DecompileStatus), Csv(row.DecompileArtifactPath), Csv(row.DecompileReason)));
            }
            Console.WriteLine($"exportCsv: {path}");
            writes++;
        }
    }

    if (writes == 0)
    {
        Console.Error.WriteLine("Unsupported format. Use: json, csv, or json,csv");
        return 2;
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
        INSERT INTO scan_files(scan_id, file_path, file_type, status, reason_code, retry_count)
        VALUES($scanId, $filePath, $fileType, $status, $reasonCode, 0);
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
    var distinctVendors = evidenceList.Select(e => e.VendorName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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

static void InsertDecompileResult(SqliteConnection connection, SqliteTransaction tx, long scanFileId, DecompileResult result)
{
    using var cmd = connection.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = """
        INSERT INTO decompilation_results(scan_file_id, status, artifact_path, reason_code)
        VALUES($scanFileId, $status, $artifact, $reason);
        """;
    cmd.Parameters.AddWithValue("$scanFileId", scanFileId);
    cmd.Parameters.AddWithValue("$status", result.Status);
    cmd.Parameters.AddWithValue("$artifact", result.ArtifactPath);
    cmd.Parameters.AddWithValue("$reason", result.ReasonCode ?? string.Empty);
    cmd.ExecuteNonQuery();
}

static void UpsertDecompileResult(SqliteConnection connection, SqliteTransaction tx, long scanFileId, DecompileResult result)
{
    using var cmd = connection.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = """
        UPDATE decompilation_results
        SET status = $status, artifact_path = $artifact, reason_code = $reason
        WHERE scan_file_id = $scanFileId;

        INSERT INTO decompilation_results(scan_file_id, status, artifact_path, reason_code)
        SELECT $scanFileId, $status, $artifact, $reason
        WHERE NOT EXISTS(SELECT 1 FROM decompilation_results WHERE scan_file_id = $scanFileId);
        """;
    cmd.Parameters.AddWithValue("$scanFileId", scanFileId);
    cmd.Parameters.AddWithValue("$status", result.Status);
    cmd.Parameters.AddWithValue("$artifact", result.ArtifactPath);
    cmd.Parameters.AddWithValue("$reason", result.ReasonCode ?? string.Empty);
    cmd.ExecuteNonQuery();
}

static void MarkPartialWithReason(SqliteConnection connection, SqliteTransaction tx, long scanFileId, string? reasonCode)
{
    using var cmd = connection.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = """
        UPDATE scan_files
        SET status = 'partial', reason_code = $reason
        WHERE id = $id;
        """;
    cmd.Parameters.AddWithValue("$reason", reasonCode ?? string.Empty);
    cmd.Parameters.AddWithValue("$id", scanFileId);
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
    var publicKeyToken = token is { Length: > 0 } ? BitConverter.ToString(token).Replace("-", "").ToLowerInvariant() : string.Empty;
    var companyName = FileVersionInfo.GetVersionInfo(path).CompanyName ?? string.Empty;

    return new ManagedMetadata(
        assembly.Name ?? Path.GetFileNameWithoutExtension(path),
        assembly.Version?.ToString() ?? string.Empty,
        publicKeyToken,
        false,
        false,
        false,
        string.Empty,
        companyName);
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
        evidence.Add(new VendorEvidence("filename-fallback", Path.GetFileNameWithoutExtension(path), 20));
    }

    return evidence;
}

static DecompileResult AttemptDecompile(string filePath, string scanId, long scanFileId)
{
    var outDir = Path.Combine(Environment.CurrentDirectory, "artifacts", "decompiled", scanId, scanFileId.ToString());
    Directory.CreateDirectory(outDir);

    var ilspyPath = FindExecutable("ilspycmd");
    if (ilspyPath is null)
    {
        var notePath = Path.Combine(outDir, "decompile-note.txt");
        File.WriteAllText(notePath, "ilspycmd not found; decompilation deferred.");
        return new DecompileResult("partial", notePath, "decompile-tool-missing");
    }

    var psi = new ProcessStartInfo
    {
        FileName = ilspyPath,
        Arguments = $"--disable-updatecheck -p -o \"{outDir}\" \"{filePath}\"",
        UseShellExecute = false,
        RedirectStandardError = true,
        RedirectStandardOutput = true
    };

    using var process = Process.Start(psi);
    if (process is null)
    {
        return new DecompileResult("partial", outDir, "decompile-start-failed");
    }

    if (!process.WaitForExit(60000))
    {
        process.Kill(true);
        return new DecompileResult("partial", outDir, "decompile-timeout");
    }

    return process.ExitCode == 0
        ? new DecompileResult("decompiled", outDir, null)
        : new DecompileResult("partial", outDir, "decompile-failed");
}

static string? FindExecutable(string commandName)
{
    var pathValue = Environment.GetEnvironmentVariable("PATH");
    if (string.IsNullOrWhiteSpace(pathValue))
    {
        return null;
    }

    foreach (var path in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
        var candidate = Path.Combine(path.Trim(), $"{commandName}.exe");
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return null;
}

static IEnumerable<string> EnumerateCandidateFiles(string rootPath, bool includePython)
{
    var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".dll", ".exe" };
    if (includePython)
    {
        allowedExtensions.Add(".py");
        allowedExtensions.Add(".whl");
    }
    foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
    {
        if (allowedExtensions.Contains(Path.GetExtension(file)))
        {
            yield return file;
        }
    }
}

static SqliteConnection OpenConnection(string dbPath)
{
    var builder = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate };
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
            retry_count INTEGER NOT NULL DEFAULT 0,
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

        CREATE TABLE IF NOT EXISTS decompilation_results(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            scan_file_id INTEGER NOT NULL UNIQUE,
            status TEXT NOT NULL,
            artifact_path TEXT NOT NULL,
            reason_code TEXT NOT NULL,
            FOREIGN KEY(scan_file_id) REFERENCES scan_files(id)
        );
        """;
    cmd.ExecuteNonQuery();
}

static long ScalarLong(SqliteConnection connection, string sql, string scanId)
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("$scanId", scanId);
    var value = cmd.ExecuteScalar();
    return value is null or DBNull ? 0 : Convert.ToInt64(value);
}

static string? GetOption(string[] args, string optionName)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            return args[i + 1];
        }
    }
    return null;
}

static bool HasFlag(string[] args, string flag)
{
    return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
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
    Console.WriteLine("  scan --root <path> [--include-python] [--db <file>]");
    Console.WriteLine("  status --id <scanId> [--db <file>]");
    Console.WriteLine("  retry --id <scanId> [--db <file>]");
    Console.WriteLine("  export --id <scanId> [--format json|csv|json,csv] [--output <path>]");
}

static string ResolveOutputFile(string outputPath, bool forceDirectory, string defaultFileName)
{
    if (forceDirectory || Directory.Exists(outputPath) || !Path.HasExtension(outputPath))
    {
        return Path.Combine(outputPath, defaultFileName);
    }

    return outputPath;
}

static string Csv(string value)
{
    var escaped = value.Replace("\"", "\"\"");
    return $"\"{escaped}\"";
}

internal sealed record ClassificationResult(string FileType, string Status, string? ReasonCode, System.Reflection.AssemblyName? AssemblyName);
internal sealed record ManagedMetadata(string AssemblyName, string AssemblyVersion, string PublicKeyToken, bool IsSingleFile, bool IsReadyToRun, bool IsNativeAotLimited, string TargetFramework, string CompanyName);
internal sealed record VendorEvidence(string EvidenceType, string VendorName, int Confidence);
internal sealed record DecompileResult(string Status, string ArtifactPath, string? ReasonCode);
internal sealed record ExportRow(
    string FilePath, string FileType, string Status, string ReasonCode, long RetryCount,
    string AssemblyName, string AssemblyVersion, string PublicKeyToken,
    string VendorName, string VendorStatus, long VendorConfidence,
    string DecompileStatus, string DecompileArtifactPath, string DecompileReason);
