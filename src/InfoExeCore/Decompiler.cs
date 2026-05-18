using System.Diagnostics;
using InfoExeCore.Models;

namespace InfoExeCore;

/// <summary>
/// ILSpy-based decompilation logic. Shared between CLI and GUI.
/// </summary>
public static class Decompiler
{
    public static DecompileResult AttemptDecompile(string filePath, string scanId, long scanFileId)
    {
        var outDir = Path.Combine(Environment.CurrentDirectory, "artifacts", "decompiled", scanId, scanFileId.ToString());
        Directory.CreateDirectory(outDir);

        var ilspyPath = FindExecutable("ilspycmd");
        if (ilspyPath is null)
        {
            var notePath = Path.Combine(outDir, "decompile-note.txt");
            File.WriteAllText(notePath, "ilspycmd not found; decompilation deferred.");
            return new DecompileResult(Constants.DecompileStatusSkipped, notePath, Constants.ReasonIlSpyNotFound);
        }

        var psi = new ProcessStartInfo
        {
            FileName = ilspyPath,
            Arguments = $"--disable-updatecheck -p -o \"{outDir}\" \"{filePath}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(psi);
        if (process is null)
            return new DecompileResult(Constants.DecompileStatusFailed, outDir, Constants.ReasonIlSpyError);

        if (!process.WaitForExit(60_000))
        {
            process.Kill(true);
            return new DecompileResult(Constants.DecompileStatusFailed, outDir, "decompile-timeout");
        }

        return process.ExitCode == 0
            ? new DecompileResult(Constants.DecompileStatusDecompiled, outDir, null)
            : new DecompileResult(Constants.DecompileStatusFailed, outDir, Constants.ReasonIlSpyError);
    }

    public static string? FindExecutable(string commandName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue)) return null;

        foreach (var path in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(path.Trim(), $"{commandName}.exe");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}