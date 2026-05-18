namespace InfoExeCore.Models;

public sealed record ExportRow(
    string FilePath,
    string FileType,
    string Status,
    string ReasonCode,
    long RetryCount,
    string AssemblyName,
    string AssemblyVersion,
    string PublicKeyToken,
    string VendorName,
    string VendorStatus,
    long VendorConfidence,
    string DecompileStatus,
    string DecompileArtifact,
    string DecompileReason
);