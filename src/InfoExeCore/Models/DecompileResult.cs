namespace InfoExeCore.Models;

public sealed record DecompileResult(
    string Status,
    string ArtifactPath,
    string? ReasonCode
);