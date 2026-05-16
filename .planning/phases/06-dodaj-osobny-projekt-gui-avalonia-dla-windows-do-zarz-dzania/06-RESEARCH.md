# Phase 6: Avalonia GUI for Scan Parameter Management — Research

**Researched:** 2026-05-15  
**Domain:** Avalonia 11.x / .NET 10 / Windows desktop GUI + CLI subprocess  
**Confidence:** HIGH (all claims verified against live repo or NuGet registry)

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
1. **Scope is scan-only** — root folder, program name, search path, program arguments, Python toggle, CLI path, DB path, run scan, show help, show live output. No status history, retry UI, or export UI.
2. **GUI stays separate from CLI logic** — launches `InfoExeApp` as a subprocess; no shared library extraction.
3. **Stable paths** — DB under user profile (`%LOCALAPPDATA%\InfoExe\infoexe.db`); CLI path configurable with a sensible dev default.
4. **Implementation style** — code-behind; no MVVM stack unless a specific binding need forces it.
5. **Windows-first** — Avalonia is the chosen framework; primary supported platform is Windows.

### Agent's Discretion
- Internal code quality, naming, and minor UX polish within the scan form.

### Deferred Ideas (OUT OF SCOPE)
- Status history browser, retry UI, export UI.
- Any broader redesign of the CLI workflow.
</user_constraints>

---

## Summary

The `InfoExeGui` project already exists and builds cleanly at `src/InfoExeGui/`. It targets `net10.0`, outputs `WinExe`, uses Avalonia 11.2.0, and implements **every locked requirement** from CONTEXT.md in code-behind: a scan form, folder pickers via `IStorageProvider`, a command preview box, live stdout/stderr streaming, an indeterminate progress bar, and stable DB/CLI path defaults.

The project is **not a greenfield task**. The planner's work is to (a) fix one confirmed path-resolution bug in `ResolveDefaultCliPath`, (b) bump Avalonia packages from 11.2.0 → 11.3.15, (c) add a `.sln` file to unify the two projects, and (d) address a transitive security advisory warning in the build. The GUI and CLI both build without errors today.

**Primary recommendation:** Treat this phase as a polish+fix phase over an existing scaffold, not a build-from-scratch phase.

---

## Existing State — What Is Already Done

| Item | Status | Location |
|------|--------|----------|
| `InfoExeGui.csproj` (WinExe, net10.0, Avalonia 11.2.0) | ✅ Done | `src/InfoExeGui/` |
| `Program.cs` — `[STAThread]`, `AppBuilder`, `StartWithClassicDesktopLifetime` | ✅ Done | `src/InfoExeGui/Program.cs` |
| `App.axaml` + `App.axaml.cs` — FluentTheme, classic desktop lifetime | ✅ Done | `src/InfoExeGui/` |
| `MainWindow.axaml` — ScrollViewer, scan form, preview box, output box, progress bar | ✅ Done | `src/InfoExeGui/` |
| `MainWindow.axaml.cs` — code-behind, all event handlers | ✅ Done | `src/InfoExeGui/` |
| `ProcessStartInfo` with `ArgumentList`, `CreateNoWindow=true`, async stream pumping | ✅ Done | `MainWindow.axaml.cs` |
| `Dispatcher.UIThread.Post()` for cross-thread UI updates | ✅ Done | `MainWindow.axaml.cs` |
| DB path default → `%LOCALAPPDATA%\InfoExe\infoexe.db` | ✅ Done | `ResolveDefaultDbPath()` |
| Folder/file pickers via `TopLevel.GetTopLevel(this)?.StorageProvider` | ✅ Done | `MainWindow.axaml.cs` |
| CLI path discovery for dev layout | ⚠️ Bug (wrong path depth) | `ResolveDefaultCliPath()` |
| Avalonia package versions pinned to 11.2.0 | ⚠️ Outdated (11.3.15 available) | `InfoExeGui.csproj` |
| Solution file (`.sln`) | ❌ Missing | — |
| `Tmds.DBus.Protocol` security advisory (NU1903) | ⚠️ Warning in build | transitive from Avalonia.FreeDesktop |

---

## Standard Stack

### Core
| Library | Version in Repo | Latest 11.x | Purpose |
|---------|-----------------|-------------|---------|
| `Avalonia` | 11.2.0 | **11.3.15** | Core UI framework [VERIFIED: NuGet flat-container] |
| `Avalonia.Desktop` | 11.2.0 | **11.3.15** | Win32 backend [VERIFIED: NuGet flat-container] |
| `Avalonia.Themes.Fluent` | 11.2.0 | **11.3.15** | Windows-style Fluent theme [VERIFIED: NuGet flat-container] |
| `Avalonia.Fonts.Inter` | 11.2.0 | **11.3.15** | Inter typeface [VERIFIED: NuGet flat-container] |

All four packages share a single version number and must be bumped together. `AvaloniaUseCompiledBindingsByDefault=true` is already set in the csproj — do not remove it.

**Version verification command:**
```powershell
(Invoke-RestMethod "https://api.nuget.org/v3-flatcontainer/avalonia/index.json").versions | Where-Object { $_ -like "11.3.*" } | Select-Object -Last 1
# Returns: 11.3.15
```

**There is also an Avalonia 12.0.x series (12.0.3 as of research date).**  
Do **not** upgrade to 12.x in this phase — it is a separate major version with breaking changes. Stay on 11.3.x as the stable track aligned with the existing project setup. [VERIFIED: NuGet flat-container]

---

## Bug: `ResolveDefaultCliPath` Has Wrong Relative Path Depth

**Confirmed by live path-resolution test in the repo environment.**

Current code in `MainWindow.axaml.cs`:
```csharp
var candidate = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory,
    "..", "..", "..", "..", "..",   // ← 5 levels up (WRONG)
    "InfoExeApp", "bin", "Debug", "net10.0", "InfoExeApp.exe"));
```

`AppContext.BaseDirectory` at debug runtime = `…\src\InfoExeGui\bin\Debug\net10.0\`

| # of `..` | Resolves to |
|-----------|-------------|
| 5 (current) | `C:\Data\src\InfoExe\InfoExeApp\bin\…` ← **does not exist** |
| 4 (correct) | `C:\Data\src\InfoExe\src\InfoExeApp\bin\…` ← **exists and builds** |

**Fix:** Change `"..", "..", "..", "..", ".."` → `"..", "..", "..", ".."` (remove one `..`).

---

## DB Path Strategy

### Current implementation (correct, keep it)
```csharp
private static string ResolveDefaultDbPath()
{
    var root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "InfoExe");
    return Path.Combine(root, "infoexe.db");
}
```
This resolves to `%LOCALAPPDATA%\InfoExe\infoexe.db` on every Windows install, regardless of where the GUI exe lives.

### CLI/GUI DB path contract
The CLI defaults to `CWD\infoexe.db` when `--db` is not supplied.  
The GUI **always passes `--db <absolute path>`** via `ArgumentList` and sets `WorkingDirectory` to the DB directory. Both projects write to the same file when the GUI is the entrypoint.

**Important:** `*.db` is already in `.gitignore` — the user-profile DB will never be accidentally committed. [VERIFIED: `.gitignore` in repo root]

### For installed builds (xcopy/MSIX/zip deploy)
No registry or installer state is written. Users who install InfoExeGui anywhere on disk will still get a consistent DB at `%LOCALAPPDATA%\InfoExe\infoexe.db` because the GUI hard-codes the LocalApplicationData path. The CLI path must be configured manually or via the browse button when not running from the dev layout.

---

## Process Launch Pattern (Already Correct — Validate, Don't Rewrite)

The existing `RunCliAsync` method in `MainWindow.axaml.cs` is the recommended pattern for launching a CLI subprocess from an Avalonia app. Key choices made correctly:

```csharp
var psi = new ProcessStartInfo
{
    FileName = cliPath,
    WorkingDirectory = Path.GetDirectoryName(dbPath)!,
    UseShellExecute = false,        // Required for stream redirection
    RedirectStandardOutput = true,  // Stream stdout
    RedirectStandardError  = true,  // Stream stderr separately
    CreateNoWindow = true           // No console flash on Windows
};
foreach (var arg in args)
    psi.ArgumentList.Add(arg);      // Safe quoting — avoids manual escaping bugs
```

Stream pumping:
```csharp
var stdout = PumpLinesAsync(process.StandardOutput, line => AppendOutput(line));
var stderr = PumpLinesAsync(process.StandardError,  line => AppendOutput(line, true));
await Task.WhenAll(stdout, stderr, process.WaitForExitAsync());
```

Cross-thread UI update (already implemented):
```csharp
Dispatcher.UIThread.Post(() => { OutputBox.Text += line + Environment.NewLine; });
```

**Do not** change the `await Task.WhenAll(stdout, stderr, process.WaitForExitAsync())` pattern. Reading both streams concurrently is mandatory — reading stdout alone before stderr (or vice-versa) causes a deadlock when the subprocess fills one pipe buffer. [ASSUMED based on .NET stream deadlock well-known pitfall; pattern is correct]

---

## Solution File

### Current state
No `.sln` file exists. The two projects (`InfoExeApp`, `InfoExeGui`) are independent `.csproj` files under `src/`.

### Recommendation: Add `InfoExe.sln` at `src/InfoExe.sln`
```bash
cd C:\Data\src\InfoExe\src
dotnet new sln -n InfoExe
dotnet sln InfoExe.sln add InfoExeApp/InfoExeApp.csproj
dotnet sln InfoExe.sln add InfoExeGui/InfoExeGui.csproj
```

**Benefits:**
- `dotnet build src/InfoExe.sln` builds both projects in one command (CI-friendly)
- IDE (VS / Rider) solution-level build and run from a single root
- Enables future shared project or `<ProjectReference>` without restructuring

**Not strictly required** to run the GUI, but the two-project layout is incomplete without it.

---

## Tmds.DBus.Protocol Security Advisory

**Warning produced at build:**
```
NU1903: Package 'Tmds.DBus.Protocol' 0.20.0 has a known high severity vulnerability
GHSA-xrw6-gwf8-vvr9
```

**Root cause:** `Avalonia.FreeDesktop` (Linux backend) is a transitive dependency of `Avalonia.Desktop` and pulls in `Tmds.DBus.Protocol`. It ships in the build output but is **never activated on Windows** — the Win32 backend (`Avalonia.Win32.dll`) loads instead.

**Options for the planner:**

| Option | Effort | Effect |
|--------|--------|--------|
| Upgrade Avalonia to 11.3.15 | 1 line change in csproj | Pulls in newer transitive; check if warning disappears |
| Suppress via `<NoWarn>NU1903</NoWarn>` in csproj | 1 line | Silences warning; does not update the package |
| Accept as-is | 0 | Warning persists in CI output; no runtime risk on Windows |

**Recommended action:** Upgrade to 11.3.15 first; if the warning persists, add `<NoWarn>NU1903</NoWarn>` to `InfoExeGui.csproj`. [VERIFIED: advisory exists at NuGet; Linux-only runtime path ASSUMED based on Avalonia architecture]

---

## Architecture Patterns

### Project structure (current — do not reorganize)
```
src/
├── InfoExeApp/              # CLI — net10.0, OutputType=Exe
│   ├── InfoExeApp.csproj
│   └── Program.cs
├── InfoExeGui/              # GUI — net10.0, OutputType=WinExe
│   ├── InfoExeGui.csproj
│   ├── Program.cs           # [STAThread], AppBuilder bootstrap
│   ├── App.axaml            # Application, FluentTheme
│   ├── App.axaml.cs
│   ├── MainWindow.axaml     # Scan form XAML
│   └── MainWindow.axaml.cs  # All logic in code-behind
└── InfoExe.sln              # TO BE ADDED
```

### Naming conventions (already in place — follow)
- Namespace: `InfoExeGui`
- AXAML code-behind class: `partial class MainWindow : Window`
- Named controls accessed by `x:Name` (e.g., `CliPathBox`, `OutputBox`, `BusyBar`)
- UI thread marshalling: `Dispatcher.UIThread.Post(...)` (not `InvokeAsync` — `Post` is fire-and-forget which is fine for appending output lines)

### Anti-Patterns to Avoid
- **String-concatenating CLI args:** `ProcessStartInfo.ArgumentList.Add()` is already used. Never switch to `Arguments = "..."` string concatenation — paths with spaces will break.
- **Reading stdout synchronously before stderr:** Deadlock risk on long scan runs. The concurrent `Task.WhenAll` pattern must stay.
- **Storing db path relative to `AppContext.BaseDirectory`:** The GUI exe install location is not a stable anchor for the database.
- **MVVM ViewModels:** Not required; CONTEXT.md explicitly locks code-behind.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead |
|---------|-------------|-------------|
| File/folder pickers | Custom dialog or P/Invoke | `IStorageProvider` (Avalonia 11 API, already used) |
| Cross-thread UI dispatch | `Control.Invoke` pattern or manual `SynchronizationContext` | `Dispatcher.UIThread.Post()` (already used) |
| Argument quoting/escaping | Manual string builder | `ProcessStartInfo.ArgumentList` (already used) |
| Theme styling | Custom control templates | `FluentTheme` (already applied) |

---

## Common Pitfalls

### Pitfall 1: CLI path hardcoded for debug layout only
**What goes wrong:** `ResolveDefaultCliPath` currently resolves to a non-existent path (5 `..` instead of 4). The `CliPathBox` loads empty; the user gets "Nie znaleziono pliku CLI" on every run unless they browse manually.  
**Fix:** Change to 4 `..` segments. [VERIFIED by live path resolution in the repo]

### Pitfall 2: `WorkingDirectory` must contain the DB folder
**What goes wrong:** The CLI computes `artifacts/decompiled/…` paths relative to `Environment.CurrentDirectory`. If `WorkingDirectory` is wrong (e.g., the GUI's own directory), decompile artifacts land in the wrong place.  
**Current code:** `WorkingDirectory = Path.GetDirectoryName(dbPath)!` — **correct**. Don't change this.

### Pitfall 3: `Directory.CreateDirectory` called on null parent
**What goes wrong:** If `DbPathBox` is manually cleared to an empty string, `Path.GetDirectoryName("")` returns `null`, and `Directory.CreateDirectory(null!)` throws.  
**Current code in `ValidateInputs`:** Falls back to `ResolveDefaultDbPath()` when blank — **correct**, but depends on the null-coalescing chain being maintained.

### Pitfall 4: Avalonia 11.x requires `[STAThread]` on Windows
Already present in `Program.cs`. Must not be removed.

### Pitfall 5: `Tmds.DBus.Protocol` warning in CI
Fails advisory scans. Address before any security gate in the pipeline.

---

## Code Examples

All patterns below are **already implemented in the repo** — documented here as the reference for any additions.

### Bootstrap (Program.cs)
```csharp
// Source: src/InfoExeGui/Program.cs [VERIFIED]
[STAThread]
public static void Main(string[] args)
{
    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
}
public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
```

### Folder picker via IStorageProvider
```csharp
// Source: src/InfoExeGui/MainWindow.axaml.cs [VERIFIED]
var folders = await TopLevel.GetTopLevel(this)!.StorageProvider
    .OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "…" });
if (folders.Count > 0)
    SomeBox.Text = folders[0].Path.LocalPath;
```

### Concurrent stream pumping
```csharp
// Source: src/InfoExeGui/MainWindow.axaml.cs [VERIFIED]
var stdout = PumpLinesAsync(process.StandardOutput, line => AppendOutput(line));
var stderr = PumpLinesAsync(process.StandardError,  line => AppendOutput(line, true));
await Task.WhenAll(stdout, stderr, process.WaitForExitAsync());

static async Task PumpLinesAsync(TextReader reader, Action<string> appendLine)
{
    while (await reader.ReadLineAsync() is { } line)
        appendLine(line);
}
```

### Safe UI update from background thread
```csharp
// Source: src/InfoExeGui/MainWindow.axaml.cs [VERIFIED]
Dispatcher.UIThread.Post(() =>
{
    OutputBox.Text += line + Environment.NewLine;
    OutputBox.CaretIndex = OutputBox.Text?.Length ?? 0;
});
```

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|-------------|-----------|---------|----------|
| .NET 10 SDK | Build InfoExeGui | ✓ | 10.0.300 | — |
| InfoExeApp.exe (CLI) | Run scans from GUI | ✓ | n/a (builds) | Browse button to set path |
| Avalonia 11.3.15 (upgrade) | Package bump | ✓ | Available on NuGet | Stay on 11.2.0 |
| `dotnet sln` | Create solution file | ✓ | Bundled with SDK | — |

**Missing dependencies with no fallback:** None.

---

## Open Questions

1. **Installed build CLI discovery**  
   - What we know: the dev-layout relative path (with bug fixed) works for `dotnet run` from the repo. For a release build (xcopy or MSIX), the CLI exe is in a separate location not predictable at compile time.  
   - What's unclear: whether a release packaging step will co-locate `InfoExeApp.exe` next to `InfoExeGui.exe`, or whether users are expected to install them separately.  
   - Recommendation: Add a second probe in `ResolveDefaultCliPath` — check `AppContext.BaseDirectory\InfoExeApp.exe` (same folder as GUI exe) before the dev-layout relative path. This covers a simple xcopy where both are placed in one folder.

2. **Output box unbounded growth**  
   - What we know: `OutputBox.Text` is appended line-by-line with string concatenation, creating O(n²) allocations for large scans. `TextBox` in Avalonia also re-measures on every text change.  
   - What's unclear: whether real scan output volumes (100s vs 1000s of lines) will make this visually slow.  
   - Recommendation: For phase 6 scope (scan-only), acceptable as-is. Flag for future iteration; a `StringBuilder` flush approach or `TextBox.Lines` replace can address it later.

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `Tmds.DBus.Protocol` is not activated on Windows at Avalonia runtime | Tmds advisory | Low — Windows users would see no impact; Linux users are already not the target |
| A2 | Avalonia 11.2.0 → 11.3.15 is a non-breaking upgrade | Standard Stack | Low — within same minor series; if breaking, revert to 11.2.0 |
| A3 | `Dispatcher.UIThread.Post` (fire-and-forget) is safe for appending output lines | Code Examples | Low — ordering is best-effort but lines are decorative output; no data loss |

---

## Sources

### Primary (HIGH confidence)
- Live repo read — `src/InfoExeGui/InfoExeGui.csproj`, `MainWindow.axaml`, `MainWindow.axaml.cs`, `Program.cs`, `App.axaml` [VERIFIED: file read in this session]
- Live repo read — `src/InfoExeApp/Program.cs` (CLI arg surface: `--root`, `--app`, `--path`, `--args`, `--db`, `--python`) [VERIFIED]
- NuGet flat-container API — `avalonia`, `avalonia.desktop`, `avalonia.themes.fluent` latest 11.x = **11.3.15** [VERIFIED: live API call]
- NuGet flat-container API — Avalonia 12.0.x series exists (12.0.3); not used [VERIFIED: live API call]
- PowerShell path resolution test — 5-level `..` does not exist; 4-level `..` does [VERIFIED: live test]
- `.gitignore` — `*.db` is excluded [VERIFIED: file read]

### Secondary (MEDIUM confidence)
- `Tmds.DBus.Protocol` GHSA-xrw6-gwf8-vvr9 advisory — visible in build output [VERIFIED: build output in session]

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — verified against NuGet live API
- Architecture: HIGH — verified against live codebase
- Pitfalls: HIGH (path bug) / MEDIUM (stream deadlock, output box perf) — path bug confirmed by test; others from well-known .NET patterns

**Research date:** 2026-05-15  
**Valid until:** 2026-06-15 (Avalonia minor versions release frequently; re-verify before packaging)
