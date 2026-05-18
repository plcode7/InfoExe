using System.Diagnostics;
using Microsoft.Win32;
using InfoExeCore.Models;

namespace InfoExeCore;

/// <summary>
/// Scans Windows Registry for application-related keys, suspicious entries,
/// broken paths, orphaned registrations, and configuration stores.
/// </summary>
public static class RegistryAnalyzer
{
    public static List<RegistryKeyFinding> AnalyzeAll(Action<string, bool>? log = null)
    {
        var results = new List<RegistryKeyFinding>();
        var discovered = DateTime.UtcNow;

        log?.Invoke("Analyzing Windows Registry...", false);

        // 1. Startup entries (HKLM + HKCU)
        AnalyzeStartupEntries(results, discovered, log);

        // 2. Uninstall entries (HKLM + HKCU)
        AnalyzeUninstallEntries(results, discovered, log);

        // 3. COM registrations
        AnalyzeComRegistrations(results, discovered, log);

        // 4. Shell extensions
        AnalyzeShellExtensions(results, discovered, log);

        // 5. Application configuration stores
        AnalyzeAppConfigurations(results, discovered, log);

        // 6. Database connection strings in registry
        AnalyzeConnectionStrings(results, discovered, log);

        // 7. Broken paths and orphaned entries
        AnalyzeBrokenPaths(results, discovered, log);

        // 8. Suspicious keys (unusual locations, hidden startups)
        AnalyzeSuspiciousKeys(results, discovered, log);

        // 9. License and registration keys
        AnalyzeLicenseKeys(results, discovered, log);

        return results;
    }

    private static void AnalyzeStartupEntries(List<RegistryKeyFinding> results, DateTime discovered, Action<string, bool>? log)
    {
        log?.Invoke("  Scanning startup entries...", false);

        var startupPaths = new (RegistryHive Hive, string Path)[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
            (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run"),
        };

        foreach (var (hive, path) in startupPaths)
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
                if (key is null) continue;

                foreach (var valueName in key.GetValueNames())
                {
                    var value = key.GetValue(valueName)?.ToString() ?? "";
                    var hiveName = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";

                    // Check for suspicious patterns
                    var severity = "info";
                    var description = $"Startup entry: {valueName} → {value}";

                    if (value.Contains("cmd.exe") || value.Contains("powershell") || value.Contains("wscript") || value.Contains("cscript"))
                    {
                        severity = "suspicious";
                        description += " [POTENTIALLY SUSPICIOUS: uses command interpreter]";
                    }
                    else if (value.Contains("Temp") || value.Contains("AppData\\Local\\Temp"))
                    {
                        severity = "warning";
                        description += " [Warning: located in Temp directory]";
                    }
                    else if (!File.Exists(value.Split(' ')[0].Trim('"')))
                    {
                        severity = "error";
                        description += $" [ERROR: file not found: {value.Split(' ')[0]}]";
                    }

                    results.Add(new RegistryKeyFinding(
                        $"startup-{Guid.NewGuid():N}",
                        hiveName, path, valueName, value, "REG_SZ",
                        severity, "startup", description,
                        severity == "error" ? "Remove or repair this entry" : null,
                        discovered
                    ));
                }
            }
            catch { /* skip inaccessible keys */ }
        }
    }

    private static void AnalyzeUninstallEntries(List<RegistryKeyFinding> results, DateTime discovered, Action<string, bool>? log)
    {
        log?.Invoke("  Scanning installed applications...", false);

        var uninstallPaths = new (RegistryHive Hive, string Path)[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        };

        foreach (var (hive, path) in uninstallPaths)
        {
            try
            {
                using var parentKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
                if (parentKey is null) continue;

                foreach (var subKeyName in parentKey.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = parentKey.OpenSubKey(subKeyName);
                        if (subKey is null) continue;

                        var displayName = subKey.GetValue("DisplayName") as string;
                        var installLocation = subKey.GetValue("InstallLocation") as string;
                        var uninstallString = subKey.GetValue("UninstallString") as string;
                        var publisher = subKey.GetValue("Publisher") as string;

                        if (string.IsNullOrWhiteSpace(displayName)) continue;

                        var hiveName = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
                        var fullPath = $@"{path}\{subKeyName}";

                        // Check if install location exists
                        if (!string.IsNullOrWhiteSpace(installLocation) && !Directory.Exists(installLocation) && !File.Exists(installLocation))
                        {
                            results.Add(new RegistryKeyFinding(
                                $"uninstall-orphan-{Guid.NewGuid():N}",
                                hiveName, fullPath, "InstallLocation", installLocation, "REG_SZ",
                                "warning", "broken_path",
                                $"Orphaned install: {displayName} — path not found: {installLocation}",
                                "Consider uninstalling or repairing this application",
                                discovered
                            ));
                        }

                        // Check if uninstaller exists
                        if (!string.IsNullOrWhiteSpace(uninstallString))
                        {
                            var uninstallerPath = uninstallString.Split(' ')[0].Trim('"');
                            if (!File.Exists(uninstallerPath))
                            {
                                results.Add(new RegistryKeyFinding(
                                    $"uninstall-broken-{Guid.NewGuid():N}",
                                    hiveName, fullPath, "UninstallString", uninstallString, "REG_SZ",
                                    "error", "broken_path",
                                    $"Broken uninstaller: {displayName} — {uninstallerPath} not found",
                                    "Manual cleanup required",
                                    discovered
                                ));
                            }
                        }

                        // Note: we don't add every app, just problematic ones
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private static void AnalyzeComRegistrations(List<RegistryKeyFinding> results, DateTime discovered, Action<string, bool>? log)
    {
        log?.Invoke("  Scanning COM registrations...", false);

        try
        {
            using var clsidKey = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64)
                .OpenSubKey(@"CLSID");
            if (clsidKey is null) return;

            foreach (var guid in clsidKey.GetSubKeyNames().Take(500))
            {
                try
                {
                    using var subKey = clsidKey.OpenSubKey(guid);
                    if (subKey is null) continue;

                    // Check InProcServer32
                    using var inprocKey = subKey.OpenSubKey("InProcServer32");
                    if (inprocKey is not null)
                    {
                        var dllPath = inprocKey.GetValue(null) as string;
                        if (!string.IsNullOrWhiteSpace(dllPath) && !File.Exists(dllPath))
                        {
                            results.Add(new RegistryKeyFinding(
                                $"com-broken-{Guid.NewGuid():N}",
                                "HKCR", $@"CLSID\{guid}\InProcServer32", "(default)", dllPath, "REG_SZ",
                                "error", "com_registration",
                                $"Broken COM registration: {dllPath} not found for CLSID {guid}",
                                "Use regsvr32 /u or cleanup tool",
                                discovered
                            ));
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static void AnalyzeShellExtensions(List<RegistryKeyFinding> results, DateTime discovered, Action<string, bool>? log)
    {
        log?.Invoke("  Scanning shell extensions...", false);

        var shellPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellIconOverlayIdentifiers",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellServiceObjects",
        };

        foreach (var shellPath in shellPaths)
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(shellPath);
                if (key is null) continue;

                foreach (var valueName in key.GetValueNames().Take(200))
                {
                    var value = key.GetValue(valueName)?.ToString() ?? "";
                    results.Add(new RegistryKeyFinding(
                        $"shell-{Guid.NewGuid():N}",
                        "HKLM", shellPath, valueName, value, "REG_SZ",
                        "info", "shell_extension",
                        $"Shell extension: {valueName}",
                        null, discovered
                    ));
                }
            }
            catch { }
        }
    }

    private static void AnalyzeAppConfigurations(List<RegistryKeyFinding> results, DateTime discovered, Action<string, bool>? log)
    {
        log?.Invoke("  Scanning application configurations...", false);

        var configPaths = new (RegistryHive Hive, string Path)[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\ODBC\ODBC.INI"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\MSSQLServer"),
            (RegistryHive.LocalMachine, @"SOFTWARE\MySQL AB"),
            (RegistryHive.LocalMachine, @"SOFTWARE\PostgreSQL"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Oracle"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Firebird Project"),
            (RegistryHive.CurrentUser, @"SOFTWARE\ODBC\ODBC.INI"),
        };

        foreach (var (hive, path) in configPaths)
        {
            try
            {
                var hiveName = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
                using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
                if (key is null) continue;

                results.Add(new RegistryKeyFinding(
                    $"appcfg-{Guid.NewGuid():N}",
                    hiveName, path, null, null, null,
                    "info", "app_config",
                    $"Application configuration found: {path}",
                    null, discovered
                ));
            }
            catch { }
        }
    }

    private static void AnalyzeConnectionStrings(List<RegistryKeyFinding> results, DateTime discovered, Action<string, bool>? log)
    {
        log?.Invoke("  Scanning for connection strings...", false);

        var searchTerms = new[] { "connection", "database", "datasource", "server", "dsn" };
        var searchPaths = new (RegistryHive Hive, string Path)[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE"),
            (RegistryHive.CurrentUser, @"SOFTWARE"),
        };

        foreach (var (hive, path) in searchPaths)
        {
            SearchRegistryForTerms(results, hive, path, searchTerms, discovered, 3);
        }
    }

    private static void SearchRegistryForTerms(List<RegistryKeyFinding> results, RegistryHive hive,
        string path, string[] terms, DateTime discovered, int depth)
    {
        if (depth <= 0) return;
        try
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
            if (key is null) return;

            var hiveName = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";

            foreach (var valueName in key.GetValueNames().Take(50))
            {
                var value = key.GetValue(valueName)?.ToString() ?? "";
                var matchesAny = terms.Any(t => valueName.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                                                 value.Contains(t, StringComparison.OrdinalIgnoreCase));
                if (!matchesAny) continue;

                // Mask credentials
                var masked = MaskSensitiveData(value);

                results.Add(new RegistryKeyFinding(
                    $"connstr-{Guid.NewGuid():N}",
                    hiveName, path, valueName, masked, key.GetValueKind(valueName).ToString(),
                    "info", "connection_string",
                    $"Connection-related registry value: {valueName}",
                    null, discovered
                ));
            }

            foreach (var subKeyName in key.GetSubKeyNames().Take(20))
            {
                SearchRegistryForTerms(results, hive, $@"{path}\{subKeyName}", terms, discovered, depth - 1);
            }
        }
        catch { }
    }

    private static string MaskSensitiveData(string value)
    {
        // Mask passwords in connection strings
        var patterns = new (string Pattern, string Replacement)[]
        {
            (@"(?i)password\s*=\s*[^;]+", "Password=***"),
            (@"(?i)pwd\s*=\s*[^;]+", "Pwd=***"),
            (@"(?i)User ID\s*=\s*[^;]+", "User ID=***"),
        };

        foreach (var (pattern, replacement) in patterns)
        {
            value = System.Text.RegularExpressions.Regex.Replace(value, pattern, replacement);
        }
        return value;
    }

    private static void AnalyzeBrokenPaths(List<RegistryKeyFinding> results, DateTime discovered, Action<string, bool>? log)
    {
        log?.Invoke("  Scanning for broken paths...", false);

        var pathKeys = new (RegistryHive Hive, string Path)[]
        {
            (RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services"),
        };

        foreach (var (hive, path) in pathKeys)
        {
            try
            {
                using var servicesKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
                if (servicesKey is null) continue;

                foreach (var serviceName in servicesKey.GetSubKeyNames().Take(200))
                {
                    try
                    {
                        using var svcKey = servicesKey.OpenSubKey(serviceName);
                        if (svcKey is null) continue;

                        var imagePath = svcKey.GetValue("ImagePath") as string;
                        if (string.IsNullOrWhiteSpace(imagePath)) continue;

                        var exePath = imagePath.Split(' ')[0].Trim('"');
                        if (!File.Exists(exePath))
                        {
                            results.Add(new RegistryKeyFinding(
                                $"broken-svc-{Guid.NewGuid():N}",
                                "HKLM", $@"{path}\{serviceName}", "ImagePath", imagePath, "REG_EXPAND_SZ",
                                "error", "broken_path",
                                $"Broken service: {serviceName} — {exePath} not found",
                                "Service may need repair or removal",
                                discovered
                            ));
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private static void AnalyzeSuspiciousKeys(List<RegistryKeyFinding> results, DateTime discovered, Action<string, bool>? log)
    {
        log?.Invoke("  Scanning for suspicious keys...", false);

        var suspiciousPaths = new (RegistryHive Hive, string Path, string Description)[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects", "Browser Helper Objects (BHO)"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\Notify", "Winlogon notification packages"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell", "Custom shell (potential hijack)"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\Userinit", "Userinit modification"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", "Image hijack options"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellExecuteHooks", "Shell execute hooks"),
        };

        foreach (var (hive, path, description) in suspiciousPaths)
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
                if (key is null) continue;

                var hiveName = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";

                foreach (var valueName in key.GetValueNames())
                {
                    var value = key.GetValue(valueName)?.ToString() ?? "";
                    results.Add(new RegistryKeyFinding(
                        $"suspicious-{Guid.NewGuid():N}",
                        hiveName, path, valueName, value, "REG_SZ",
                        "suspicious", "unknown",
                        $"[SUSPICIOUS] {description}: {valueName} = {value}",
                        "Verify this entry is legitimate",
                        discovered
                    ));
                }
            }
            catch { }
        }
    }

    private static void AnalyzeLicenseKeys(List<RegistryKeyFinding> results, DateTime discovered, Action<string, bool>? log)
    {
        log?.Invoke("  Scanning for license/registration keys...", false);

        var licensePaths = new (RegistryHive Hive, string Path)[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion"),
        };

        var licenseTerms = new[] { "ProductId", "DigitalProductId", "License", "Registration", "Serial" };

        foreach (var (hive, path) in licensePaths)
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
                if (key is null) continue;

                var hiveName = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";

                foreach (var valueName in key.GetValueNames())
                {
                    if (!licenseTerms.Any(t => valueName.Contains(t, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var value = key.GetValue(valueName);
                    var valueStr = value is byte[] bytes
                        ? $"BINARY ({bytes.Length} bytes)"
                        : value?.ToString() ?? "";

                    // Mask long IDs
                    if (valueStr.Length > 20)
                        valueStr = valueStr[..20] + "...";

                    results.Add(new RegistryKeyFinding(
                        $"license-{Guid.NewGuid():N}",
                        hiveName, path, valueName, valueStr,
                        key.GetValueKind(valueName).ToString(),
                        "info", "license",
                        $"License/registration key: {valueName}",
                        null, discovered
                    ));
                }
            }
            catch { }
        }
    }
}