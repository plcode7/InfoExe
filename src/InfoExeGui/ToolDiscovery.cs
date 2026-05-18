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
                    var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var toolPath = Path.Combine(homeDir, ".dotnet", "tools", "ilspycmd.exe");
                    if (File.Exists(toolPath)) return toolPath;
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"FindIlSpy (dotnet tool list): {ex.Message}"); }

        // 2. Check PATH
        try
        {
            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(dir.Trim(), "ilspycmd.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"FindIlSpy (PATH): {ex.Message}"); }

        // 3. Check well-known locations
        try
        {
            var wellKnown = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", "ilspycmd.exe"),
            };
            foreach (var path in wellKnown)
                if (File.Exists(path)) return path;
        }
        catch (Exception ex) { Debug.WriteLine($"FindIlSpy (well-known): {ex.Message}"); }

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
        catch (Exception ex)
        {
            Debug.WriteLine($"IsDotNetSdkAvailable: {ex.Message}");
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
        catch (Exception ex)
        {
            Debug.WriteLine($"GetDotNetVersion: {ex.Message}");
            return null;
        }
    }
}