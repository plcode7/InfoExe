using Microsoft.Data.Sqlite;
using InfoExeCore.Models;

namespace InfoExeCore;

/// <summary>
/// Shared database operations used by both CLI and GUI.
/// Eliminates code duplication between Program.cs and ScanService.cs.
/// </summary>
public static class Database
{
    public static SqliteConnection OpenConnection(string dbPath)
    {
        // SEC-01: Validate path before constructing connection string
        var safePath = PathValidator.ValidateDbPath(dbPath);
        Directory.CreateDirectory(Path.GetDirectoryName(safePath)!);

        var connection = new SqliteConnection($"Data Source={safePath}");
        connection.Open();
        return connection;
    }

    public static void InitializeSchema(SqliteConnection connection)
    {
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

    public static void InsertScanJob(SqliteConnection connection, SqliteTransaction tx,
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

    public static void CompleteScanJob(SqliteConnection connection, SqliteTransaction tx, string scanId)
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

    public static long InsertScanFile(SqliteConnection connection, SqliteTransaction tx,
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

    public static void InsertAssemblyMetadata(SqliteConnection connection, SqliteTransaction tx,
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

    public static void InsertVendorEvidence(SqliteConnection connection, SqliteTransaction tx,
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

    public static void InsertVendorResult(SqliteConnection connection, SqliteTransaction tx,
        long scanFileId, IReadOnlyList<VendorEvidence> evidenceList)
    {
        var distinctVendors = evidenceList.Select(e => e.VendorName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var status = Constants.VendorStatusInconclusive;
        var vendorName = Constants.VendorStatusInconclusive;
        var confidence = 0;
        if (distinctVendors.Count == 1)
        {
            status = Constants.VendorStatusAttributed;
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

    public static void InsertDecompileResult(SqliteConnection connection, SqliteTransaction tx,
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

    public static void UpsertDecompileResult(SqliteConnection connection, SqliteTransaction tx,
        long scanFileId, DecompileResult result)
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

    public static void MarkPartialWithReason(SqliteConnection connection, SqliteTransaction tx,
        long scanFileId, string? reasonCode)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE scan_files SET status = 'partial', reason_code = $reason WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$reason", reasonCode ?? Constants.ReasonUnknown);
        cmd.Parameters.AddWithValue("$id", scanFileId);
        cmd.ExecuteNonQuery();
    }

    public static string DefaultProgramName(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) return string.Empty;
        var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }
}