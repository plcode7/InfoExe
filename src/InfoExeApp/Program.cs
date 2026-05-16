using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

var dbPath = GetOption(args, "--db") ?? Path.Combine(Environment.CurrentDirectory, "infoexe.db");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
InitializeDatabase(dbPath);

if (args.Length == 0)
{
    return RunInteractiveScan(dbPath, IsPolishHelp());
}

var command = args[0].ToLowerInvariant();
if (command is "help" or "--help" or "-h")
{
    PrintUsage(IsPolishHelp());
    return 0;
}
return command switch
{
    "scan" => RunScan(args, dbPath),
    "status" => RunStatus(args, dbPath),
    "retry" => RunRetry(args, dbPath),
    "export" => RunExport(args, dbPath),
    "analyze" => RunAnalyze(args, dbPath),
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

    var programName = GetOptionWithAliases(args, "--app", "--program-name", "--name");
    var programParameters = GetOptionWithAliases(args, "--args", "--program-parameters", "--params") ?? string.Empty;
    var searchPathArg = GetOptionWithAliases(args, "--path", "--search-path");

    var absoluteRoot = Path.GetFullPath(rootPath);
    if (!Directory.Exists(absoluteRoot))
    {
        Console.Error.WriteLine($"Directory not found: {absoluteRoot}");
        return 2;
    }

    var absoluteSearchPath = absoluteRoot;
    if (!string.IsNullOrWhiteSpace(searchPathArg))
    {
        absoluteSearchPath = Path.GetFullPath(searchPathArg);
        if (!File.Exists(absoluteSearchPath) && !Directory.Exists(absoluteSearchPath))
        {
            Console.Error.WriteLine($"Search path not found: {absoluteSearchPath}");
            return 2;
        }

        if (!IsPathWithinRoot(absoluteRoot, absoluteSearchPath))
        {
            Console.Error.WriteLine($"Search path must be inside rootPath: {absoluteSearchPath}");
            return 2;
        }
    }

    var resolvedProgramName = string.IsNullOrWhiteSpace(programName)
        ? DefaultProgramName(absoluteRoot)
        : programName.Trim();

    var scanId = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
    using var connection = OpenConnection(dbPath);
    using var tx = connection.BeginTransaction();

    InsertScanJob(connection, tx, scanId, absoluteRoot, resolvedProgramName, programParameters, absoluteSearchPath);

    var includePython = HasFlag(args, "--include-python") || HasFlag(args, "--python");
    foreach (var file in EnumerateCandidateFiles(absoluteSearchPath, includePython))
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
    Console.WriteLine($"programName: {resolvedProgramName}");
    Console.WriteLine($"programParameters: {(string.IsNullOrWhiteSpace(programParameters) ? "-" : programParameters)}");
    Console.WriteLine($"searchPath: {absoluteSearchPath}");
    return 0;
}

static int RunInteractiveScan(string dbPath, bool polish)
{
    Console.WriteLine(polish ? "Kreator skanu" : "Scan wizard");

    var rootPrompt = polish ? "Podaj katalog do skanowania:" : "Enter the folder to scan:";
    var rootPath = ReadPrompt(rootPrompt, required: true);
    if (string.IsNullOrWhiteSpace(rootPath))
    {
        Console.Error.WriteLine(polish ? "Brak katalogu." : "No folder provided.");
        return 2;
    }

    var absoluteRoot = Path.GetFullPath(rootPath);
    if (!Directory.Exists(absoluteRoot))
    {
        Console.Error.WriteLine((polish ? "Nie znaleziono katalogu: " : "Directory not found: ") + absoluteRoot);
        return 2;
    }

    var appPrompt = polish ? "Nazwa programu (Enter = nazwa folderu):" : "Program name (Enter = folder name):";
    var programName = ReadPrompt(appPrompt, required: false);
    var resolvedProgramName = string.IsNullOrWhiteSpace(programName)
        ? DefaultProgramName(absoluteRoot)
        : programName.Trim();

    var searchPrompt = polish ? "Ścieżka do przeszukania (Enter = cały katalog):" : "Search path (Enter = whole folder):";
    var searchPathInput = ReadPrompt(searchPrompt, required: false);
    var absoluteSearchPath = absoluteRoot;
    if (!string.IsNullOrWhiteSpace(searchPathInput))
    {
        absoluteSearchPath = Path.GetFullPath(searchPathInput);
        if (!File.Exists(absoluteSearchPath) && !Directory.Exists(absoluteSearchPath))
        {
            Console.Error.WriteLine((polish ? "Nie znaleziono ścieżki: " : "Path not found: ") + absoluteSearchPath);
            return 2;
        }

        if (!IsPathWithinRoot(absoluteRoot, absoluteSearchPath))
        {
            Console.Error.WriteLine(polish ? "Ścieżka musi być wewnątrz katalogu." : "The path must be inside the folder.");
            return 2;
        }
    }

    var parametersPrompt = polish ? "Parametry programu (Enter = brak):" : "Program arguments (Enter = none):";
    var programParameters = ReadPrompt(parametersPrompt, required: false) ?? string.Empty;

    var includePythonPrompt = polish ? "Uwzględnić Python? [t/N]:" : "Include Python? [y/N]:";
    var includePython = ReadYesNo(includePythonPrompt);

    var scanArgs = new List<string>
    {
        "scan",
        "--root", absoluteRoot,
        "--app", resolvedProgramName,
        "--path", absoluteSearchPath
    };

    if (!string.IsNullOrWhiteSpace(programParameters))
    {
        scanArgs.Add("--args");
        scanArgs.Add(programParameters);
    }

    if (includePython)
    {
        scanArgs.Add("--python");
    }

    scanArgs.Add("--db");
    scanArgs.Add(dbPath);

    Console.WriteLine();
    Console.WriteLine(polish ? "Start skanu..." : "Starting scan...");
    return RunScan(scanArgs.ToArray(), dbPath);
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
        SELECT scan_id, root_path, program_name, program_parameters, search_path, status, started_at_utc, finished_at_utc
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
    Console.WriteLine($"programName: {ReadStringOrEmpty(reader, 2)}");
    Console.WriteLine($"programParameters: {DisplayValueOrDash(ReadStringOrEmpty(reader, 3))}");
    Console.WriteLine($"searchPath: {ReadStringOrEmpty(reader, 4)}");
    Console.WriteLine($"status: {reader.GetString(5)}");
    Console.WriteLine($"startedAtUtc: {reader.GetString(6)}");
    Console.WriteLine($"finishedAtUtc: {(reader.IsDBNull(7) ? "-" : reader.GetString(7))}");

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
    using var jobCmd = connection.CreateCommand();
    jobCmd.CommandText = """
        SELECT root_path, program_name, program_parameters, search_path
        FROM scan_jobs
        WHERE scan_id = $scanId;
        """;
    jobCmd.Parameters.AddWithValue("$scanId", scanId);

    string rootPath;
    string programName;
    string programParameters;
    string searchPath;
    using (var reader = jobCmd.ExecuteReader())
    {
        if (!reader.Read())
        {
            Console.Error.WriteLine($"scanId not found: {scanId}");
            return 3;
        }

        rootPath = reader.GetString(0);
        programName = ReadStringOrEmpty(reader, 1);
        programParameters = ReadStringOrEmpty(reader, 2);
        searchPath = ReadStringOrEmpty(reader, 3);
    }

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
            var payload = new
            {
                scanId,
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                rootPath,
                programName,
                programParameters,
                searchPath,
                items
            };
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

static int RunAnalyze(string[] args, string dbPath)
{
    var scanId = GetOption(args, "--id");
    if (string.IsNullOrWhiteSpace(scanId))
    {
        Console.Error.WriteLine("Missing required option: --id <scanId>");
        return 2;
    }

    var format = (GetOption(args, "--format") ?? "md").ToLowerInvariant();
    if (format is not ("md" or "html"))
    {
        Console.Error.WriteLine("Unsupported format. Use: md or html");
        return 2;
    }

    var outputArg = GetOption(args, "--output") ?? Path.Combine(Environment.CurrentDirectory, "reports");
    var outputPath = Path.GetFullPath(outputArg);

    try
    {
        using var connection = OpenConnection(dbPath);
        var report = AnalysisReportBuilder.BuildReport(connection, scanId);
        
        if (report == null)
        {
            Console.Error.WriteLine($"scanId not found or scan has no data: {scanId}");
            return 3;
        }

        Directory.CreateDirectory(outputPath);
        string outputFile;

        if (format == "md")
        {
            outputFile = Path.Combine(outputPath, $"{scanId}_report.md");
            var mdContent = MarkdownReportFormatter.Format(report);
            File.WriteAllText(outputFile, mdContent);
        }
        else
        {
            outputFile = Path.Combine(outputPath, $"{scanId}_report.html");
            var htmlContent = HtmlReportFormatter.Format(report);
            File.WriteAllText(outputFile, htmlContent);
        }

        using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = """
            INSERT INTO analyze_reports(scan_id, format, generated_at_utc, output_path, status)
            VALUES($scanId, $format, $generatedAt, $outputPath, 'success');
            """;
        insertCmd.Parameters.AddWithValue("$scanId", scanId);
        insertCmd.Parameters.AddWithValue("$format", format);
        insertCmd.Parameters.AddWithValue("$generatedAt", DateTime.UtcNow.ToString("O"));
        insertCmd.Parameters.AddWithValue("$outputPath", outputFile);
        insertCmd.ExecuteNonQuery();

        Console.WriteLine($"analyze{(format == "md" ? "Markdown" : "Html")}: {outputFile}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error generating analysis report: {ex.Message}");
        return 1;
    }
}

static void InsertScanJob(SqliteConnection connection, SqliteTransaction tx, string scanId, string rootPath, string programName, string programParameters, string searchPath)
{
    using var cmd = connection.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = """
        INSERT INTO scan_jobs(scan_id, root_path, program_name, program_parameters, search_path, status, started_at_utc, finished_at_utc)
        VALUES($scanId, $rootPath, $programName, $programParameters, $searchPath, 'running', $startedAt, NULL);
        """;
    cmd.Parameters.AddWithValue("$scanId", scanId);
    cmd.Parameters.AddWithValue("$rootPath", rootPath);
    cmd.Parameters.AddWithValue("$programName", programName);
    cmd.Parameters.AddWithValue("$programParameters", programParameters);
    cmd.Parameters.AddWithValue("$searchPath", searchPath);
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

static IEnumerable<string> EnumerateCandidateFiles(string searchPath, bool includePython)
{
    var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".dll", ".exe" };
    if (includePython)
    {
        allowedExtensions.Add(".py");
        allowedExtensions.Add(".whl");
    }

    if (File.Exists(searchPath))
    {
        if (allowedExtensions.Contains(Path.GetExtension(searchPath)))
        {
            yield return searchPath;
        }

        yield break;
    }

    foreach (var file in Directory.EnumerateFiles(searchPath, "*", SearchOption.AllDirectories))
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
            program_name TEXT NOT NULL DEFAULT '',
            program_parameters TEXT NOT NULL DEFAULT '',
            search_path TEXT NOT NULL DEFAULT '',
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

        CREATE TABLE IF NOT EXISTS analyze_reports(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            scan_id TEXT NOT NULL,
            format TEXT NOT NULL,
            generated_at_utc TEXT NOT NULL,
            output_path TEXT NOT NULL,
            status TEXT NOT NULL,
            FOREIGN KEY(scan_id) REFERENCES scan_jobs(scan_id)
        );

        CREATE TABLE IF NOT EXISTS assembly_references(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            scan_file_id INTEGER NOT NULL,
            referenced_assembly_name TEXT NOT NULL,
            referenced_version TEXT NOT NULL,
            FOREIGN KEY(scan_file_id) REFERENCES scan_files(id)
        );

        CREATE TABLE IF NOT EXISTS license_detections(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            scan_file_id INTEGER NOT NULL,
            license_type TEXT NOT NULL,
            confidence INTEGER NOT NULL,
            source_path TEXT NOT NULL,
            FOREIGN KEY(scan_file_id) REFERENCES scan_files(id)
        );
        """;
    cmd.ExecuteNonQuery();

    EnsureColumn(connection, "scan_jobs", "program_name", "TEXT NOT NULL DEFAULT ''");
    EnsureColumn(connection, "scan_jobs", "program_parameters", "TEXT NOT NULL DEFAULT ''");
    EnsureColumn(connection, "scan_jobs", "search_path", "TEXT NOT NULL DEFAULT ''");
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

static string? GetOptionWithAliases(string[] args, params string[] optionNames)
{
    string? resolved = null;
    string? resolvedName = null;
    var values = new List<(string Name, string Value)>();

    foreach (var optionName in optionNames)
    {
        var value = GetOption(args, optionName);
        if (value is null)
        {
            continue;
        }

        values.Add((optionName, value));
        if (resolved is null)
        {
            resolved = value;
            resolvedName = optionName;
        }
    }

    if (values.Count > 1 && values.Select(v => v.Value).Distinct(StringComparer.Ordinal).Count() > 1)
    {
        Console.Error.WriteLine($"Warning: multiple values were supplied for {string.Join(", ", optionNames)}; {resolvedName} takes precedence.");
    }

    return resolved;
}

static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
{
    using var infoCmd = connection.CreateCommand();
    infoCmd.CommandText = $"PRAGMA table_info({tableName});";
    using var reader = infoCmd.ExecuteReader();
    while (reader.Read())
    {
        if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
    }

    using var alterCmd = connection.CreateCommand();
    alterCmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
    alterCmd.ExecuteNonQuery();
}

static bool IsPathWithinRoot(string rootPath, string candidatePath)
{
    var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
    var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));

    if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
    return normalizedCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
}

static string DefaultProgramName(string rootPath)
{
    var trimmedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
    var name = Path.GetFileName(trimmedRoot);
    return string.IsNullOrWhiteSpace(name) ? trimmedRoot : name;
}

static string ReadStringOrEmpty(SqliteDataReader reader, int ordinal)
{
    return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
}

static string DisplayValueOrDash(string value)
{
    return string.IsNullOrWhiteSpace(value) ? "-" : value;
}

static string? ReadPrompt(string prompt, bool required)
{
    Console.Write(prompt + " ");
    var value = Console.ReadLine();
    if (required && string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    return value;
}

static bool ReadYesNo(string prompt)
{
    Console.Write(prompt + " ");
    var value = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
    return value is "y" or "yes" or "t" or "tak";
}

static bool IsPolishHelp()
{
    return string.Equals(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "pl", StringComparison.OrdinalIgnoreCase);
}

static bool HasFlag(string[] args, string flag)
{
    return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    PrintUsage(IsPolishHelp());
    return 2;
}

static void PrintUsage(bool polish)
{
    if (polish)
    {
        Console.WriteLine("InfoExe CLI");
        Console.WriteLine("Użycie:");
        Console.WriteLine("  uruchom bez argumentów, żeby przejść przez kreator");
        Console.WriteLine("  scan --root <path> [--app <name>] [--path <path>] [--args <args>] [--python] [--db <file>]");
        Console.WriteLine("  help");
        Console.WriteLine("  przykłady:");
        Console.WriteLine("    scan --root C:\\Apps\\MyApp --app MyApp --path C:\\Apps\\MyApp\\bin\\sample.dll");
        Console.WriteLine("    scan --root C:\\Apps\\Tools --app ToolA --args \"--mode quick\" --python");
        Console.WriteLine("  status --id <scanId> [--db <file>]");
        Console.WriteLine("  retry --id <scanId> [--db <file>]");
        Console.WriteLine("  export --id <scanId> [--format json|csv|json,csv] [--output <path>]");
        Console.WriteLine("  analyze --id <scanId> [--format md|html] [--output <path>]");
        return;
    }

    Console.WriteLine("InfoExe CLI");
    Console.WriteLine("Usage:");
    Console.WriteLine("  run without arguments to use the wizard");
    Console.WriteLine("  scan --root <path> [--app <name>] [--path <path>] [--args <args>] [--python] [--db <file>]");
    Console.WriteLine("  help");
    Console.WriteLine("  examples:");
    Console.WriteLine("    scan --root C:\\Apps\\MyApp --app MyApp --path C:\\Apps\\MyApp\\bin\\sample.dll");
    Console.WriteLine("    scan --root C:\\Apps\\Tools --app ToolA --args \"--mode quick\" --python");
    Console.WriteLine("  status --id <scanId> [--db <file>]");
    Console.WriteLine("  retry --id <scanId> [--db <file>]");
    Console.WriteLine("  export --id <scanId> [--format json|csv|json,csv] [--output <path>]");
    Console.WriteLine("  analyze --id <scanId> [--format md|html] [--output <path>]");
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

internal sealed class AnalysisReport
{
    public string ScanId { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public List<AssemblyInfo> Assemblies { get; set; } = [];
    public TechnologyStack TechnologyStack { get; set; } = new();
    public List<string> Licenses { get; set; } = [];
    public List<string> BuildFiles { get; set; } = [];
    public CodeMetrics Metrics { get; set; } = new();
}

internal sealed class TechnologyStack
{
    public List<string> Frameworks { get; set; } = [];
    public List<string> CompilationModes { get; set; } = [];
    public List<string> Languages { get; set; } = [];
    public List<string> NuGetPackages { get; set; } = [];
}

internal sealed class CodeMetrics
{
    public int TotalAssemblies { get; set; }
    public int TotalDecompiledFiles { get; set; }
    public long TotalLinesOfCode { get; set; }
    public int ProjectsFound { get; set; }
}

internal sealed class AssemblyInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string AssemblyName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public string PublicKeyToken { get; set; } = string.Empty;
    public List<string> References { get; set; } = [];
    public string VendorName { get; set; } = string.Empty;
    public int VendorConfidence { get; set; }
    public string EncodingType { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public bool IsILOnly { get; set; }
    public bool IsReadyToRun { get; set; }
    public bool IsNativeAot { get; set; }
    public long FileSize { get; set; }
}

internal static class AnalysisReportBuilder
{
    public static AnalysisReport? BuildReport(SqliteConnection connection, string scanId)
    {
        using var jobCmd = connection.CreateCommand();
        jobCmd.CommandText = "SELECT root_path, program_name, search_path FROM scan_jobs WHERE scan_id = $scanId;";
        jobCmd.Parameters.AddWithValue("$scanId", scanId);

        string rootPath, programName, searchPath;
        using (var reader = jobCmd.ExecuteReader())
        {
            if (!reader.Read())
                return null;
            rootPath = reader.GetString(0);
            programName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            searchPath = reader.IsDBNull(2) ? rootPath : reader.GetString(2);
        }

        var report = new AnalysisReport
        {
            ScanId = scanId,
            ProgramName = programName,
            RootPath = rootPath,
            GeneratedAt = DateTime.UtcNow
        };

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT f.id, f.file_path, am.assembly_name, am.assembly_version, am.target_framework, 
                   am.public_key_token, am.is_single_file, am.is_ready_to_run, am.is_native_aot_limited,
                   vr.vendor_name, vr.confidence, d.artifact_path
            FROM scan_files f
            LEFT JOIN assembly_metadata am ON am.scan_file_id = f.id
            LEFT JOIN vendor_results vr ON vr.scan_file_id = f.id
            LEFT JOIN decompilation_results d ON d.scan_file_id = f.id
            WHERE f.scan_id = $scanId AND f.file_type = 'dotnet-managed'
            ORDER BY f.file_path;
        ";
        cmd.Parameters.AddWithValue("$scanId", scanId);

        var frameworks = new HashSet<string>();
        var compilationModes = new HashSet<string>();
        var languages = new HashSet<string> { "C#" };
        var nugetPackages = new HashSet<string>();
        var licenses = new HashSet<string>();
        var buildFiles = new HashSet<string>();
        int totalDecompiledFiles = 0;
        long totalLines = 0;
        int projectsFound = 0;

        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var fileId = reader.GetInt64(0);
                var filePath = reader.GetString(1);
                var assemblyName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var version = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                var tfm = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
                var token = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
                var isSingleFile = !reader.IsDBNull(6) && reader.GetInt64(6) == 1;
                var isR2R = !reader.IsDBNull(7) && reader.GetInt64(7) == 1;
                var isNativeAot = !reader.IsDBNull(8) && reader.GetInt64(8) == 1;
                var vendorName = reader.IsDBNull(9) ? string.Empty : reader.GetString(9);
                var vendorConfidence = reader.IsDBNull(10) ? 0 : Convert.ToInt32(reader.GetInt64(10));
                var artifactPath = reader.IsDBNull(11) ? string.Empty : reader.GetString(11);

                long fileSize = 0;
                try { fileSize = new FileInfo(filePath).Length; } catch { }

                var info = new AssemblyInfo
                {
                    FilePath = filePath,
                    AssemblyName = assemblyName,
                    Version = version,
                    TargetFramework = tfm,
                    PublicKeyToken = token,
                    VendorName = vendorName,
                    VendorConfidence = vendorConfidence,
                    IsILOnly = !isSingleFile && !isR2R && !isNativeAot,
                    IsReadyToRun = isR2R,
                    IsNativeAot = isNativeAot,
                    FileSize = fileSize,
                    EncodingType = DetectEncoding(filePath)
                };

                // Classify framework
                if (!string.IsNullOrWhiteSpace(tfm))
                {
                    var fw = ClassifyFramework(tfm);
                    if (!string.IsNullOrWhiteSpace(fw))
                        frameworks.Add(fw);
                }

                if (isR2R) compilationModes.Add("ReadyToRun");
                if (isNativeAot) compilationModes.Add("NativeAOT");
                if (isSingleFile) compilationModes.Add("SingleFile");

                // Extract assembly references
                try
                {
                    var asmRefs = System.Reflection.Assembly.LoadFrom(filePath).GetReferencedAssemblies();
                    foreach (var r in asmRefs)
                    {
                        var refName = r.Name ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(refName) && !refName.StartsWith("System.") && !refName.StartsWith("Microsoft.") && refName != "mscorlib" && refName != "netstandard")
                        {
                            info.References.Add($"{refName}@{r.Version}");
                        }
                        DetectNuGetPackage(refName, nugetPackages);

                        try
                        {
                            using var refCmd = connection.CreateCommand();
                            refCmd.CommandText = "INSERT OR IGNORE INTO assembly_references(scan_file_id, referenced_assembly_name, referenced_version) VALUES($fid, $name, $ver);";
                            refCmd.Parameters.AddWithValue("$fid", fileId);
                            refCmd.Parameters.AddWithValue("$name", refName);
                            refCmd.Parameters.AddWithValue("$ver", r.Version?.ToString() ?? "");
                            refCmd.ExecuteNonQuery();
                        }
                        catch { }
                    }
                }
                catch { }

                // Analyze decompiled artifacts
                if (!string.IsNullOrWhiteSpace(artifactPath) && Directory.Exists(artifactPath))
                {
                    try
                    {
                        var csFiles = Directory.GetFiles(artifactPath, "*.cs", SearchOption.AllDirectories);
                        totalDecompiledFiles += csFiles.Length;
                        foreach (var cs in csFiles)
                        {
                            try { totalLines += File.ReadLines(cs).Count(); } catch { }
                        }

                        var csprojFiles = Directory.GetFiles(artifactPath, "*.csproj", SearchOption.AllDirectories);
                        projectsFound += csprojFiles.Length;
                        foreach (var cp in csprojFiles)
                        {
                            try
                            {
                                var projContent = File.ReadAllText(cp);
                                DetectNuGetPackagesFromCsproj(projContent, nugetPackages);
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                // License detection per assembly
                try
                {
                    var asmDir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrWhiteSpace(asmDir))
                    {
                        var license = DetectLicenseInDirectory(asmDir);
                        if (!string.IsNullOrWhiteSpace(license))
                        {
                            info.License = license;
                            licenses.Add(license);
                        }
                    }
                }
                catch { }

                report.Assemblies.Add(info);
            }
        }

        // Build process analysis
        DetectBuildFiles(rootPath, buildFiles);

        report.TechnologyStack.Frameworks.AddRange(frameworks);
        report.TechnologyStack.CompilationModes.AddRange(compilationModes);
        report.TechnologyStack.Languages.AddRange(languages);
        report.TechnologyStack.NuGetPackages.AddRange(nugetPackages);
        report.Licenses.AddRange(licenses);
        report.BuildFiles.AddRange(buildFiles);
        report.Metrics = new CodeMetrics
        {
            TotalAssemblies = report.Assemblies.Count,
            TotalDecompiledFiles = totalDecompiledFiles,
            TotalLinesOfCode = totalLines,
            ProjectsFound = projectsFound
        };

        return report;
    }

    private static string ClassifyFramework(string tfm)
    {
        if (tfm.StartsWith("net10")) return ".NET 10";
        if (tfm.StartsWith("net9")) return ".NET 9";
        if (tfm.StartsWith("net8")) return ".NET 8";
        if (tfm.StartsWith("net7")) return ".NET 7";
        if (tfm.StartsWith("net6")) return ".NET 6";
        if (tfm.StartsWith("net5")) return ".NET 5";
        if (tfm.StartsWith("netcoreapp3")) return ".NET Core 3.1";
        if (tfm.StartsWith("netcoreapp")) return ".NET Core 2.x";
        if (tfm.StartsWith("netstandard")) return ".NET Standard";
        if (tfm.StartsWith("net4")) return ".NET Framework 4.x";
        if (tfm.StartsWith("v4")) return ".NET Framework 4.x";
        return tfm;
    }

    private static void DetectNuGetPackage(string assemblyName, HashSet<string> packages)
    {
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Newtonsoft.Json"] = "Newtonsoft.Json",
            ["Serilog"] = "Serilog",
            ["NLog"] = "NLog",
            ["AutoMapper"] = "AutoMapper",
            ["Dapper"] = "Dapper",
            ["Microsoft.EntityFrameworkCore"] = "Entity Framework Core",
            ["Npgsql"] = "Npgsql",
            ["StackExchange.Redis"] = "StackExchange.Redis",
            ["MassTransit"] = "MassTransit",
            ["MediatR"] = "MediatR",
            ["FluentValidation"] = "FluentValidation",
            ["Polly"] = "Polly",
            ["RestSharp"] = "RestSharp",
            ["Swashbuckle"] = "Swashbuckle (Swagger)",
            ["Xunit"] = "xUnit",
            ["NUnit"] = "NUnit",
            ["Moq"] = "Moq",
            ["FluentAssertions"] = "FluentAssertions",
            ["Bogus"] = "Bogus",
            ["BenchmarkDotNet"] = "BenchmarkDotNet",
            ["OpenTelemetry"] = "OpenTelemetry",
            ["YamlDotNet"] = "YamlDotNet",
            ["CsvHelper"] = "CsvHelper",
            ["Avalonia"] = "Avalonia UI",
            ["ReactiveUI"] = "ReactiveUI",
        };
        if (known.TryGetValue(assemblyName, out var pkg))
            packages.Add(pkg);
    }

    private static void DetectNuGetPackagesFromCsproj(string content, HashSet<string> packages)
    {
        var matches = Regex.Matches(content, @"<PackageReference\s+Include=""([^""]+)""[^>]*>");
        foreach (Match match in matches)
        {
            if (match.Groups.Count > 1)
                packages.Add(match.Groups[1].Value);
        }
    }

    private static string DetectLicenseInDirectory(string directory)
    {
        var licenseFiles = new[] { "LICENSE", "LICENSE.txt", "LICENSE.md", "license.txt", "license.md" };
        foreach (var lf in licenseFiles)
        {
            var path = Path.Combine(directory, lf);
            if (!File.Exists(path)) continue;
            try
            {
                var text = File.ReadAllText(path);
                if (text.Contains("MIT")) return "MIT";
                if (text.Contains("Apache License") && text.Contains("2.0")) return "Apache-2.0";
                if (text.Contains("GNU GENERAL PUBLIC LICENSE") && text.Contains("Version 3")) return "GPL-3.0";
                if (text.Contains("GNU GENERAL PUBLIC LICENSE")) return "GPL";
                if (text.Contains("BSD")) return "BSD";
                if (text.Contains("Mozilla Public License")) return "MPL";
                return "Other";
            }
            catch { }
        }
        return string.Empty;
    }

    private static void DetectBuildFiles(string rootPath, HashSet<string> buildFiles)
    {
        var patterns = new[] { "*.sln", "*.csproj", "*.vbproj", "*.fsproj", "*.props", "*.targets",
            "Dockerfile", "docker-compose.yml", "docker-compose.yaml",
            "Jenkinsfile", ".travis.yml", "azure-pipelines.yml",
            "Makefile", "CMakeLists.txt", "package.json", "*.nuspec",
            "NuGet.config", "Directory.Build.props", "Directory.Packages.props",
            "global.json", ".editorconfig" };

        foreach (var pattern in patterns)
        {
            try
            {
                var files = Directory.GetFiles(rootPath, pattern, SearchOption.AllDirectories);
                foreach (var f in files)
                    buildFiles.Add(Path.GetRelativePath(rootPath, f));
            }
            catch { }
        }

        // CI/CD directories
        var ciDirs = new[] { ".github/workflows", ".gitlab-ci.yml", ".circleci", ".azuredevops" };
        foreach (var dir in ciDirs)
        {
            var fullPath = Path.Combine(rootPath, dir);
            if (Directory.Exists(fullPath))
            {
                try
                {
                    var ymlFiles = Directory.GetFiles(fullPath, "*.yml", SearchOption.AllDirectories);
                    foreach (var yf in ymlFiles)
                        buildFiles.Add(Path.GetRelativePath(rootPath, yf));
                }
                catch { }
            }
        }
    }

    private static string DetectEncoding(string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            if (bytes.Length < 4) return "unknown";
            if (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return "UTF-8 BOM";
            if (ContainsUtf8(bytes)) return "UTF-8";
            return "ASCII/Binary";
        }
        catch { return "unknown"; }
    }

    private static bool ContainsUtf8(byte[] bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
            if ((bytes[i] & 0x80) != 0) return true;
        return false;
    }
}

internal static class MarkdownReportFormatter
{
    public static string Format(AnalysisReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Analysis Report: {report.ProgramName}");
        sb.AppendLine();
        sb.AppendLine($"**Scan ID:** `{report.ScanId}`");
        sb.AppendLine($"**Generated:** {report.GeneratedAt:O}");
        sb.AppendLine($"**Root Path:** `{report.RootPath}`");
        sb.AppendLine();

        sb.AppendLine("## Technology Stack");
        sb.AppendLine();
        if (report.TechnologyStack.Frameworks.Count > 0)
            sb.AppendLine("- **Frameworks:** " + string.Join(", ", report.TechnologyStack.Frameworks));
        if (report.TechnologyStack.CompilationModes.Count > 0)
            sb.AppendLine("- **Compilation:** " + string.Join(", ", report.TechnologyStack.CompilationModes));
        if (report.TechnologyStack.Languages.Count > 0)
            sb.AppendLine("- **Languages:** " + string.Join(", ", report.TechnologyStack.Languages));
        if (report.TechnologyStack.NuGetPackages.Count > 0)
            sb.AppendLine("- **NuGet Packages:** " + string.Join(", ", report.TechnologyStack.NuGetPackages.Take(30)));
        sb.AppendLine();

        sb.AppendLine("## Code Metrics");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| Total Assemblies | {report.Metrics.TotalAssemblies} |");
        sb.AppendLine($"| Decompiled Files | {report.Metrics.TotalDecompiledFiles} |");
        sb.AppendLine($"| Lines of Code | {report.Metrics.TotalLinesOfCode:N0} |");
        sb.AppendLine($"| Projects Found | {report.Metrics.ProjectsFound} |");
        sb.AppendLine();

        sb.AppendLine("## Assemblies");
        sb.AppendLine();
        sb.AppendLine("| Assembly | Version | Framework | Type | Vendor | License | Size |");
        sb.AppendLine("|----------|---------|-----------|------|--------|---------|------|");

        foreach (var asm in report.Assemblies)
        {
            var type = asm.IsReadyToRun ? "R2R" : asm.IsNativeAot ? "AOT" : "IL";
            var vendor = string.IsNullOrEmpty(asm.VendorName) ? "-" : $"{asm.VendorName} ({asm.VendorConfidence}%)";
            var license = string.IsNullOrEmpty(asm.License) ? "-" : asm.License;
            var size = asm.FileSize > 0 ? $"{asm.FileSize / 1024.0:F1} KB" : "-";
            sb.AppendLine($"| {asm.AssemblyName} | {asm.Version} | {asm.TargetFramework} | {type} | {vendor} | {license} | {size} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Dependencies");
        sb.AppendLine();
        foreach (var asm in report.Assemblies.Where(a => a.References.Count > 0))
        {
            sb.AppendLine($"### {asm.AssemblyName}");
            foreach (var dep in asm.References.Take(10))
                sb.AppendLine($"- `{dep}`");
            if (asm.References.Count > 10)
                sb.AppendLine($"- ... and {asm.References.Count - 10} more");
            sb.AppendLine();
        }

        if (report.Licenses.Count > 0)
        {
            sb.AppendLine("## Licenses Detected");
            sb.AppendLine();
            foreach (var lic in report.Licenses)
                sb.AppendLine($"- {lic}");
            sb.AppendLine();
        }

        if (report.BuildFiles.Count > 0)
        {
            sb.AppendLine("## Build Files");
            sb.AppendLine();
            foreach (var bf in report.BuildFiles.OrderBy(x => x))
                sb.AppendLine($"- `{bf}`");
            sb.AppendLine();
        }

        sb.AppendLine("## File Paths");
        sb.AppendLine();
        foreach (var asm in report.Assemblies)
            sb.AppendLine($"- `{asm.FilePath}`");

        return sb.ToString();
    }
}

internal static class HtmlReportFormatter
{
    public static string Format(AnalysisReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"<title>Analysis Report: {HtmlEscape(report.ProgramName)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("*,*::before,*::after{box-sizing:border-box;margin:0;padding:0}");
        sb.AppendLine("body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Oxygen,Ubuntu,sans-serif;background:#0d1117;color:#c9d1d9;line-height:1.6;min-height:100vh}");
        sb.AppendLine(".container{max-width:1100px;margin:0 auto;padding:2rem}");
        sb.AppendLine("h1{font-size:2rem;font-weight:700;color:#58a6ff;margin-bottom:.5rem}");
        sb.AppendLine("h2{font-size:1.35rem;font-weight:600;color:#f0f6fc;margin:2rem 0 1rem;padding-bottom:.5rem;border-bottom:1px solid #30363d}");
        sb.AppendLine("h3{font-size:1.1rem;font-weight:600;color:#e6edf3;margin:1rem 0 .5rem}");
        sb.AppendLine(".meta{background:#161b22;border:1px solid #30363d;border-radius:8px;padding:1.25rem;margin:1.5rem 0}");
        sb.AppendLine(".meta p{margin:.35rem 0}");
        sb.AppendLine(".badge{display:inline-block;padding:.15em .6em;font-size:.8rem;font-weight:600;border-radius:12px;margin:.15em}");
        sb.AppendLine(".badge-net{background:#178600;color:#fff}");
        sb.AppendLine(".badge-pkg{background:#1f6feb;color:#fff}");
        sb.AppendLine(".badge-lic{background:#6e40c9;color:#fff}");
        sb.AppendLine(".badge-build{background:#30363d;color:#8b949e}");
        sb.AppendLine("table{width:100%;border-collapse:collapse;margin:1rem 0;font-size:.9rem}");
        sb.AppendLine("th,td{border:1px solid #30363d;padding:.65rem .85rem;text-align:left}");
        sb.AppendLine("th{background:#161b22;font-weight:600;color:#f0f6fc;position:sticky;top:0}");
        sb.AppendLine("tr:nth-child(even){background:#161b22}");
        sb.AppendLine("tr:hover{background:#1c2129}");
        sb.AppendLine("code{background:#161b22;padding:.15em .4em;border-radius:4px;font-family:'JetBrains Mono',Cascadia Code,Consolas,monospace;font-size:.88em;color:#d2a8ff}");
        sb.AppendLine("ul,ol{margin:.5rem 0 .5rem 1.5rem}");
        sb.AppendLine("li{margin:.2rem 0}");
        sb.AppendLine(".grid-2{display:grid;grid-template-columns:repeat(auto-fit,minmax(250px,1fr));gap:1rem;margin:1rem 0}");
        sb.AppendLine(".card{background:#161b22;border:1px solid #30363d;border-radius:8px;padding:1rem}");
        sb.AppendLine(".card h4{font-size:.95rem;color:#8b949e;margin-bottom:.5rem;text-transform:uppercase;letter-spacing:.05em}");
        sb.AppendLine(".card .value{font-size:1.5rem;font-weight:700;color:#f0f6fc}");
        sb.AppendLine(".section-deps{max-height:400px;overflow-y:auto}");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"container\">");

        sb.AppendLine($"<h1>Analysis Report: {HtmlEscape(report.ProgramName)}</h1>");
        sb.AppendLine("<div class=\"meta\">");
        sb.AppendLine($"<p><strong>Scan ID:</strong> <code>{HtmlEscape(report.ScanId)}</code></p>");
        sb.AppendLine($"<p><strong>Generated:</strong> {report.GeneratedAt:O}</p>");
        sb.AppendLine($"<p><strong>Root Path:</strong> <code>{HtmlEscape(report.RootPath)}</code></p>");
        sb.AppendLine("</div>");

        sb.AppendLine("<h2>Code Metrics</h2>");
        sb.AppendLine("<div class=\"grid-2\">");
        sb.AppendLine($"<div class=\"card\"><h4>Assemblies</h4><div class=\"value\">{report.Metrics.TotalAssemblies}</div></div>");
        sb.AppendLine($"<div class=\"card\"><h4>Decompiled Files</h4><div class=\"value\">{report.Metrics.TotalDecompiledFiles}</div></div>");
        sb.AppendLine($"<div class=\"card\"><h4>Lines of Code</h4><div class=\"value\">{report.Metrics.TotalLinesOfCode:N0}</div></div>");
        sb.AppendLine($"<div class=\"card\"><h4>Projects</h4><div class=\"value\">{report.Metrics.ProjectsFound}</div></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<h2>Technology Stack</h2>");
        if (report.TechnologyStack.Frameworks.Count > 0)
        {
            sb.AppendLine("<p>");
            foreach (var fw in report.TechnologyStack.Frameworks)
                sb.AppendLine($"<span class=\"badge badge-net\">{HtmlEscape(fw)}</span> ");
            sb.AppendLine("</p>");
        }
        if (report.TechnologyStack.CompilationModes.Count > 0)
            sb.AppendLine("<p><strong>Compilation:</strong> " + string.Join(", ", report.TechnologyStack.CompilationModes.Select(HtmlEscape)) + "</p>");
        if (report.TechnologyStack.NuGetPackages.Count > 0)
        {
            sb.AppendLine("<p>");
            foreach (var pkg in report.TechnologyStack.NuGetPackages.Take(40))
                sb.AppendLine($"<span class=\"badge badge-pkg\">{HtmlEscape(pkg)}</span> ");
            if (report.TechnologyStack.NuGetPackages.Count > 40)
                sb.AppendLine($"<span class=\"badge\">+{report.TechnologyStack.NuGetPackages.Count - 40} more</span>");
            sb.AppendLine("</p>");
        }

        sb.AppendLine("<h2>Assemblies</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine("<thead><tr><th>Assembly</th><th>Version</th><th>Framework</th><th>Type</th><th>Vendor</th><th>License</th><th>Size</th></tr></thead>");
        sb.AppendLine("<tbody>");
        foreach (var asm in report.Assemblies)
        {
            var type = asm.IsReadyToRun ? "R2R" : asm.IsNativeAot ? "AOT" : "IL";
            var vendor = string.IsNullOrEmpty(asm.VendorName) ? "-" : $"{HtmlEscape(asm.VendorName)} ({asm.VendorConfidence}%)";
            var license = string.IsNullOrEmpty(asm.License) ? "-" : $"<span class=\"badge badge-lic\">{HtmlEscape(asm.License)}</span>";
            var size = asm.FileSize > 0 ? $"{asm.FileSize / 1024.0:F1} KB" : "-";
            sb.AppendLine($"<tr><td>{HtmlEscape(asm.AssemblyName)}</td><td>{HtmlEscape(asm.Version)}</td><td>{HtmlEscape(asm.TargetFramework)}</td><td>{type}</td><td>{vendor}</td><td>{license}</td><td>{size}</td></tr>");
        }
        sb.AppendLine("</tbody>");
        sb.AppendLine("</table>");

        sb.AppendLine("<h2>Dependencies</h2>");
        sb.AppendLine("<div class=\"section-deps\">");
        foreach (var asm in report.Assemblies.Where(a => a.References.Count > 0))
        {
            sb.AppendLine($"<h3>{HtmlEscape(asm.AssemblyName)}</h3><ul>");
            foreach (var dep in asm.References.Take(10))
                sb.AppendLine($"<li><code>{HtmlEscape(dep)}</code></li>");
            if (asm.References.Count > 10)
                sb.AppendLine($"<li>... and {asm.References.Count - 10} more</li>");
            sb.AppendLine("</ul>");
        }
        sb.AppendLine("</div>");

        if (report.Licenses.Count > 0)
        {
            sb.AppendLine("<h2>Licenses Detected</h2>");
            sb.AppendLine("<ul>");
            foreach (var lic in report.Licenses)
                sb.AppendLine($"<li>{HtmlEscape(lic)}</li>");
            sb.AppendLine("</ul>");
        }

        if (report.BuildFiles.Count > 0)
        {
            sb.AppendLine("<h2>Build Files</h2>");
            sb.AppendLine("<ul>");
            foreach (var bf in report.BuildFiles.OrderBy(x => x))
                sb.AppendLine($"<li><code>{HtmlEscape(bf)}</code></li>");
            sb.AppendLine("</ul>");
        }

        sb.AppendLine("<h2>File Paths</h2>");
        sb.AppendLine("<ul>");
        foreach (var asm in report.Assemblies)
            sb.AppendLine($"<li><code>{HtmlEscape(asm.FilePath)}</code></li>");
        sb.AppendLine("</ul>");

        sb.AppendLine("</div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static string HtmlEscape(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }
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
