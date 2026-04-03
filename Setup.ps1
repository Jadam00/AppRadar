#Requires -Version 5.1
<#
.SYNOPSIS
    AppRadar Windows Setup Script

.DESCRIPTION
    Validates prerequisites, checks dependencies, and prepares the AppRadar
    pipeline to run on Windows 11 / Windows Server.

    Run this script from the repository root before using AppRadar for the
    first time, or whenever you need to diagnose setup issues.

.EXAMPLE
    .\Setup.ps1

.EXAMPLE
    .\Setup.ps1 -GeneratePlaceholders

    Also generates placeholder images from the existing description.json files
    so you can run the pipeline immediately without real app screenshots.
#>

[CmdletBinding()]
param(
    [switch]$GeneratePlaceholders
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:PassCount = 0
$script:WarnCount = 0
$script:FailCount = 0

function Write-Pass([string]$Message) {
    Write-Host "  [PASS] $Message" -ForegroundColor Green
    $script:PassCount++
}

function Write-Warn([string]$Message) {
    Write-Host "  [WARN] $Message" -ForegroundColor Yellow
    $script:WarnCount++
}

function Write-Fail([string]$Message) {
    Write-Host "  [FAIL] $Message" -ForegroundColor Red
    $script:FailCount++
}

function Write-Section([string]$Title) {
    Write-Host ""
    Write-Host "--- $Title ---" -ForegroundColor Cyan
}

# ──────────────────────────────────────────────────────────────────────────────
# 1. Operating System
# ──────────────────────────────────────────────────────────────────────────────
Write-Section "Operating System"

if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    Write-Pass "Running on Windows"
    $osInfo = [System.Environment]::OSVersion.VersionString
    Write-Host "       $osInfo"
}
else {
    Write-Fail "AppRadar is supported on Windows only. Detected non-Windows environment."
}

# ──────────────────────────────────────────────────────────────────────────────
# 2. .NET SDK
# ──────────────────────────────────────────────────────────────────────────────
Write-Section ".NET SDK"

$dotnetExe = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetExe) {
    Write-Fail ".NET SDK not found. Install from: https://dotnet.microsoft.com/download/dotnet/10.0"
}
else {
    $dotnetVersion = & dotnet --version 2>&1
    if ($dotnetVersion -match '^10\.') {
        Write-Pass ".NET SDK $dotnetVersion"
    }
    elseif ($dotnetVersion -match '^\d+\.') {
        Write-Warn ".NET SDK $dotnetVersion found but AppRadar targets net10.0. Consider upgrading."
        Write-Host "       Download: https://dotnet.microsoft.com/download/dotnet/10.0"
    }
    else {
        Write-Fail "Unable to determine .NET SDK version. Output: $dotnetVersion"
    }
}

# ──────────────────────────────────────────────────────────────────────────────
# 3. FFmpeg
# ──────────────────────────────────────────────────────────────────────────────
Write-Section "FFmpeg"

$ffmpegCandidates = @(
    'C:\ffmpeg\bin\ffmpeg.exe',
    'C:\Program Files\ffmpeg\bin\ffmpeg.exe',
    'C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe',
    (Join-Path $env:ProgramData 'chocolatey\bin\ffmpeg.exe'),
    (Join-Path $env:USERPROFILE  'scoop\shims\ffmpeg.exe'),
    'C:\ProgramData\scoop\shims\ffmpeg.exe',
    (Join-Path $env:LOCALAPPDATA 'Programs\ffmpeg\bin\ffmpeg.exe')
)

# Also check WinGet packages folder for Gyan.FFmpeg installs
$wingetBase = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
if (Test-Path $wingetBase) {
    Get-ChildItem "$wingetBase\Gyan.FFmpeg_*" -Directory -ErrorAction SilentlyContinue |
        ForEach-Object {
            Get-ChildItem "$($_.FullName)\ffmpeg-*\bin\ffmpeg.exe" -ErrorAction SilentlyContinue |
                ForEach-Object { $ffmpegCandidates += $_.FullName }
        }
}

# Also honour explicit env var
if ($env:FFMPEG_PATH -and (Test-Path $env:FFMPEG_PATH)) {
    $ffmpegCandidates = @($env:FFMPEG_PATH) + $ffmpegCandidates
}

$ffmpegFound = $false
foreach ($candidate in $ffmpegCandidates) {
    if (Test-Path $candidate) {
        $ver = & "$candidate" -version 2>&1 | Select-Object -First 1
        Write-Pass "FFmpeg found: $candidate"
        Write-Host "       $ver"
        $ffmpegFound = $true
        break
    }
}

if (-not $ffmpegFound) {
    # Try PATH
    $ffmpegOnPath = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($ffmpegOnPath) {
        $ver = & ffmpeg -version 2>&1 | Select-Object -First 1
        Write-Pass "FFmpeg found on PATH: $($ffmpegOnPath.Source)"
        Write-Host "       $ver"
        $ffmpegFound = $true
    }
}

if (-not $ffmpegFound) {
    Write-Fail "FFmpeg not found."
    Write-Host ""
    Write-Host "  To install FFmpeg on Windows, use one of:" -ForegroundColor Yellow
    Write-Host "    winget install Gyan.FFmpeg" -ForegroundColor White
    Write-Host "    choco install ffmpeg" -ForegroundColor White
    Write-Host "    scoop install ffmpeg" -ForegroundColor White
    Write-Host ""
    Write-Host "  Or download manually from: https://ffmpeg.org/download.html#build-windows" -ForegroundColor Yellow
    Write-Host "  Extract to C:\ffmpeg and add C:\ffmpeg\bin to your PATH." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Alternatively, set the explicit path in input\config.json:" -ForegroundColor Yellow
    Write-Host '    "tools": { "ffmpegPath": "C:\\ffmpeg\\bin\\ffmpeg.exe" }' -ForegroundColor White
    Write-Host ""
    Write-Host "  Or set the FFMPEG_PATH environment variable:" -ForegroundColor Yellow
    Write-Host '    $env:FFMPEG_PATH = "C:\ffmpeg\bin\ffmpeg.exe"' -ForegroundColor White
}

# ──────────────────────────────────────────────────────────────────────────────
# 4. Required folders
# ──────────────────────────────────────────────────────────────────────────────
Write-Section "Required Folders"

$requiredFolders = @(
    'input\featuredApps\images',
    'input\myApps\images',
    'output\images',
    'output\videos',
    'output\manifests',
    'temp'
)

foreach ($folder in $requiredFolders) {
    $fullPath = Join-Path $PSScriptRoot $folder
    if (Test-Path $fullPath) {
        Write-Pass "Exists: $folder"
    }
    else {
        New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
        Write-Pass "Created: $folder"
    }
}

# ──────────────────────────────────────────────────────────────────────────────
# 5. Input files
# ──────────────────────────────────────────────────────────────────────────────
Write-Section "Input Files"

$descFiles = @(
    'input\featuredApps\description.json',
    'input\myApps\description.json'
)

foreach ($file in $descFiles) {
    $fullPath = Join-Path $PSScriptRoot $file
    if (Test-Path $fullPath) {
        Write-Pass "Found: $file"
    }
    else {
        Write-Warn "Missing: $file  (create this file to define your app metadata)"
    }
}

$configPath = Join-Path $PSScriptRoot 'input\config.json'
if (Test-Path $configPath) {
    Write-Pass "Found: input\config.json"
}
else {
    Write-Warn "Missing: input\config.json  (optional — defaults will be used)"
}

# ──────────────────────────────────────────────────────────────────────────────
# 6. Build check
# ──────────────────────────────────────────────────────────────────────────────
Write-Section "Build"

if ($null -ne $dotnetExe) {
    Write-Host "  Building project..."
    Push-Location $PSScriptRoot
    try {
        $buildOutput = & dotnet build src\AppRadar --nologo --verbosity quiet 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Pass "Build succeeded"
        }
        else {
            Write-Fail "Build failed. Output:"
            $buildOutput | ForEach-Object { Write-Host "    $_" }
        }
    }
    finally {
        Pop-Location
    }
}
else {
    Write-Warn "Skipping build check — .NET SDK not found"
}

# ──────────────────────────────────────────────────────────────────────────────
# 7. Optional: generate placeholder images
# ──────────────────────────────────────────────────────────────────────────────
if ($GeneratePlaceholders -and $null -ne $dotnetExe) {
    Write-Section "Generating Placeholder Images"
    Push-Location $PSScriptRoot
    try {
        & dotnet run --project src\AppRadar --no-build -- setup --input input
        if ($LASTEXITCODE -eq 0) {
            Write-Pass "Placeholder images generated"
        }
        else {
            Write-Fail "Setup command failed (exit code $LASTEXITCODE)"
        }
    }
    finally {
        Pop-Location
    }
}

# ──────────────────────────────────────────────────────────────────────────────
# Summary
# ──────────────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host " Setup Summary" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Pass : $($script:PassCount)" -ForegroundColor Green
if ($script:WarnCount -gt 0) {
    Write-Host "  Warn : $($script:WarnCount)" -ForegroundColor Yellow
}
if ($script:FailCount -gt 0) {
    Write-Host "  Fail : $($script:FailCount)" -ForegroundColor Red
}
Write-Host ""

if ($script:FailCount -eq 0 -and $script:WarnCount -eq 0) {
    Write-Host "All checks passed. You are ready to run AppRadar!" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Generate placeholder images (first time):" -ForegroundColor Cyan
    Write-Host "    dotnet run --project src\AppRadar -- setup --input input"
    Write-Host ""
    Write-Host "  Generate a reel:" -ForegroundColor Cyan
    Write-Host "    dotnet run --project src\AppRadar -- generate --count 1 --duration 24"
}
elseif ($script:FailCount -eq 0) {
    Write-Host "Setup completed with warnings. Review items above before running." -ForegroundColor Yellow
}
else {
    Write-Host "Setup has failures. Resolve the issues above before running AppRadar." -ForegroundColor Red
    exit 1
}
