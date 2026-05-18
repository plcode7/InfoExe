using InfoExeCore.Models;

namespace InfoExeCore;

/// <summary>
/// Vendor attribution logic — identifies the vendor/creator of assemblies.
/// </summary>
public static class VendorAnalyzer
{
    public static IReadOnlyList<VendorEvidence> BuildVendorEvidence(string path, ManagedMetadata metadata)
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
}