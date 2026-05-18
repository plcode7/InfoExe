using System.Reflection;
using InfoExeCore.Models;

namespace InfoExeCore;

/// <summary>
/// File classification logic — determines file type (dotnet-managed, python, etc).
/// </summary>
public static class Classifier
{
    public static ClassificationResult ClassifyFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".py" or ".whl")
            return new ClassificationResult(Constants.FileTypePythonArtifact, Constants.StatusProcessed, null, null);

        if (ext is ".dll" or ".exe" or ".winmd")
        {
            try
            {
                var assemblyName = AssemblyName.GetAssemblyName(path);
                return new ClassificationResult(Constants.FileTypeDotNetManaged, Constants.StatusProcessed, null, assemblyName);
            }
            catch (BadImageFormatException)
            {
                return new ClassificationResult("native-or-unsupported", Constants.StatusPartial, "unsupported-format", null);
            }
            catch (FileLoadException)
            {
                return new ClassificationResult("native-or-unsupported", Constants.StatusPartial, "metadata-unreadable", null);
            }
            catch (IOException)
            {
                return new ClassificationResult("native-or-unsupported", Constants.StatusFailed, "io-error", null);
            }
            catch (UnauthorizedAccessException)
            {
                return new ClassificationResult("native-or-unsupported", Constants.StatusFailed, "access-denied", null);
            }
        }

        return new ClassificationResult("ignored", Constants.StatusPartial, "unsupported-extension", null);
    }

    public static IEnumerable<string> EnumerateCandidateFiles(string searchPath, bool includePython)
    {
        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".dll", ".exe", ".winmd" };
        if (includePython)
        {
            allowedExtensions.Add(".py");
            allowedExtensions.Add(".whl");
        }

        if (File.Exists(searchPath))
        {
            if (allowedExtensions.Contains(Path.GetExtension(searchPath)))
                yield return searchPath;
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(searchPath, "*", SearchOption.AllDirectories))
        {
            if (allowedExtensions.Contains(Path.GetExtension(file)))
                yield return file;
        }
    }
}