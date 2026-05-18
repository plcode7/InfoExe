using System.Diagnostics;
using Microsoft.Data.Sqlite;
using InfoExeCore;
using InfoExeCore.Models;

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

            // SEC-01: Validate dbPath before using
            var safeDbPath = PathValidator.ValidateDbPath(dbPath);
            var absoluteRoot = PathValidator.ValidateScanRoot(rootPath);
            var absoluteSearchPath = PathValidator.ValidateSearchPath(absoluteRoot, searchPath);

            var resolvedProgramName = string.IsNullOrWhiteSpace(programName)
                ? Database.DefaultProgramName(absoluteRoot)
                : programName.Trim();

            using var connection = new SqliteConnection($"Data Source={safeDbPath}");
            connection.Open();
            Database.InitializeSchema(connection);
            using var tx = connection.BeginTransaction();

            Database.InsertScanJob(connection, tx, scanId, absoluteRoot, resolvedProgramName, programParameters, absoluteSearchPath);

            var files = Classifier.EnumerateCandidateFiles(absoluteSearchPath, includePython).ToList();
            var totalFiles = files.Count;
            var processed = 0;
            var partialCount = 0;
            var failedCount = 0;

            foreach (var file in files)
            {
                ReportProgress(progress, processed, totalFiles, file, "classifying");

                var classification = Classifier.ClassifyFile(file);
                var scanFileId = Database.InsertScanFile(connection, tx, scanId, file, classification);

                if (classification.FileType == Constants.FileTypeDotNetManaged)
                {
                    ReportProgress(progress, processed, totalFiles, file, "metadata");
                    var metadata = MetadataExtractor.ExtractManagedMetadata(file, classification);
                    Database.InsertAssemblyMetadata(connection, tx, scanFileId, metadata);

                    var evidence = VendorAnalyzer.BuildVendorEvidence(file, metadata);
                    Database.InsertVendorEvidence(connection, tx, scanFileId, evidence);
                    Database.InsertVendorResult(connection, tx, scanFileId, evidence);

                    ReportProgress(progress, processed, totalFiles, file, "decompiling");
                    _logCallback($"Decompiling: {Path.GetFileName(file)}", false);
                    var decompileResult = Decompiler.AttemptDecompile(file, scanId, scanFileId);
                    Database.InsertDecompileResult(connection, tx, scanFileId, decompileResult);

                    if (decompileResult.Status != Constants.DecompileStatusDecompiled)
                    {
                        Database.MarkPartialWithReason(connection, tx, scanFileId, decompileResult.ReasonCode);
                        partialCount++;
                    }
                    else
                    {
                        processed++;
                    }
                }
                else if (classification.Status == Constants.StatusFailed)
                {
                    failedCount++;
                }
                else
                {
                    processed++;
                }
            }

            Database.CompleteScanJob(connection, tx, scanId);
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

    // BUG-01: Use dedicated method with proper synchronization
    private static void ReportProgress(IProgress<ScanProgress>? progress, int processed, int total, string file, string stage)
    {
        if (progress is null) return;
        progress.Report(new ScanProgress
        {
            FilesProcessed = processed,
            TotalFiles = total,
            CurrentFile = file,
            Stage = stage
        });
    }
}