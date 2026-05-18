using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;
using InfoExeCore;

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
            // BUG-02: Safe access to UI controls
            if (DbPathBox is not null)
                DbPathBox.Text = ResolveDefaultDbPath();
            LoadScans();
            CheckTools();
        };
    }

    private async void BrowseDb_Click(object? sender, RoutedEventArgs e)
    {
        var storage = GetStorageProvider();
        if (storage is null) return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Wybierz folder bazy danych", AllowMultiple = false });
        if (folders.Count > 0 && DbPathBox is not null)
            DbPathBox.Text = Path.Combine(folders[0].Path.LocalPath, "infoexe.db");
    }

    private async void BrowseRoot_Click(object? sender, RoutedEventArgs e)
    {
        var storage = GetStorageProvider();
        if (storage is null) return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Wybierz katalog do skanowania", AllowMultiple = false });
        if (folders.Count > 0 && RootPathBox is not null)
            RootPathBox.Text = folders[0].Path.LocalPath;
    }

    private async void BrowseSearch_Click(object? sender, RoutedEventArgs e)
    {
        var storage = GetStorageProvider();
        if (storage is null) return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Wybierz katalog do przeszukania", AllowMultiple = false });
        if (folders.Count > 0 && SearchPathBox is not null)
            SearchPathBox.Text = folders[0].Path.LocalPath;
    }

    private async void RunScan_Click(object? sender, RoutedEventArgs e)
    {
        // BUG-02: Safe null checks on all UI controls
        var rootPath = (RootPathBox?.Text ?? string.Empty).Trim();
        var dbPath = string.IsNullOrWhiteSpace(DbPathBox?.Text)
            ? ResolveDefaultDbPath()
            : Path.GetFullPath(DbPathBox!.Text.Trim());
        var programName = (ProgramNameBox?.Text ?? string.Empty).Trim();
        var searchPath = (SearchPathBox?.Text ?? string.Empty).Trim();
        var programArgs = (ProgramArgsBox?.Text ?? string.Empty).Trim();
        var includePython = IncludePythonBox?.IsChecked == true;

        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            ShowError("Wskaz istniejacy katalog do skanowania.");
            return;
        }

        if (string.IsNullOrWhiteSpace(searchPath))
            searchPath = rootPath;

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        SetBusy(true, "Skanuje...");
        if (OutputBox is not null) OutputBox.Text = string.Empty;

        var scanService = new ScanService((msg, isErr) => AppendOutput(msg, isErr));

        // BUG-01: Use InvokeAsync for ordered progress updates
        var progress = new Progress<ScanProgress>(p =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (p.TotalFiles <= 0) return;

                if (BusyBar is not null)
                    BusyBar.Value = (double)p.FilesProcessed / p.TotalFiles * 100;

                if (ProgressText is not null)
                {
                    ProgressText.Text = $"Plik {p.FilesProcessed}/{p.TotalFiles}: {Path.GetFileName(p.CurrentFile)} [{p.Stage}]";
                    ProgressText.IsVisible = true;
                }
            });
        });

        try
        {
            var result = await scanService.RunScanAsync(rootPath, dbPath, programName, searchPath, programArgs, includePython, progress);
            if (StatusText is not null) StatusText.Text = $"Skan zakonczony: {result.ScanId}";
            LoadScans();
            if (ScanIdCombo is not null) ScanIdCombo.SelectedItem = result.ScanId;
        }
        catch (Exception ex)
        {
            ShowError($"Blad skanu: {ex.Message}");
        }
        finally
        {
            SetBusy(false, "Gotowe");
        }
    }

    private async Task RunCliAsync(IReadOnlyList<string> args, string cliPath, string dbPath, bool appendHeader)
    {
        DisableActions(true);
        try
        {
            AppendOutput(string.Empty);
            if (appendHeader) AppendOutput($"> {BuildPreviewCommand(cliPath, args)}");

            var psi = new ProcessStartInfo
            {
                FileName = cliPath,
                WorkingDirectory = Path.GetDirectoryName(dbPath)!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);

            using var process = new Process { StartInfo = psi };
            if (!process.Start()) throw new InvalidOperationException("Nie udało się uruchomić CLI.");

            var stdout = PumpLinesAsync(process.StandardOutput, line => AppendOutput(line));
            var stderr = PumpLinesAsync(process.StandardError, line => AppendOutput(line, true));
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync());

            SetStatus(process.ExitCode == 0 ? "Skan zakończony." : $"CLI zakończyło się kodem {process.ExitCode}.");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { DisableActions(false); }
    }

    private static async Task PumpLinesAsync(TextReader reader, Action<string> appendLine)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line is null) break;
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
        => value.Contains(' ') || value.Contains('"') ? $"\"{value.Replace("\"", "\\\"")}\"" : value;

    private void AppendOutput(string line, bool error = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (OutputBox is null) return;
            var current = OutputBox.Text ?? string.Empty;
            var prefix = error ? "[ERR] " : string.Empty;
            OutputBox.Text = current + prefix + line + Environment.NewLine;
            OutputBox.CaretIndex = OutputBox.Text.Length;
        });
    }

    private void SetStatus(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (StatusText is not null) StatusText.Text = text;
        });
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
            if (BusyBar is not null) BusyBar.IsVisible = busy;
            if (RunButton is not null) RunButton.IsEnabled = !busy;
            if (StatusText is not null) StatusText.Text = status;
        });
    }

    private void DisableActions(bool busy)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (RunButton is not null) RunButton.IsEnabled = !busy;
            if (BusyBar is not null) BusyBar.IsVisible = busy;
        });
    }

    private static string ResolveDefaultDbPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Constants.DefaultAppDataFolder, Constants.DefaultDbFileName);

    private static string ResolveDefaultCliPath()
    {
        var appDir = AppContext.BaseDirectory;
        var cliPath = Path.Combine(appDir, "InfoExeApp.exe");
        if (File.Exists(cliPath)) return cliPath;

        var relativeCliPath = Path.Combine(appDir, "..", "..", "..", "..", "InfoExeApp", "bin", "Debug", "net10.0", "InfoExeApp.exe");
        return File.Exists(relativeCliPath) ? Path.GetFullPath(relativeCliPath) : string.Empty;
    }

    private bool IsPathWithinRoot(string rootPath, string candidatePath)
        => PathValidator.IsPathWithinRoot(rootPath, candidatePath);

    private IStorageProvider? GetStorageProvider()
        => TopLevel.GetTopLevel(this)?.StorageProvider;

    private async void BrowseReportOutput_Click(object? sender, RoutedEventArgs e)
    {
        var storage = GetStorageProvider();
        if (storage is null) return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Wybierz folder dla raportu", AllowMultiple = false });
        if (folders.Count > 0 && ReportOutputBox is not null)
            ReportOutputBox.Text = folders[0].Path.LocalPath;
    }

    private async void GenerateReport_Click(object? sender, RoutedEventArgs e)
    {
        var selectedScan = ScanIdCombo?.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selectedScan) || selectedScan.StartsWith("("))
        {
            if (StatusText is not null) StatusText.Text = "Błąd: Najpierw wybierz skan";
            return;
        }

        var format = FormatHtml?.IsChecked == true ? "html" : "md";
        var outputPath = ReportOutputBox?.Text ?? "./reports";
        var args = new List<string> { "analyze", "--id", selectedScan, "--format", format };
        if (!string.IsNullOrWhiteSpace(outputPath)) { args.Add("--output"); args.Add(outputPath); }

        var dbPath = DbPathBox?.Text ?? ResolveDefaultDbPath();
        var cliPath = ResolveDefaultCliPath();
        if (string.IsNullOrWhiteSpace(cliPath) || !File.Exists(cliPath))
        {
            ShowError("Nie znaleziono InfoExeApp.exe. Uruchom build projektu.");
            return;
        }

        if (BusyBar is not null) BusyBar.IsVisible = true;
        if (GenerateReportButton is not null) GenerateReportButton.IsEnabled = false;
        if (StatusText is not null) StatusText.Text = "Generowanie raportu...";
        if (OutputBox is not null) OutputBox.Text = string.Empty;

        try
        {
            await RunCliAsync(args, cliPath, dbPath, true);
            if (StatusText is not null) StatusText.Text = "Raport wygenerowany pomyślnie!";

            var lines = OutputBox?.Text?.Split('\n') ?? [];
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
                _lastReportFormat = FormatHtml?.IsChecked == true ? "html" : "md";
                if (ReportActionsPanel is not null) ReportActionsPanel.IsVisible = true;
                if (OpenBrowserBtn is not null) OpenBrowserBtn.IsVisible = _lastReportFormat == "html";
            }
        }
        catch (Exception ex)
        {
            if (StatusText is not null) StatusText.Text = $"Błąd: {ex.Message}";
            if (OutputBox is not null) OutputBox.Text = ex.ToString();
        }
        finally
        {
            if (BusyBar is not null) BusyBar.IsVisible = false;
            if (GenerateReportButton is not null) GenerateReportButton.IsEnabled = true;
        }
    }

    private void LoadScans()
    {
        try
        {
            var dbPath = DbPathBox?.Text ?? ResolveDefaultDbPath();
            if (!File.Exists(dbPath)) return;

            var scans = new List<string>();
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT scan_id FROM scan_jobs ORDER BY started_at_utc DESC LIMIT 50;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) scans.Add(reader.GetString(0));

            if (ScanIdCombo is not null)
            {
                // BUG-06: Clear before reloading to avoid duplicates
                ScanIdCombo.Items?.Clear();
                foreach (var scan in scans)
                    ScanIdCombo.Items?.Add(scan);
                if (scans.Count > 0)
                    ScanIdCombo.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            // SEC-04: Log error instead of silently swallowing
            Debug.WriteLine($"LoadScans failed: {ex.Message}");
        }
    }

    private void OpenReport_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastReportPath) && File.Exists(_lastReportPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = _lastReportPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowError($"Nie można otworzyć raportu: {ex.Message}");
            }
        }
    }

    private void OpenBrowser_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastReportPath) && File.Exists(_lastReportPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = _lastReportPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowError($"Nie można otworzyć przeglądarki: {ex.Message}");
            }
        }
    }

    private void CheckTools()
    {
        try
        {
            var ilspy = ToolDiscovery.FindIlSpy();
            var dotnetVersion = ToolDiscovery.GetDotNetVersion();
            var statusParts = new List<string>();

            if (ilspy is not null)
                statusParts.Add("ILSpy: OK");
            else
                statusParts.Add("ILSpy: brak (dekompilacja niedostępna)");

            if (dotnetVersion is not null)
                statusParts.Add($".NET: {dotnetVersion}");
            else
                statusParts.Add(".NET: brak");

            SetStatus(string.Join(" | ", statusParts));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CheckTools failed: {ex.Message}");
        }
    }

    private void OpenExplorer_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastReportPath))
        {
            try
            {
                var dir = Path.GetDirectoryName(_lastReportPath);
                if (dir is not null && Directory.Exists(dir))
                    Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowError($"Nie można otworzyć Explorera: {ex.Message}");
            }
        }
    }
}
