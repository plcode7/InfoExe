using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace InfoExeGui;

public class ScanProgress
{
    public int FilesProcessed { get; set; }
    public int TotalFiles { get; set; }
    public string CurrentFile { get; set; } = string.Empty;
    public string Stage { get; set; } = "starting";
}

public class ScanResult
{
    public string ScanId { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int PartialFiles { get; set; }
    public int FailedFiles { get; set; }
}

public class ScanService
{
    private readonly Action<string, bool> _logCallback;

    public ScanService(Action<string, bool> logCallback)
    {
        _logCallback = logCallback;
    }

    public async Task<ScanResult> RunScanAsync(
        string rootPath,
        string dbPath,
        string programName,
        string searchPath,
        string programParameters,
        bool includePython,
        IProgress<ScanProgress>? progress = null)
    {
        return await Task.Run(() =>
        {
            var scanId = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
            InitializeDatabase(dbPath);

            var absoluteRoot = Path.GetFullPath(rootPath);
            var absoluteSearchPath = string.IsNullOrWhiteSpace(searchPath) ? absoluteRoot : Path.GetFullPath(searchPath);

            var resolvedProgramName = string.IsNullOrWhiteSpace(programName)
                ? DefaultProgramName(absoluteRoot)
                : programName.Trim();

            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();
            using var tx = connection.BeginTransaction();

            InsertScanJob(connection, tx, scanId, absoluteRoot, resolvedProgramName, programParameters, absoluteSearchPath);

            var files = EnumerateCandidateFiles(absoluteSearchPath, includePython).ToList();
            var totalFiles = files.Count;
            var processed = 0;
            var partialCount = 0;
            var failedCount = 0;

            foreach (var file in files)
            {
                progress?.Report(new ScanProgress
                {
                    FilesProcessed = processed,
                    TotalFiles = totalFiles,
                    CurrentFile = file,
                    Stage = "classifying"
                });

                var classification = ClassifyFile(file);
                var scanFileId = InsertScanFile(connection, tx, scanId, file, classification);

                if (classification.FileType == "dotnet-managed")
                {
                    progress?.Report(new ScanProgress
                    {
                        FilesProcessed = processed,
                        TotalFiles = totalFiles,
                        CurrentFile = file,
                        Stage = "metadata"
                    });

                    var metadata = ExtractManagedMetadata(file, classification);
                    InsertAssemblyMetadata(connection, tx, scanFileId, metadata);

                    var evidence = BuildVendorEvidence(file, metadata);
                    InsertVendorEvidence(connection, tx, scanFileId, evidence);
                    InsertVendorResult(connection, tx, scanFileId, evidence);

                    progress?.Report(new ScanProgress
                    {
                        FilesProcessed = processed,
                        TotalFiles = totalFiles,
                        CurrentFile = file,
                        Stage = "decompiling"
                    });

                    _logCallback($"Decompiling: {Path.GetFileName(file)}", false);
                    var decompileResult = AttemptDecompile(file, scanId, scanFileId);
                    InsertDecompileResult(connection, tx, scanFileId, decompileResult);

                    if (decompileResult.Status != "decompiled")
                    {
                        MarkPartialWithReason(connection, tx, scanFileId, decompileResult.ReasonCode);
                        partialCount++;
                    }
                    else
                    {
                        processed++;
                    }
                }
                else if (classification.Status == "failed")
                {
                    failedCount++;
                }
                else
                {
                    processed++;
                }
            }

            CompleteScanJob(connection, tx, scanId);
            tx.Commit();

            _logCallback($"Scan complete: {scanId}", false);
            _logCallback($"Files: {totalFiles} total, {processed} processed, {partialCount} partial, {failedCount} failed", false);

            return new ScanResult
            {
                ScanId = scanId,
                RootPath = absoluteRoot,
                ProgramName = resolvedProgramName,
                TotalFiles = totalFiles,
                ProcessedFiles = processed,
                PartialFiles = partialCount,
                FailedFiles = failedCount
            };
        });
    }

    private static string DefaultProgramName(string rootPath)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    private static void InitializeDatabase(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
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
    }

    private static void InsertScanJob(SqliteConnection connection, SqliteTransaction tx,
        string scanId, string rootPath, string programName, string programParameters, string searchPath)
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

    private static void CompleteScanJob(SqliteConnection connection, SqliteTransaction tx, string scanId)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE scan_jobs SET status = 'completed', finished_at_utc = $finishedAt WHERE scan_id = $scanId;
            """;
        cmd.Parameters.AddWithValue("$finishedAt", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$scanId", scanId);
        cmd.ExecuteNonQuery();
    }

    private static long InsertScanFile(SqliteConnection connection, SqliteTransaction tx,
        string scanId, string filePath, ClassificationResult classification)
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

    private static void InsertAssemblyMetadata(SqliteConnection connection, SqliteTransaction tx,
        long scanFileId, ManagedMetadata metadata)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO assembly_metadata(scan_file_id, assembly_name, assembly_version, public_key_token,
                is_single_file, is_ready_to_run, is_native_aot_limited, target_framework)
            VALUES($scanFileId, $name, $version, $token, $isSingleFile, $isReadyToRun, $isNativeAot, $targetFramework);
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

    private static void InsertVendorEvidence(SqliteConnection connection, SqliteTransaction tx,
        long scanFileId, IReadOnlyList<VendorEvidence> evidenceList)
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

    private static void InsertVendorResult(SqliteConnection connection, SqliteTransaction tx,
        long scanFileId, IReadOnlyList<VendorEvidence> evidenceList)
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

    private static void InsertDecompileResult(SqliteConnection connection, SqliteTransaction tx,
        long scanFileId, DecompileResult result)
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

    private static void MarkPartialWithReason(SqliteConnection connection, SqliteTransaction tx,
        long scanFileId, string? reasonCode)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE scan_files SET status = 'partial', reason_code = $reason WHERE id = $id;";
        cmd.Parameters.AddWithValue("$reason", reasonCode ?? "unknown");
        cmd.Parameters.AddWithValue("$id", scanFileId);
        cmd.ExecuteNonQuery();
    }

    private static ClassificationResult ClassifyFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".py" or ".whl")
            return new ClassificationResult("python-artifact", "processed", null, null);

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
            catch
            {
                return new ClassificationResult("native-or-unsupported", "partial", "metadata-unreadable", null);
            }
        }

        return new ClassificationResult("ignored", "partial", "unsupported-extension", null);
    }

    private static ManagedMetadata ExtractManagedMetadata(string path, ClassificationResult classification)
    {
        var assembly = classification.AssemblyName!;
        var token = assembly.GetPublicKeyToken();
        var publicKeyToken = token is { Length: > 0 }
            ? BitConverter.ToString(token).Replace("-", "").ToLowerInvariant()
            : string.Empty;
        var companyName = FileVersionInfo.GetVersionInfo(path).CompanyName ?? string.Empty;

        return new ManagedMetadata(
            assembly.Name ?? Path.GetFileNameWithoutExtension(path),
            assembly.Version?.ToString() ?? string.Empty,
            publicKeyToken, false, false, false, string.Empty, companyName);
    }

    private static IReadOnlyList<VendorEvidence> BuildVendorEvidence(string path, ManagedMetadata metadata)
    {
        var evidence = new List<VendorEvidence>();
        if (!string.IsNullOrWhiteSpace(metadata.CompanyName))
            evidence.Add(new VendorEvidence("file-version-company", metadata.CompanyName, 60));
        if (!string.IsNullOrWhiteSpace(metadata.PublicKeyToken))
            evidence.Add(new VendorEvidence("assembly-public-key-token", metadata.PublicKeyToken, 35));
        if (evidence.Count == 0)
            evidence.Add(new VendorEvidence("filename-fallback", Path.GetFileNameWithoutExtension(path), 20));
        return evidence;
    }

    private DecompileResult AttemptDecompile(string filePath, string scanId, long scanFileId)
    {
        var outDir = Path.Combine(Environment.CurrentDirectory, "artifacts", "decompiled", scanId, scanFileId.ToString());
        Directory.CreateDirectory(outDir);

        var ilspyPath = ToolDiscovery.FindIlSpy();
        if (ilspyPath is null)
        {
            var notePath = Path.Combine(outDir, "decompile-note.txt");
            File.WriteAllText(notePath, "ilspycmd not found; decompilation deferred.");
            _logCallback($"  [SKIP] ilspycmd not found for: {Path.GetFileName(filePath)}", true);
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
            return new DecompileResult("partial", outDir, "decompile-start-failed");

        // Pump stderr as live output
        _ = Task.Run(() =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = process.StandardError.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                    _logCallback($"  {line}", false);
            }
        });

        if (!process.WaitForExit(60000))
        {
            process.Kill(true);
            return new DecompileResult("partial", outDir, "decompile-timeout");
        }

        return process.ExitCode == 0
            ? new DecompileResult("decompiled", outDir, null)
            : new DecompileResult("partial", outDir, "decompile-failed");
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string searchPath, bool includePython)
    {
        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".dll", ".exe" };
        if (includePython) { allowedExtensions.Add(".py"); allowedExtensions.Add(".whl"); }

        if (File.Exists(searchPath))
        {
            var ext = Path.GetExtension(searchPath);
            if (allowedExtensions.Contains(ext))
                yield return searchPath;
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(searchPath, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file);
            if (allowedExtensions.Contains(ext))
                yield return file;
        }
    }
}

internal sealed record ClassificationResult(string FileType, string Status, string? ReasonCode, System.Reflection.AssemblyName? AssemblyName);
internal sealed record ManagedMetadata(string AssemblyName, string AssemblyVersion, string PublicKeyToken, bool IsSingleFile, bool IsReadyToRun, bool IsNativeAotLimited, string TargetFramework, string CompanyName);
internal sealed record VendorEvidence(string EvidenceType, string VendorName, int Confidence);
internal sealed record DecompileResult(string Status, string ArtifactPath, string? ReasonCode);