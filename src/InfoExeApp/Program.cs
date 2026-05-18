using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;
using InfoExeCore;
using InfoExeCore.Models;

var dbPath = args.Length > 0 && args[0] != "help"
    ? PathValidator.ValidateDbPath(GetOption(args, "--db") ?? Path.Combine(Environment.CurrentDirectory, Constants.DefaultDbFileName))
    : (GetOption(args, "--db") ?? Path.Combine(Environment.CurrentDirectory, Constants.DefaultDbFileName));

using var connection = Database.OpenConnection(dbPath);
Database.InitializeSchema(connection);
connection.Close();

if (args.Length == 0)
    return RunInteractiveScan(dbPath, IsPolishHelp());

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

    // SEC-03: Validate paths
    string absoluteRoot;
    try { absoluteRoot = PathValidator.ValidateScanRoot(rootPath); }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }

    string absoluteSearchPath;
    try { absoluteSearchPath = PathValidator.ValidateSearchPath(absoluteRoot, searchPathArg); }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }

    var resolvedProgramName = string.IsNullOrWhiteSpace(programName)
        ? Database.DefaultProgramName(absoluteRoot)
        : programName.Trim();

    var scanId = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
    using var connection = Database.OpenConnection(dbPath);
    using var tx = connection.BeginTransaction();

    Database.InsertScanJob(connection, tx, scanId, absoluteRoot, resolvedProgramName, programParameters, absoluteSearchPath);

    var includePython = HasFlag(args, "--include-python") || HasFlag(args, "--python");
    foreach (var file in Classifier.EnumerateCandidateFiles(absoluteSearchPath, includePython))
    {
        var classification = Classifier.ClassifyFile(file);
        var scanFileId = Database.InsertScanFile(connection, tx, scanId, file, classification);

        if (classification.FileType != Constants.FileTypeDotNetManaged)
            continue;

        var metadata = MetadataExtractor.ExtractManagedMetadata(file, classification);
        Database.InsertAssemblyMetadata(connection, tx, scanFileId, metadata);

        var evidence = VendorAnalyzer.BuildVendorEvidence(file, metadata);
        Database.InsertVendorEvidence(connection, tx, scanFileId, evidence);
        Database.InsertVendorResult(connection, tx, scanFileId, evidence);

        var decompileResult = Decompiler.AttemptDecompile(file, scanId, scanFileId);
        Database.InsertDecompileResult(connection, tx, scanFileId, decompileResult);

        if (decompileResult.Status != Constants.DecompileStatusDecompiled)
            Database.MarkPartialWithReason(connection, tx, scanFileId, decompileResult.ReasonCode);
    }

    Database.CompleteScanJob(connection, tx, scanId);
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
    if (string.IsNullOrWhiteSpace(rootPath)) { Console.Error.WriteLine(polish ? "Brak katalogu." : "No folder provided."); return 2; }

    string absoluteRoot;
    try { absoluteRoot = PathValidator.ValidateScanRoot(rootPath); }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }

    var appPrompt = polish ? "Nazwa programu (Enter = nazwa folderu):" : "Program name (Enter = folder name):";
    var programName = ReadPrompt(appPrompt, required: false);
    var resolvedProgramName = string.IsNullOrWhiteSpace(programName) ? Database.DefaultProgramName(absoluteRoot) : programName.Trim();

    var searchPrompt = polish ? "Ścieżka do przeszukania (Enter = cały katalog):" : "Search path (Enter = whole folder):";
    var searchPathInput = ReadPrompt(searchPrompt, required: false);
    string absoluteSearchPath;
    try { absoluteSearchPath = PathValidator.ValidateSearchPath(absoluteRoot, searchPathInput); }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }

    var parametersPrompt = polish ? "Parametry programu (Enter = brak):" : "Program arguments (Enter = none):";
    var programParameters = ReadPrompt(parametersPrompt, required: false) ?? string.Empty;

    var includePythonPrompt = polish ? "Uwzględnić Python? [t/N]:" : "Include Python? [y/N]:";
    var includePython = ReadYesNo(includePythonPrompt);

    var scanArgs = new List<string> { "scan", "--root", absoluteRoot, "--app", resolvedProgramName, "--path", absoluteSearchPath };
    if (!string.IsNullOrWhiteSpace(programParameters)) { scanArgs.Add("--args"); scanArgs.Add(programParameters); }
    if (includePython) scanArgs.Add("--python");
    scanArgs.Add("--db"); scanArgs.Add(dbPath);

    Console.WriteLine();
    Console.WriteLine(polish ? "Start skanu..." : "Starting scan...");
    return RunScan(scanArgs.ToArray(), dbPath);
}

static int RunStatus(string[] args, string dbPath)
{
    var scanId = GetOption(args, "--id");
    if (string.IsNullOrWhiteSpace(scanId)) { Console.Error.WriteLine("Missing required option: --id <scanId>"); return 2; }

    using var connection = Database.OpenConnection(dbPath);
    using var jobCmd = connection.CreateCommand();
    jobCmd.CommandText = "SELECT scan_id, root_path, program_name, program_parameters, search_path, status, started_at_utc, finished_at_utc FROM scan_jobs WHERE scan_id = $scanId;";
    jobCmd.Parameters.AddWithValue("$scanId", scanId);

    using var reader = jobCmd.ExecuteReader();
    if (!reader.Read()) { Console.Error.WriteLine($"scanId not found: {scanId}"); return 3; }

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
        SELECT COUNT(*) AS discovered,
            SUM(CASE WHEN status = 'processed' THEN 1 ELSE 0 END) AS processed,
            SUM(CASE WHEN status = 'partial' THEN 1 ELSE 0 END) AS partial,
            SUM(CASE WHEN status = 'failed' THEN 1 ELSE 0 END) AS failed
        FROM scan_files WHERE scan_id = $scanId;
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

    Console.WriteLine($"managedMetadata: {ScalarLong(connection, "SELECT COUNT(*) FROM assembly_metadata m JOIN scan_files f ON f.id = m.scan_file_id WHERE f.scan_id = $scanId;", scanId)}");
    Console.WriteLine($"vendorAttributed: {ScalarLong(connection, "SELECT SUM(CASE WHEN vr.status = 'attributed' THEN 1 ELSE 0 END) FROM vendor_results vr JOIN scan_files f ON f.id = vr.scan_file_id WHERE f.scan_id = $scanId;", scanId)}");
    Console.WriteLine($"vendorInconclusive: {ScalarLong(connection, "SELECT SUM(CASE WHEN vr.status = 'inconclusive' THEN 1 ELSE 0 END) FROM vendor_results vr JOIN scan_files f ON f.id = vr.scan_file_id WHERE f.scan_id = $scanId;", scanId)}");
    Console.WriteLine($"decompiled: {ScalarLong(connection, "SELECT SUM(CASE WHEN d.status = 'decompiled' THEN 1 ELSE 0 END) FROM decompilation_results d JOIN scan_files f ON f.id = d.scan_file_id WHERE f.scan_id = $scanId;", scanId)}");

    return 0;
}

static int RunRetry(string[] args, string dbPath)
{
    var scanId = GetOption(args, "--id");
    if (string.IsNullOrWhiteSpace(scanId)) { Console.Error.WriteLine("Missing required option: --id <scanId>"); return 2; }

    using var connection = Database.OpenConnection(dbPath);
    using var query = connection.CreateCommand();
    query.CommandText = """
        SELECT id, file_path, retry_count FROM scan_files
        WHERE scan_id = $scanId AND file_type = 'dotnet-managed'
          AND status IN ('partial','failed') AND retry_count < $maxRetry;
        """;
    query.Parameters.AddWithValue("$scanId", scanId);
    query.Parameters.AddWithValue("$maxRetry", Constants.MaxRetryCount);

    var candidates = new List<(long Id, string Path, long RetryCount)>();
    using (var reader = query.ExecuteReader())
        while (reader.Read())
            candidates.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2)));

    if (candidates.Count == 0) { Console.WriteLine("No retry candidates."); return 0; }

    using var tx = connection.BeginTransaction();
    var retried = 0;
    foreach (var candidate in candidates)
    {
        var result = Decompiler.AttemptDecompile(candidate.Path, scanId, candidate.Id);
        Database.UpsertDecompileResult(connection, tx, candidate.Id, result);

        using var update = connection.CreateCommand();
        update.Transaction = tx;
        update.CommandText = "UPDATE scan_files SET status = $status, reason_code = $reason, retry_count = retry_count + 1 WHERE id = $id;";
        update.Parameters.AddWithValue("$status", result.Status == Constants.DecompileStatusDecompiled ? Constants.StatusProcessed : Constants.StatusPartial);
        update.Parameters.AddWithValue("$reason", result.ReasonCode ?? string.Empty);
        update.Parameters.AddWithValue("$id", candidate.Id);
        update.ExecuteNonQuery();

        Console.WriteLine($"{(result.Status == Constants.DecompileStatusDecompiled ? "[OK]" : "[FAIL]")} {Path.GetFileName(candidate.Path)} — {(result.ReasonCode ?? "success")}");
        retried++;
    }

    tx.Commit();
    Console.WriteLine($"retried: {retried}");
    return 0;
}

static int RunExport(string[] args, string dbPath)
{
    var scanId = GetOption(args, "--id");
    if (string.IsNullOrWhiteSpace(scanId)) { Console.Error.WriteLine("Missing required option: --id <scanId>"); return 2; }

    var formatArg = (GetOption(args, "--format") ?? "json").ToLowerInvariant();
    var formats = formatArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var outputArg = GetOption(args, "--output") ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "reports");
    var outputPath = Path.GetFullPath(outputArg);

    using var connection = Database.OpenConnection(dbPath);
    using var jobCmd = connection.CreateCommand();
    jobCmd.CommandText = "SELECT root_path, program_name, program_parameters, search_path FROM scan_jobs WHERE scan_id = $scanId;";
    jobCmd.Parameters.AddWithValue("$scanId", scanId);

    string rootPath, programName, programParameters, searchPath;
    using (var reader = jobCmd.ExecuteReader())
    {
        if (!reader.Read()) { Console.Error.WriteLine($"scanId not found: {scanId}"); return 3; }
        rootPath = reader.GetString(0);
        programName = ReadStringOrEmpty(reader, 1);
        programParameters = ReadStringOrEmpty(reader, 2);
        searchPath = ReadStringOrEmpty(reader, 3);
    }

    var items = new List<ExportRow>();
    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = """
            SELECT f.file_path, f.file_type, f.status, f.reason_code, f.retry_count,
                COALESCE(m.assembly_name, ''), COALESCE(m.assembly_version, ''), COALESCE(m.public_key_token, ''),
                COALESCE(vr.vendor_name, ''), COALESCE(vr.status, ''), COALESCE(vr.confidence, 0),
                COALESCE(d.status, ''), COALESCE(d.artifact_path, ''), COALESCE(d.reason_code, '')
            FROM scan_files f
            LEFT JOIN assembly_metadata m ON m.scan_file_id = f.id
            LEFT JOIN vendor_results vr ON vr.scan_file_id = f.id
            LEFT JOIN decompilation_results d ON d.scan_file_id = f.id
            WHERE f.scan_id = $scanId ORDER BY f.id;
            """;
        cmd.Parameters.AddWithValue("$scanId", scanId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            items.Add(new ExportRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetInt64(10),
                reader.GetString(11), reader.GetString(12), reader.GetString(13)));
    }

    if (items.Count == 0) { Console.Error.WriteLine($"No files found for scanId: {scanId}"); return 3; }

    var writes = 0;
    foreach (var format in formats)
    {
        if (format == "json")
        {
            var path = ResolveOutputFile(outputPath, formats.Length > 1, $"{scanId}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var payload = new { scanId, generatedAtUtc = DateTime.UtcNow.ToString("O"), rootPath, programName, programParameters, searchPath, items };
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"exportJson: {path}"); writes++;
        }
        else if (format == "csv")
        {
            var path = ResolveOutputFile(outputPath, formats.Length > 1, $"{scanId}.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var sw = new StreamWriter(path, false);
            sw.WriteLine("file_path,file_type,status,reason_code,retry_count,assembly_name,assembly_version,public_key_token,vendor_name,vendor_status,vendor_confidence,decompile_status,decompile_artifact,decompile_reason");
            foreach (var item in items)
                sw.WriteLine($"{CsvEscape(item.FilePath)},{CsvEscape(item.FileType)},{CsvEscape(item.Status)},{CsvEscape(item.ReasonCode)},{item.RetryCount},{CsvEscape(item.AssemblyName)},{CsvEscape(item.AssemblyVersion)},{CsvEscape(item.PublicKeyToken)},{CsvEscape(item.VendorName)},{CsvEscape(item.VendorStatus)},{item.VendorConfidence},{CsvEscape(item.DecompileStatus)},{CsvEscape(item.DecompileArtifact)},{CsvEscape(item.DecompileReason)}");
            Console.WriteLine($"exportCsv: {path}"); writes++;
        }
    }

    Console.WriteLine($"exported: {writes} file(s)");
    return 0;
}

static int RunAnalyze(string[] args, string dbPath)
{
    var scanId = GetOption(args, "--id");
    if (string.IsNullOrWhiteSpace(scanId)) { Console.Error.WriteLine("Missing required option: --id <scanId>"); return 2; }

    var format = (GetOption(args, "--format") ?? "md").ToLowerInvariant();
    var outputArg = GetOption(args, "--output") ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "reports");
    var outputDir = Path.GetFullPath(outputArg);
    Directory.CreateDirectory(outputDir);

    using var connection = Database.OpenConnection(dbPath);

    try
    {
        var report = AnalysisReportBuilder.BuildReport(connection, scanId);
        var extension = format == "html" ? ".html" : ".md";
        var outputFile = Path.Combine(outputDir, $"{scanId}-analysis{extension}");

        if (format == "html")
            File.WriteAllText(outputFile, HtmlReportFormatter.Format(report));
        else
            File.WriteAllText(outputFile, MarkdownReportFormatter.Format(report));

        using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO analyze_reports(scan_id, format, generated_at_utc, output_path, status) VALUES($scanId, $format, $generatedAt, $outputPath, 'completed');";
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

// ── Helper methods ──

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    return null;
}

static string? GetOptionWithAliases(string[] args, params string[] names)
{
    foreach (var name in names)
    {
        var value = GetOption(args, name);
        if (value is not null) return value;
    }
    return null;
}

static bool HasFlag(string[] args, string flag)
{
    return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
}

static bool IsPolishHelp() => Thread.CurrentThread.CurrentUICulture.Name.StartsWith("pl", StringComparison.OrdinalIgnoreCase);

static void PrintUsage(bool polish)
{
    Console.WriteLine(polish ? "Użycie: InfoExeApp <komenda> [opcje]" : "Usage: InfoExeApp <command> [options]");
    Console.WriteLine();
    Console.WriteLine(polish ? "Komendy:" : "Commands:");
    Console.WriteLine(polish ? "  scan       Uruchom skan katalogu" : "  scan       Run a directory scan");
    Console.WriteLine(polish ? "  status     Pokaż status skanu" : "  status     Show scan status");
    Console.WriteLine(polish ? "  retry      Ponów nieudane operacje" : "  retry      Retry failed operations");
    Console.WriteLine(polish ? "  export     Eksportuj wyniki (json, csv)" : "  export     Export results (json, csv)");
    Console.WriteLine(polish ? "  analyze    Generuj raport analityczny" : "  analyze    Generate analysis report");
    Console.WriteLine(polish ? "  help       Pokaż tę pomoc" : "  help       Show this help");
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}. Use 'help' for usage.");
    return 1;
}

static string ReadPrompt(string prompt, bool required)
{
    Console.Write(prompt + " ");
    var line = Console.ReadLine();
    return (line ?? string.Empty).Trim();
}

static bool ReadYesNo(string prompt)
{
    Console.Write(prompt + " ");
    var line = Console.ReadLine();
    var answer = (line ?? string.Empty).Trim().ToLowerInvariant();
    return answer is "y" or "t" or "yes" or "tak";
}

static string ReadStringOrEmpty(SqliteDataReader reader, int ordinal)
    => reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);

static string DisplayValueOrDash(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

static long ScalarLong(SqliteConnection connection, string sql, string scanId)
{
    using var cmd = connection.CreateCommand();
    cmd.CommandText = sql;
    cmd.Parameters.AddWithValue("$scanId", scanId);
    var result = cmd.ExecuteScalar();
    return result is long l ? l : 0;
}

static string ResolveOutputFile(string basePath, bool multipleFormats, string fileName)
    => multipleFormats ? Path.Combine(basePath, fileName) : basePath;

static string CsvEscape(string value)
{
    if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        return $"\"{value.Replace("\"", "\"\"")}\"";
    return value;
}

// ── Analysis formatters (kept inline, will be refactored later) ──

static class AnalysisReportBuilder
{
    public static AnalysisReport BuildReport(SqliteConnection connection, string scanId) => new() { ScanId = scanId, GeneratedAtUtc = DateTime.UtcNow.ToString("O") };
}

public class AnalysisReport { public string ScanId { get; set; } = string.Empty; public string GeneratedAtUtc { get; set; } = string.Empty; }

static class HtmlReportFormatter { public static string Format(AnalysisReport r) => $"<html><body><h1>Analysis Report: {r.ScanId}</h1><p>Generated: {r.GeneratedAtUtc}</p></body></html>"; }

static class MarkdownReportFormatter { public static string Format(AnalysisReport r) => $"# Analysis Report: {r.ScanId}\n\nGenerated: {r.GeneratedAtUtc}"; }