# InfoExe Documentation

Welcome to the **InfoExe** documentation site.

## What is InfoExe?

InfoExe is a local offline tool for analyzing .NET binaries (`.dll`, `.exe`) with optional Python lane support.
It scans directories, collects metadata, identifies library vendors, decompiles IL to C# projects,
and generates comprehensive analysis reports.

## Quick Start

### Installation

```powershell
# Download the installer from GitHub Releases
# Run the installer and follow the wizard
```

### CLI Commands

```powershell
# Scan a directory
InfoExeApp scan --root "C:\MyApp"

# Check scan status
InfoExeApp status --id <scanId>

# Discover local databases
InfoExeApp dbscan

# Analyze Windows Registry
InfoExeApp regscan

# Scan configuration files for connection strings
InfoExeApp configscan --path "C:\MyProject"
```

## Documentation Sections

- **[Articles](articles/)** — User guides and feature documentation
- **[Planning](planning/)** — Project planning, architecture, and roadmap
- **[API Reference](api/)** — Generated API documentation from source code

## Key Features

| Feature | Description |
|---------|-------------|
| 🔍 **.NET Scanning** | Analyze DLL/EXE files for metadata, dependencies, and technologies |
| 🔓 **Decompilation** | Decompile IL to C# source code using ILSpy |
| 🏷️ **Vendor Attribution** | Identify library creators and vendors |
| 📊 **Reporting** | Export to JSON, CSV, Markdown, and HTML formats |
| 🗄️ **Database Discovery** | Find local SQLite, MySQL, MSSQL, PostgreSQL databases |
| 📝 **Registry Analysis** | Audit Windows Registry for broken paths, suspicious keys, and errors |
| ⚙️ **Config Scanning** | Extract database connection strings from configuration files |
| 🖥️ **GUI** | Avalonia-based desktop interface with live progress |

## Project Links

- **GitHub Repository:** [github.com/passcode2026/InfoExe](https://github.com/passcode2026/InfoExe)
- **Downloads:** [github.com/passcode2026/InfoExe/releases](https://github.com/passcode2026/InfoExe/releases)

## License

MIT License — see LICENSE.txt for details.