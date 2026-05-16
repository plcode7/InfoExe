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

            DbPathBox.Text = ResolveDefaultDbPath();

            

            LoadScans();

            CheckTools();

        };

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

            

        }

    }

    private void TextFieldChanged(object? sender, TextChangedEventArgs e)

    {

        

    }

    private void OptionChanged(object? sender, RoutedEventArgs e)

    {

        

    }

    private async void RunScan_Click(object? sender, RoutedEventArgs e)

    {

        var rootPath = (RootPathBox.Text ?? string.Empty).Trim();

        var dbPath = string.IsNullOrWhiteSpace(DbPathBox.Text) ? ResolveDefaultDbPath() : Path.GetFullPath(DbPathBox.Text.Trim());

        var programName = (ProgramNameBox.Text ?? string.Empty).Trim();

        var searchPath = (SearchPathBox.Text ?? string.Empty).Trim();

        var programArgs = (ProgramArgsBox.Text ?? string.Empty).Trim();

        var includePython = IncludePythonBox.IsChecked == true;

        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))

        {

            ShowError("Wskaz istniejacy katalog do skanowania.");

            return;

        }

        if (string.IsNullOrWhiteSpace(searchPath))

            searchPath = rootPath;

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        SetBusy(true, "Skanuje...");

        OutputBox.Text = string.Empty;

        var scanService = new ScanService((msg, isErr) => AppendOutput(msg, isErr));

        var progress = new Progress<ScanProgress>(p =>

        {

            Dispatcher.UIThread.Post(() =>

            {

                if (p.TotalFiles > 0)

                {

                    BusyBar.Value = (double)p.FilesProcessed / p.TotalFiles * 100;

                    ProgressText.Text = $"Plik {p.FilesProcessed}/{p.TotalFiles}: {Path.GetFileName(p.CurrentFile)} [{p.Stage}]";

                    ProgressText.IsVisible = true;

                }

            });

        });

        try

        {

            var result = await scanService.RunScanAsync(rootPath, dbPath, programName, searchPath, programArgs, includePython, progress);

            StatusText.Text = $"Skan zakonczony: {result.ScanId}";

            LoadScans();

            ScanIdCombo.SelectedItem = result.ScanId;

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


            StatusText.Text = status;

        });

    }

    private void DisableActions(bool busy)

    {

        Dispatcher.UIThread.Post(() =>

        {

            RunButton.IsEnabled = !busy;


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

        var appDir = AppContext.BaseDirectory;

        var cliPath = Path.Combine(appDir, "InfoExeApp.exe");

        if (File.Exists(cliPath))

            return cliPath;



        // Try relative path to CLI build directory

        var relativeCliPath = Path.Combine(appDir, "..", "..", "..", "..", "InfoExeApp", "bin", "Debug", "net10.0", "InfoExeApp.exe");

        if (File.Exists(relativeCliPath))

            return Path.GetFullPath(relativeCliPath);



        return string.Empty;

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


        var dbPath = DbPathBox.Text ?? ResolveDefaultDbPath();

        var cliPath = ResolveDefaultCliPath();
        if (string.IsNullOrWhiteSpace(cliPath) || !File.Exists(cliPath))
        {
            ShowError("Nie znaleziono InfoExeApp.exe. Uruchom build projektu.");
            return;
        }

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

    private void CheckTools()

    {

        Dispatcher.UIThread.Post(() =>

        {

            var dotnetOk = ToolDiscovery.IsDotNetSdkAvailable();

            var dotnetVer = ToolDiscovery.GetDotNetVersion();

            DotNetStatusIcon.Text = dotnetOk ? "✓" : "✗";

            DotNetStatusText.Text = dotnetOk ? $".NET SDK {dotnetVer}" : ".NET SDK: brak — uruchom setup.ps1";

            var ilspyPath = ToolDiscovery.FindIlSpy();

            var ilspyOk = !string.IsNullOrWhiteSpace(ilspyPath);

            IlSpyStatusIcon.Text = ilspyOk ? "✓" : "✗";

            IlSpyStatusText.Text = ilspyOk ? "ILSpy: OK" : "ILSpy: brak — uruchom setup.ps1";

        });

    }

}