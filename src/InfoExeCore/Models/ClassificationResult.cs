using System.Reflection;

namespace InfoExeCore.Models;

public sealed record ClassificationResult(
    string FileType,
    string Status,
    string? ReasonCode,
    AssemblyName? AssemblyName
);