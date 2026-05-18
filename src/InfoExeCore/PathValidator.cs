namespace InfoExeCore;

/// <summary>
/// Centralized path validation and sanitization.
/// Addresses SEC-01 (SQL injection via dbPath), SEC-03 (path traversal).
/// </summary>
public static class PathValidator
{
    /// <summary>
    /// Validates and normalizes a database file path.
    /// Rejects paths containing ';' to prevent connection string injection.
    /// </summary>
    public static string ValidateDbPath(string rawPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPath);

        // SEC-01: Prevent connection string injection
        if (rawPath.Contains(';'))
            throw new ArgumentException("Database path contains invalid characters (;)");

        var normalized = Path.GetFullPath(rawPath);
        if (normalized.Length > Constants.MaxPathLength)
            throw new ArgumentException($"Path exceeds maximum length of {Constants.MaxPathLength} characters");

        return normalized;
    }

    /// <summary>
    /// Validates a scan root path, blocking path traversal and non-existent directories.
    /// </summary>
    public static string ValidateScanRoot(string rawPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPath);

        // SEC-03: Block path traversal
        if (rawPath.Contains(".."))
            throw new ArgumentException("Path traversal detected in scan root");

        var normalized = Path.GetFullPath(rawPath);
        if (normalized.Length > Constants.MaxPathLength)
            throw new ArgumentException($"Path exceeds maximum length of {Constants.MaxPathLength} characters");

        if (!Directory.Exists(normalized))
            throw new DirectoryNotFoundException($"Directory not found: {normalized}");

        return normalized;
    }

    /// <summary>
    /// Validates a search path, ensuring it exists and is within the root path.
    /// </summary>
    public static string ValidateSearchPath(string rootPath, string? searchPath)
    {
        var absoluteRoot = ValidateScanRoot(rootPath);

        if (string.IsNullOrWhiteSpace(searchPath))
            return absoluteRoot;

        if (searchPath.Contains(".."))
            throw new ArgumentException("Path traversal detected in search path");

        var normalized = Path.GetFullPath(searchPath);

        if (!File.Exists(normalized) && !Directory.Exists(normalized))
            throw new FileNotFoundException($"Search path not found: {normalized}");

        if (!IsPathWithinRoot(absoluteRoot, normalized))
            throw new ArgumentException($"Search path must be inside rootPath: {normalized}");

        return normalized;
    }

    public static bool IsPathWithinRoot(string rootPath, string candidatePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));

        if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            return true;

        return normalizedCandidate.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
}