using System.Diagnostics;

namespace InfoExeGui;

public static class ToolDiscovery
{
    public static string? FindIlSpy()
    {
        // 1. Check dotnet tool list --global
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "tool list --global",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is not null)
            {
                process.WaitForExit(5000);
                var output = process.StandardOutput.ReadToEnd();
                if (output.Contains("ilspycmd"))
                {
                    // ilspycmd is installed as global tool — find the actual exe
                    var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var toolPath = Path.Combine(homeDir, ".dotnet", "tools", "ilspycmd.exe");
                    if (File.Exists(toolPath))
                        return toolPath;
                }
            }
        }
        catch { }

        // 2. Check PATH
        try
        {
            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(dir.Trim(), "ilspycmd.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        catch { }

        // 3. Check well-known locations
        var wellKnown = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", "ilspycmd.exe"),
        };
        foreach (var path in wellKnown)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    public static bool IsDotNetSdkAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static string? GetDotNetVersion()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return null;
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}