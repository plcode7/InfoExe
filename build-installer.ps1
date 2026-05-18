# build-installer.ps1 — Build InfoExe and create Inno Setup installer
# Usage: .\build-installer.ps1

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrEmpty($scriptDir)) { $scriptDir = Get-Location }

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  InfoExe Installer Build Script" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# ─────────────────────────────────────────────
# Step 1: Build InfoExeGui (self-contained)
# ─────────────────────────────────────────────
Write-Host "[1/3] Building InfoExeGui (self-contained)..." -ForegroundColor Yellow
$guiProject = Join-Path $scriptDir "src\InfoExeGui"
$guiOutput = Join-Path $guiProject "bin\Release\net10.0\win-x64\publish"

try {
    Push-Location $scriptDir
    dotnet publish $guiProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true
    if ($LASTEXITCODE -eq 0) {
        Write-Host "      InfoExeGui built successfully" -ForegroundColor Green
        Write-Host "      Output: $guiOutput" -ForegroundColor Gray
    } else {
        throw "Build failed with exit code $LASTEXITCODE"
    }
    Pop-Location
} catch {
    Write-Host "      ERROR: Failed to build InfoExeGui" -ForegroundColor Red
    Write-Host "      $_" -ForegroundColor Red
    Pop-Location
    exit 1
}

# ─────────────────────────────────────────────
# Step 2: Build InfoExeApp (self-contained)
# ─────────────────────────────────────────────
Write-Host "[2/3] Building InfoExeApp (self-contained)..." -ForegroundColor Yellow
$appProject = Join-Path $scriptDir "src\InfoExeApp"
$appOutput = Join-Path $appProject "bin\Release\net10.0\win-x64\publish"

try {
    Push-Location $scriptDir
    # Don't trim CLI - it uses reflection heavily
    dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
    if ($LASTEXITCODE -eq 0) {
        Write-Host "      InfoExeApp built successfully" -ForegroundColor Green
        Write-Host "      Output: $appOutput" -ForegroundColor Gray
    } else {
        throw "Build failed with exit code $LASTEXITCODE"
    }
    Pop-Location
} catch {
    Write-Host "      ERROR: Failed to build InfoExeApp" -ForegroundColor Red
    Write-Host "      $_" -ForegroundColor Red
    Pop-Location
    exit 1
}

# ─────────────────────────────────────────────
# Step 3: Create Inno Setup installer
# ─────────────────────────────────────────────
Write-Host "[3/3] Creating Inno Setup installer..." -ForegroundColor Yellow

# Check if Inno Setup compiler is available
$innocmd = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if (-not $innocmd) {
    # Try common installation paths (including winget location)
    $innocmdPaths = @(
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 5\ISCC.exe"
    )
    foreach ($path in $innocmdPaths) {
        if (Test-Path $path) {
            $innocmd = Get-Item $path
            break
        }
    }
}

if (-not $innocmd) {
    Write-Host "      ERROR: Inno Setup compiler (ISCC.exe) not found" -ForegroundColor Red
    Write-Host "      Download from: https://jrsoftware.org/isdl.php" -ForegroundColor Red
    Write-Host "      After installation, run this script again." -ForegroundColor Red
    exit 1
}

Write-Host "      Found Inno Setup at: $($innocmd.FullName)" -ForegroundColor Gray

$issScript = Join-Path $scriptDir "InfoExeInstaller.iss"
if (-not (Test-Path $issScript)) {
    Write-Host "      ERROR: InfoExeInstaller.iss not found" -ForegroundColor Red
    exit 1
}

try {
    $outputDir = Join-Path $scriptDir "installer-output"
    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }

    & $innocmd.FullName $issScript
    if ($LASTEXITCODE -eq 0) {
        Write-Host "      Installer created successfully" -ForegroundColor Green
        Write-Host "      Output directory: $outputDir" -ForegroundColor Gray
    } else {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
    }
} catch {
    Write-Host "      ERROR: Failed to create installer" -ForegroundColor Red
    Write-Host "      $_" -ForegroundColor Red
    exit 1
}

# ─────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────
Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  BUILD COMPLETE" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Installer location: $outputDir" -ForegroundColor White
Write-Host ""
Write-Host "  The installer includes:" -ForegroundColor White
Write-Host "    - InfoExe GUI (self-contained)" -ForegroundColor Gray
Write-Host "    - InfoExe CLI (self-contained)" -ForegroundColor Gray
Write-Host "    - setup.ps1 (for dependencies)" -ForegroundColor Gray
Write-Host ""
Write-Host "  User will need:" -ForegroundColor White
Write-Host "    - .NET 10.0 Runtime (or run setup.ps1)" -ForegroundColor Gray
Write-Host "    - ILSpy (ilspycmd) for decompilation (or run setup.ps1)" -ForegroundColor Gray
Write-Host ""
