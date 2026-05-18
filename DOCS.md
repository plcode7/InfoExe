# Documentation

This project uses [docfx](https://dotnet.github.io/docfx/) to generate API documentation and user guides.

## Building Documentation

### Prerequisites

- docfx 2.78.5+ (installed via: `dotnet tool install -g docfx`)
- .NET SDK 10.0

### Quick Build

**PowerShell:**
```powershell
.\build-docs.ps1
```

**Bash:**
```bash
./build-docs.sh
```

### Manual Build

```bash
# Generate API metadata from .NET projects
docfx metadata docfx.json

# Build HTML documentation
docfx build docfx.json

# Copy index.html to root
cp docs/_site/docs/index.html docs/_site/index.html
```

### Serve Documentation Locally

```bash
docfx serve docs/_site --port 8080
```

Then open http://localhost:8080 in your browser.

## Documentation Structure

- **docs/index.md** - Main landing page
- **docs/toc.yml** - Table of contents
- **docfx.json** - Docfx configuration
- **filterConfig.yml** - API filter configuration
- **articles/** - User guides and feature documentation
- **planning/** - Project planning documents
- **api/** - Generated API documentation (auto-generated)

## Adding Documentation

### Adding Articles

1. Create a new markdown file in the project root (e.g., `NEW_FEATURE.md`)
2. Add it to `docfx.json` under the articles section
3. Add an entry to `docs/toc.yml`

### Adding API Documentation

API documentation is automatically generated from XML documentation comments in the source code. Simply add `///` comments to your classes and methods:

```csharp
/// <summary>
/// Scans a directory for .NET assemblies
/// </summary>
/// <param name="rootPath">The root directory to scan</param>
/// <returns>A list of scan results</returns>
public async Task<List<ScanResult>> ScanAsync(string rootPath)
{
    // ...
}
```

Then rebuild the documentation.

## GitHub Pages

To deploy documentation to GitHub Pages:

1. Enable GitHub Pages in repository settings
2. Set source to `docs/_site` directory
3. Create a workflow file `.github/workflows/docs.yml`:

```yaml
name: Build Documentation

on:
  push:
    branches: [ master ]
    paths:
      - 'src/**'
      - 'docs/**'
      - 'docfx.json'
      - 'filterConfig.yml'

jobs:
  build:
    runs-on: windows-latest
    steps:
    - uses: actions/checkout@v3
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '10.0.x'
    - name: Install docfx
      run: dotnet tool install -g docfx
    - name: Build documentation
      run: |
        docfx metadata docfx.json
        docfx build docfx.json
        Copy-Item docs/_site/docs/index.html docs/_site/index.html
    - name: Deploy to GitHub Pages
      uses: peaceiris/actions-gh-pages@v3
      with:
        github_token: ${{ secrets.GITHUB_TOKEN }}
        publish_dir: ./docs/_site
```

## Configuration

### docfx.json

Main configuration file that defines:
- Metadata sources (which .NET projects to document)
- Content sources (which markdown files to include)
- Output destination
- Global metadata (title, footer, etc.)

### filterConfig.yml

Controls which namespaces and types are included in the API documentation. Currently includes:
- InfoExeCore namespace
- InfoExeGui namespace
- InfoExeApp namespace

Excludes:
- System.* types
- Microsoft.Data.Sqlite namespace
- Avalonia namespace

## Troubleshooting

### Build fails with "No files found"

Ensure the paths in `docfx.json` are correct relative to the project root.

### API documentation missing

Run `docfx metadata docfx.json` first to generate the YAML files from the .NET projects.

### Links broken in planning documents

Some planning documents contain links to milestone files that don't exist in the public documentation. These warnings can be ignored.