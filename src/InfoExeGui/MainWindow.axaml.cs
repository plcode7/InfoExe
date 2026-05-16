using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace InfoExeGui;

public partial class MainWindow : Window
{
    private string? _lastReportPath;
    private string? _lastReportFormat;
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            CliPathBox.Text = ResolveDefaultCliPath();
            DbPathBox.Text = ResolveDefaultDbPath();
            UpdatePreview();
            LoadScans();
        };
    }

    private async void BrowseCli_Click(object? sender, RoutedEventArgs e)
    {
        var storage = GetStorageProvider();
        if (storage is null)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz InfoExeApp.exe",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Aplikacje EXE") { Patterns = ["*.exe"] }]
        });

        if (files.Count > 0)
        {
            CliPathBox.Text = files[0].Path.LocalPath;
            UpdatePreview();
        }
    }

    private async void BrowseDb_Click(object? sender, RoutedEventArgs e)
    {
        var storage = GetStorageProvider();
        if (storage is null)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Wybierz folder bazy danych",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            DbPathBox.Text = Path.Combine(folders[0].Path.LocalPath, "infoexe.db");
            UpdatePreview();
        }
    }

    private async void BrowseRoot_Click(object? sender, RoutedEventArgs e)
    {
        var storage = GetStorageProvider();
        if (storage is null)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Wybierz katalog do skanowania",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            RootPathBox.Text = folders[0].Path.LocalPath;
            UpdatePreview();
        }
    }

    private async void BrowseSearch_Click(object? sender, RoutedEventArgs e)
    {
        var storage = GetStorageProvider();
        if (storage is null)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Wybierz katalog do przeszukania",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            SearchPathBox.Text = folders[0].Path.LocalPath;
            UpdatePreview();
        }
    }

    private void TextFieldChanged(object? sender, TextChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void OptionChanged(object? sender, RoutedEventArgs e)
    {
        UpdatePreview();
    }

    private async void Help_Click(object? sender, RoutedEventArgs e)
    {
        var cliPath = (CliPathBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cliPath))
        {
            cliPath = ResolveDefaultCliPath();
        }

        if (string.IsNullOrWhiteSpace(cliPath) || !File.Exists(cliPath))
        {
            ShowError("Nie znaleziono pliku CLI. Wskaż InfoExeApp.exe.");
            return;
        }

        var dbPath = string.IsNullOrWhiteSpace(DbPathBox.Text) ? ResolveDefaultDbPath() : Path.GetFullPath(DbPathBox.Text.Trim());
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        await RunCliAsync(new[] { "help" }, cliPath, dbPath, appendHeader: true);
    }

    private async void RunScan_Click(object? sender, RoutedEventArgs e)
    {
        if (!ValidateInputs(out var cliPath, out var dbPath, out var rootPath, out var appName, out var searchPath, out var programArgs, out var includePython))
        {
            return;
        }

        SetBusy(true, "Skanuję...");
        try
        {
            var args = new List<string>
            {
                "scan",
                "--root", rootPath,
                "--app", appName,
                "--path", searchPath,
                "--db", dbPath
            };

            if (!string.IsNullOrWhiteSpace(programArgs))
            {
                args.Add("--args");
                args.Add(programArgs);
            }

            if (includePython)
            {
                args.Add("--python");
            }

            await RunCliAsync(args, cliPath, dbPath, appendHeader: true);
            LoadScans();
        }
        finally
        {
            SetBusy(false, "Gotowe");
        }
    }

    private bool ValidateInputs(out string cliPath, out string dbPath, out string rootPath, out string appName, out string searchPath, out string programArgs, out bool includePython)
    {
        cliPath = (CliPathBox.Text ?? string.Empty).Trim();
        dbPath = string.IsNullOrWhiteSpace(DbPathBox.Text) ? ResolveDefaultDbPath() : Path.GetFullPath(DbPathBox.Text.Trim());
        rootPath = (RootPathBox.Text ?? string.Empty).Trim();
        appName = string.IsNullOrWhiteSpace(ProgramNameBox.Text) ? DefaultProgramName(rootPath) : ProgramNameBox.Text.Trim();
        searchPath = (SearchPathBox.Text ?? string.Empty).Trim();
        programArgs = (ProgramArgsBox.Text ?? string.Empty).Trim();
        includePython = IncludePythonBox.IsChecked == true;

        if (string.IsNullOrWhiteSpace(cliPath) || !File.Exists(cliPath))
        {
            ShowError("Nie znaleziono pliku CLI. Wskaż InfoExeApp.exe.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            ShowError("Wskaż istniejący katalog do skanowania.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(searchPath))
        {
            searchPath = rootPath;
        }
        else
        {
            searchPath = Path.GetFullPath(searchPath);
            if (!File.Exists(searchPath) && !Directory.Exists(searchPath))
            {
                ShowError("Wskaż istniejącą ścieżkę do przeszukania.");
                return false;
            }

            if (!IsPathWithinRoot(rootPath, searchPath))
            {
                ShowError("Ścieżka do przeszukania musi być wewnątrz katalogu.");
                return false;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        return true;
    }

    private async Task RunCliAsync(IReadOnlyList<string> args, string cliPath, string dbPath, bool appendHeader)
    {
        DisableActions(true);
        try
        {
            AppendOutput(string.Empty);
            if (appendHeader)
            {
                AppendOutput($"> {BuildPreviewCommand(cliPath, args)}");
            }

            var psi = new ProcessStartInfo
            {
                FileName = cliPath,
                WorkingDirectory = Path.GetDirectoryName(dbPath)!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                throw new InvalidOperationException("Nie udało się uruchomić CLI.");
            }

            var stdout = PumpLinesAsync(process.StandardOutput, line => AppendOutput(line));
            var stderr = PumpLinesAsync(process.StandardError, line => AppendOutput(line, true));
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync());

            if (process.ExitCode == 0)
            {
                SetStatus("Skan zakończony.");
            }
            else
            {
                SetStatus($"CLI zakończyło się kodem {process.ExitCode}.");
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            DisableActions(false);
        }
    }

    private static async Task PumpLinesAsync(TextReader reader, Action<string> appendLine)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            appendLine(line);
        }
    }

    private string BuildPreviewCommand(string cliPath, IReadOnlyList<string> args)
    {
        var items = new List<string> { Quote(cliPath) };
        items.AddRange(args.Select(Quote));
        return string.Join(' ', items);
    }

    private static string Quote(string value)
    {
        return value.Contains(' ') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }

    private void UpdatePreview()
    {
        var cliPath = (CliPathBox.Text ?? string.Empty).Trim();
        var dbPath = string.IsNullOrWhiteSpace(DbPathBox.Text) ? ResolveDefaultDbPath() : DbPathBox.Text.Trim();
        var rootPath = (RootPathBox.Text ?? string.Empty).Trim();
        var appName = string.IsNullOrWhiteSpace(ProgramNameBox.Text) ? "<nazwa programu>" : ProgramNameBox.Text.Trim();
        var searchPath = string.IsNullOrWhiteSpace(SearchPathBox.Text) ? "<katalog>" : SearchPathBox.Text.Trim();
        var args = string.IsNullOrWhiteSpace(ProgramArgsBox.Text) ? null : ProgramArgsBox.Text.Trim();
        var python = IncludePythonBox.IsChecked == true ? " --python" : string.Empty;

        PreviewBox.Text = string.Join(' ', new[]
        {
            string.IsNullOrWhiteSpace(cliPath) ? "<InfoExeApp.exe>" : Quote(cliPath),
            "scan",
            "--root", Quote(string.IsNullOrWhiteSpace(rootPath) ? "<katalog>" : rootPath),
            "--app", Quote(appName),
            "--path", Quote(searchPath),
            args is null ? string.Empty : $"--args {Quote(args)}",
            string.IsNullOrWhiteSpace(dbPath) ? string.Empty : $"--db {Quote(dbPath)}",
            python
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private void AppendOutput(string line, bool error = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var current = OutputBox.Text ?? string.Empty;
            var prefix = error ? "[ERR] " : string.Empty;
            OutputBox.Text = current + prefix + line + Environment.NewLine;
            OutputBox.CaretIndex = OutputBox.Text.Length;
        });
    }

    private void SetStatus(string text)
    {
        Dispatcher.UIThread.Post(() => StatusText.Text = text);
    }

    private void ShowError(string message)
    {
        SetStatus(message);
        AppendOutput(message, true);
    }

    private void SetBusy(bool busy, string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            BusyBar.IsVisible = busy;
            RunButton.IsEnabled = !busy;
            HelpButton.IsEnabled = !busy;
            StatusText.Text = status;
        });
    }

    private void DisableActions(bool busy)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RunButton.IsEnabled = !busy;
            HelpButton.IsEnabled = !busy;
            BusyBar.IsVisible = busy;
        });
    }

    private static string ResolveDefaultDbPath()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InfoExe");
        return Path.Combine(root, "infoexe.db");
    }

    private static string ResolveDefaultCliPath()
    {
        var sameFolder = Path.Combine(AppContext.BaseDirectory, "InfoExeApp.exe");
        if (File.Exists(sameFolder))
        {
            return sameFolder;
        }

        var devLayout = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "InfoExeApp", "bin", "Debug", "net10.0", "InfoExeApp.exe"));
        return File.Exists(devLayout) ? devLayout : string.Empty;
    }

    private static string DefaultProgramName(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return string.Empty;
        }

        var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    private static bool IsPathWithinRoot(string rootPath, string candidatePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));

        if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private IStorageProvider? GetStorageProvider()
    {
        return TopLevel.GetTopLevel(this)?.StorageProvider;
    }

    private async void BrowseReportOutput_Click(object? sender, RoutedEventArgs e)
    {
        var storage = GetStorageProvider();
        if (storage is null)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Wybierz folder dla raportu",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            ReportOutputBox.Text = folders[0].Path.LocalPath;
        }
    }

    private async void GenerateReport_Click(object? sender, RoutedEventArgs e)
    {
        var selectedScan = ScanIdCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selectedScan) || selectedScan.StartsWith("("))
        {
            StatusText.Text = "Błąd: Najpierw wybierz skan";
            return;
        }

        var format = FormatHtml.IsChecked == true ? "html" : "md";
        var outputPath = ReportOutputBox.Text ?? "./reports";

        var args = new List<string>
        {
            "analyze",
            "--id", selectedScan,
            "--format", format
        };

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            args.Add("--output");
            args.Add(outputPath);
        }

        var cliPath = CliPathBox.Text ?? ResolveDefaultCliPath();
        var dbPath = DbPathBox.Text ?? ResolveDefaultDbPath();

        BusyBar.IsVisible = true;
        GenerateReportButton.IsEnabled = false;
        StatusText.Text = "Generowanie raportu...";
        OutputBox.Text = string.Empty;

        try
        {
            await RunCliAsync(args, cliPath, dbPath, true);
            StatusText.Text = "Raport wygenerowany pomyślnie!";

            var lines = OutputBox.Text?.Split('\n') ?? [];
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("analyze") && trimmed.Contains(':'))
                {
                    _lastReportPath = trimmed[(trimmed.IndexOf(':') + 1)..].Trim();
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(_lastReportPath))
            {
                _lastReportFormat = FormatHtml.IsChecked == true ? "html" : "md";
                ReportActionsPanel.IsVisible = true;
                OpenBrowserBtn.IsVisible = _lastReportFormat == "html";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Błąd: {ex.Message}";
            OutputBox.Text = ex.ToString();
        }
        finally
        {
            BusyBar.IsVisible = false;
            GenerateReportButton.IsEnabled = true;
        }
    }

    private void LoadScans()
    {
        try
        {
            var dbPath = DbPathBox.Text ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InfoExe", "infoexe.db");
            if (!File.Exists(dbPath))
            {
                return;
            }

            var scans = new System.Collections.Generic.List<string>();
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT scan_id FROM scan_jobs ORDER BY started_at_utc DESC LIMIT 50;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                scans.Add(reader.GetString(0));
            }

            if (scans.Count > 0)
            {
                foreach (var scan in scans)
                {
                    ScanIdCombo.Items?.Add(scan);
                }
                ScanIdCombo.SelectedIndex = 0;
            }
        }
        catch
        {
            // silently fail
        }
    }

    private void OpenReport_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastReportPath) && File.Exists(_lastReportPath))
        {
            var psi = new ProcessStartInfo { FileName = _lastReportPath, UseShellExecute = true };
            Process.Start(psi);
        }
    }

    private void OpenExplorer_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastReportPath))
        {
            var folder = Path.GetDirectoryName(_lastReportPath);
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                var psi = new ProcessStartInfo { FileName = "explorer.exe", Arguments = "/select,\"" + _lastReportPath + "\"", UseShellExecute = false };
                Process.Start(psi);
            }
        }
    }

    private void OpenBrowser_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastReportPath) && File.Exists(_lastReportPath))
        {
            var psi = new ProcessStartInfo { FileName = _lastReportPath, UseShellExecute = true };
            Process.Start(psi);
        }
    }
}

