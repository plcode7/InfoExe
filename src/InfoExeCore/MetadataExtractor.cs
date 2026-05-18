using System.Diagnostics;
using System.Reflection;
using InfoExeCore.Models;

namespace InfoExeCore;

/// <summary>
/// Extracts managed metadata from .NET assemblies.
/// </summary>
public static class MetadataExtractor
{
    /// <summary>
    /// SEC-02: Extracts metadata using reflection.
    /// IMPORTANT: This loads assemblies via AssemblyName.GetAssemblyName() which only reads
    /// the assembly metadata header, NOT the full assembly. It does NOT execute any code.
    /// The analyze command's Assembly.LoadFrom is more dangerous — see warning in that method.
    /// </summary>
    public static ManagedMetadata ExtractManagedMetadata(string path, ClassificationResult classification)
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
            publicKeyToken,
            false,
            false,
            false,
            string.Empty,
            companyName);
    }
}