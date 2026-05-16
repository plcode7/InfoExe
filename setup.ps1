# setup.ps1 — InfoExe bootstrap script
# Uruchom: .\setup.ps1

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrEmpty($scriptDir)) { $scriptDir = Get-Location }

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  InfoExe Setup — v1.1" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

$allOk = $true

# ─────────────────────────────────────────────
# Step 1: .NET SDK 10.0
# ─────────────────────────────────────────────
Write-Host "[1/4] .NET SDK 10.0..." -ForegroundColor Yellow
$dotnetOk = $false
try {
    $dotnetVersion = dotnet --version 2>$null
    if ($LASTEXITCODE -eq 0 -and $dotnetVersion -match "^10\.") {
        Write-Host "      .NET SDK $dotnetVersion already present" -ForegroundColor Green
        $dotnetOk = $true
    }
} catch { }

if (-not $dotnetOk) {
    Write-Host "      Installing .NET SDK 10.0 via winget..." -ForegroundColor Yellow
    try {
        winget install Microsoft.DotNet.SDK.10 --accept-source-agreements --accept-package-agreements --silent
        Write-Host "      .NET SDK 10.0 installed" -ForegroundColor Green
    } catch {
        Write-Host "      ERROR: Could not install .NET SDK." -ForegroundColor Red
        Write-Host "      Install manually: https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Red
        $allOk = $false
    }
}

# ─────────────────────────────────────────────
# Step 2: ILSpy (ilspycmd)
# ─────────────────────────────────────────────
Write-Host "[2/4] ILSpy (ilspycmd)..." -ForegroundColor Yellow
$ilspyOk = $false
try {
    $toolList = dotnet tool list --global 2>$null
    if ($LASTEXITCODE -eq 0 -and $toolList -match "ilspycmd") {
        Write-Host "      ILSpy already installed" -ForegroundColor Green
        $ilspyOk = $true
    }
} catch { }

if (-not $ilspyOk) {
    Write-Host "      Installing ILSpy as global dotnet tool..." -ForegroundColor Yellow
    try {
        dotnet tool install --global ilspycmd
        Write-Host "      ILSpy installed successfully" -ForegroundColor Green
    } catch {
        Write-Host "      ERROR: Could not install ILSpy." -ForegroundColor Red
        Write-Host "      Install manually: dotnet tool install --global ilspycmd" -ForegroundColor Red
        $allOk = $false
    }
}

# ─────────────────────────────────────────────
# Step 3: NuGet restore
# ─────────────────────────────────────────────
Write-Host "[3/4] NuGet restore..." -ForegroundColor Yellow
try {
    Push-Location $scriptDir
    $slnPath = Join-Path $scriptDir "src\InfoExe.sln"
    if (Test-Path $slnPath) {
        dotnet restore $slnPath
        Write-Host "      NuGet packages restored" -ForegroundColor Green
    } else {
        Write-Host "      WARNING: src\InfoExe.sln not found" -ForegroundColor Yellow
    }
    Pop-Location
} catch {
    Write-Host "      ERROR: NuGet restore failed." -ForegroundColor Red
    $allOk = $false
    Pop-Location
}

# ─────────────────────────────────────────────
# Step 4: SQLite database
# ─────────────────────────────────────────────
Write-Host "[4/4] SQLite database..." -ForegroundColor Yellow
try {
    $dataDir = Join-Path $env:LOCALAPPDATA "InfoExe"
    New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
    $dbPath = Join-Path $dataDir "infoexe.db"

    Push-Location $scriptDir
    $appProject = Join-Path $scriptDir "src\InfoExeApp"
    if (Test-Path $appProject) {
        dotnet run --project $appProject -- --db $dbPath help 2>$null | Out-Null
        Write-Host "      Database initialized at $dbPath" -ForegroundColor Green
    } else {
        Write-Host "      WARNING: src\InfoExeApp not found — skipping DB init" -ForegroundColor Yellow
    }
    Pop-Location
} catch {
    Write-Host "      WARNING: Database initialization skipped (non-critical)" -ForegroundColor Yellow
}

# ─────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────
Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
if ($allOk) {
    Write-Host "  SETUP COMPLETE" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Run the GUI:" -ForegroundColor White
    Write-Host "    dotnet run --project src\InfoExeGui" -ForegroundColor White
    Write-Host ""
    Write-Host "  Or build a standalone exe:" -ForegroundColor White
    Write-Host "    dotnet publish src\InfoExeGui -c Release -o .\publish" -ForegroundColor White
} else {
    Write-Host "  SETUP COMPLETE WITH WARNINGS" -ForegroundColor Yellow
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Some steps had issues. Review the output above." -ForegroundColor Yellow
}
Write-Host ""