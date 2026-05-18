namespace InfoExeCore.Models;

public sealed record ManagedMetadata(
    string AssemblyName,
    string AssemblyVersion,
    string PublicKeyToken,
    bool IsSingleFile,
    bool IsReadyToRun,
    bool IsNativeAotLimited,
    string TargetFramework,
    string CompanyName
);