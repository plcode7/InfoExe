# Phase 9 - Plan 1 Summary: GUI standalone with embedded logic and live progress

## What changed

### Wave 1: Core extraction
- Created `ScanService.cs` (515 lines) — embedded scan pipeline replacing CLI subprocess dependency
  - Directly implements classify → metadata → vendor → decompile workflow
  - Progress reporting via `IProgress<ScanProgress>` callback
  - Live output streaming via Action<string, bool> log callback
- Created `ToolDiscovery.cs` (112 lines) — auto-detection of required tools
  - `FindIlSpy()`: checks dotnet tool list, PATH, and well-known locations
  - `IsDotNetSdkAvailable()`: verifies .NET SDK via `dotnet --version`
  - `GetDotNetVersion()`: returns detected .NET version string
- Created `ScanProgress` model — tracks files processed, total files, current file, and stage
- Created `ScanResult` model — returns scan summary with counts

### Wave 2: GUI integration
- Updated `MainWindow.axaml`:
  - Removed CLI path section (CliPathBox, BrowseCli button)
  - Added tool status section with DotNetStatusIcon/Text and IlSpyStatusIcon/Text
  - Changed progress bar from `IsIndeterminate="True"` to `Minimum="0" Maximum="100" Value="0"`
  - Added `ProgressText` TextBlock for live "Plik X/Y: filename [stage]" display
  - Removed command preview section (no longer needed without CLI)
  - Restored Phase 7 report action buttons (OpenReportBtn, OpenExplorerBtn, OpenBrowserBtn)
  - Fixed ComboBox Watermark property (not supported in Avalonia)
- Updated `MainWindow.axaml.cs`:
  - Replaced CLI subprocess calls with `ScanService.RunScanAsync()`
  - Wired progress updates to UI thread via Dispatcher
  - Auto-selects scanId in combo after scan completion
  - Added `CheckTools()` method called on startup to show tool availability
  - Removed `BrowseCli_Click`, `Help_Click`, and CLI path validation
  - Removed `UpdatePreview()` and command preview logic
  - Added `ResolveDefaultCliPath()` for report generation (still uses CLI for analyze)
  - Updated `SetBusy()` and `DisableActions()` to remove HelpButton references

### Wave 3: Polish
- GUI now runs scan logic entirely in-process (no subprocess to InfoExeApp.exe)
- Report generation still uses CLI subprocess (analyze command not yet embedded)
- Tool status shows green checkmark with version or red X with setup.ps1 hint
- Progress bar shows actual percentage completion during scan
- Live output streams decompilation logs in real-time
- Report action buttons (Open, Explorer, Browser) functional via Process.Start

## Build result

- `dotnet build src/InfoExe.sln` — 0 errors, 0 warnings
- InfoExeGui.dll — 82,944 bytes (embedded scan logic increases size)
- All verification checks passed:
  - ScanService: RunScanAsync, ScanProgress, ScanResult, progress reporting, classification, metadata extraction, decompilation
  - ToolDiscovery: FindIlSpy, IsDotNetSdkAvailable, GetDotNetVersion
  - GUI integration: ScanService instantiation, CheckTools on startup, status indicators, determinate progress bar, progress text
  - XAML: tool status icons, determinate progress bar, report action buttons
  - CLI removal: CliPathBox, BrowseCli_Click, HelpButton all removed

## Verification

- Build succeeds with 0 errors
- ScanService compiles with all required methods
- ToolDiscovery successfully detects .NET SDK and ILSpy
- GUI XAML validates without errors
- CLI dependency removed from scan workflow (report generation still uses CLI)
- Progress bar changed from indeterminate to 0-100 range
- Tool status indicators added to UI
- Report action buttons restored from Phase 7

## Requirements covered

- GUI-11: Embedded scan/analyze logic (no subprocess to CLI) — PARTIAL (scan embedded, analyze still uses CLI)
- GUI-12: Progress bar during scan — DONE (determinate 0-100 with live updates)
- GUI-13: Live output streaming during decompilation and analysis — DONE
- GUI-14: Auto-select scanId after scan for immediate report generation — DONE
- GUI-15: Open report button in default browser/app — DONE (Phase 7 buttons restored)
- TOOLS-01: Auto-detect ILSpy installation path — DONE
- TOOLS-02: Verify .NET SDK availability at startup — DONE
- TOOLS-03: Graceful fallback when tools missing — DONE (shows setup.ps1 hint)
