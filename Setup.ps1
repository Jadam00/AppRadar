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
    [switch]$GeneratePlaceholders,
    [switch]$StrictXtts,
    [switch]$NoSelfHeal
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

function Write-Info([string]$Message) {
    Write-Host "  [INFO] $Message" -ForegroundColor DarkGray
}

function Get-ObjectValue {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    if ($Object -is [System.Collections.IDictionary]) {
        foreach ($key in $Object.Keys) {
            if ([string]::Equals([string]$key, $Name, [System.StringComparison]::OrdinalIgnoreCase)) {
                return $Object[$key]
            }
        }
        return $null
    }

    $property = $Object.PSObject.Properties |
        Where-Object { [string]::Equals($_.Name, $Name, [System.StringComparison]::OrdinalIgnoreCase) } |
        Select-Object -First 1

    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-ConfigValue {
    param(
        [Parameter(Mandatory)]$Config,
        [Parameter(Mandatory)][string[]]$Path,
        $DefaultValue = $null
    )

    if ($null -eq $Config) {
        return $DefaultValue
    }

    $current = $Config
    foreach ($segment in $Path) {
        $current = Get-ObjectValue -Object $current -Name $segment
        if ($null -eq $current) {
            return $DefaultValue
        }
    }

    return $current
}

function To-Bool {
    param(
        $Value,
        [bool]$DefaultValue = $false
    )

    if ($null -eq $Value) {
        return $DefaultValue
    }

    if ($Value -is [bool]) {
        return $Value
    }

    if ($Value -is [string]) {
        if ([string]::Equals($Value, 'true', [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }

        if ([string]::Equals($Value, 'false', [System.StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
    }

    return $DefaultValue
}

function Resolve-AppPath {
    param(
        [string]$PathValue,
        [string]$DefaultRelativePath = $null
    )

    $effective = if ([string]::IsNullOrWhiteSpace($PathValue)) { $DefaultRelativePath } else { $PathValue }
    if ([string]::IsNullOrWhiteSpace($effective)) {
        return $null
    }

    if ([System.IO.Path]::IsPathRooted($effective)) {
        return $effective
    }

    return Join-Path $PSScriptRoot $effective
}

function Resolve-Executable {
    param([Parameter(Mandatory)][string]$CommandName)

    if ([string]::IsNullOrWhiteSpace($CommandName)) {
        return $null
    }

    if (Test-Path $CommandName) {
        return (Resolve-Path $CommandName).Path
    }

    $cmd = Get-Command $CommandName -ErrorAction SilentlyContinue
    if ($null -eq $cmd) {
        return $null
    }

    return $cmd.Source
}

function Get-PythonVersion {
    param([Parameter(Mandatory)][string]$PythonExe)

    try {
        $output = & $PythonExe -c "import sys; print(f'{sys.version_info[0]}.{sys.version_info[1]}.{sys.version_info[2]}')" 2>&1
        if ($LASTEXITCODE -ne 0) {
            return $null
        }

        $versionText = [string]($output | Select-Object -Last 1)
        return [version]$versionText
    }
    catch {
        return $null
    }
}

function Get-PythonModuleVersion {
    param(
        [Parameter(Mandatory)][string]$PythonExe,
        [Parameter(Mandatory)][string]$ModuleName
    )

    try {
        $output = & $PythonExe -c "import $ModuleName; print($ModuleName.__version__)" 2>&1
        if ($LASTEXITCODE -ne 0) {
            return $null
        }

        return [string]($output | Select-Object -Last 1)
    }
    catch {
        return $null
    }
}

function Get-TorchDiagnostics {
    param([Parameter(Mandatory)][string]$PythonExe)

    $probe = @"
import json
import torch

info = {
    'torchVersion': torch.__version__,
    'cudaAvailable': bool(torch.cuda.is_available()),
    'cudaRuntime': str(torch.version.cuda),
    'archList': [],
    'deviceName': '',
}

if info['cudaAvailable']:
    info['archList'] = torch.cuda.get_arch_list()
    info['deviceName'] = torch.cuda.get_device_name(0)

print(json.dumps(info))
"@

    try {
        $result = Invoke-NativeCommandCapture -Command {
            & $PythonExe -c $probe
        }

        if ($result.ExitCode -ne 0) {
            return $null
        }

        $jsonLine = [string]($result.Output | Select-Object -Last 1)
        if ([string]::IsNullOrWhiteSpace($jsonLine)) {
            return $null
        }

        return $jsonLine | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Test-SupportedPythonVersion {
    param([version]$Version)

    if ($null -eq $Version) {
        return $false
    }

    # XTTS dependency TTS==0.22.0 supports Python >=3.9,<3.12. We enforce 3.10-3.11.
    return ($Version -ge [version]'3.10.0' -and $Version -lt [version]'3.12.0')
}

function Invoke-NativeCommandCapture {
    param([Parameter(Mandatory)][scriptblock]$Command)

    $previousPreference = $ErrorActionPreference
    $output = @()
    $exitCode = 1

    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $Command 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    return [pscustomobject]@{
        Output = $output
        ExitCode = $exitCode
    }
}

function Get-SupportedPythonInterpreter {
    param([string]$PreferredCommand)

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($PreferredCommand)) {
        $candidates += [pscustomobject]@{ Kind = 'command'; Value = $PreferredCommand; Source = "config ($PreferredCommand)" }
    }

    $candidates += [pscustomobject]@{ Kind = 'launcher'; Value = '3.11'; Source = 'py -3.11' }
    $candidates += [pscustomobject]@{ Kind = 'launcher'; Value = '3.10'; Source = 'py -3.10' }
    $candidates += [pscustomobject]@{ Kind = 'command'; Value = 'python3.11'; Source = 'python3.11' }
    $candidates += [pscustomobject]@{ Kind = 'command'; Value = 'python3.10'; Source = 'python3.10' }
    $candidates += [pscustomobject]@{ Kind = 'command'; Value = 'python'; Source = 'python' }

    $seen = @{}
    $pyLauncher = Resolve-Executable -CommandName 'py'

    foreach ($candidate in $candidates) {
        if ($candidate.Kind -eq 'command') {
            $exe = Resolve-Executable -CommandName $candidate.Value
            if ($null -eq $exe) {
                continue
            }

            $dedupeKey = "cmd::$exe"
            if ($seen.ContainsKey($dedupeKey)) {
                continue
            }

            $seen[$dedupeKey] = $true
            $version = Get-PythonVersion -PythonExe $exe
            if (Test-SupportedPythonVersion -Version $version) {
                return [pscustomobject]@{
                    Path = $exe
                    Version = $version
                    Source = $candidate.Source
                }
            }

            continue
        }

        if ($null -eq $pyLauncher) {
            continue
        }

        $dedupeKey = "launcher::$($candidate.Value)"
        if ($seen.ContainsKey($dedupeKey)) {
            continue
        }

        $seen[$dedupeKey] = $true

        try {
            $launcherVersionResult = Invoke-NativeCommandCapture -Command {
                & $pyLauncher "-$($candidate.Value)" -c "import sys; print(f'{sys.version_info[0]}.{sys.version_info[1]}.{sys.version_info[2]}')"
            }

            if ($launcherVersionResult.ExitCode -ne 0) {
                continue
            }

            $version = [version]([string]($launcherVersionResult.Output | Select-Object -Last 1))
            if (-not (Test-SupportedPythonVersion -Version $version)) {
                continue
            }

            $exePathResult = Invoke-NativeCommandCapture -Command {
                & $pyLauncher "-$($candidate.Value)" -c "import sys; print(sys.executable)"
            }

            if ($exePathResult.ExitCode -ne 0) {
                continue
            }

            $exePath = [string]($exePathResult.Output | Select-Object -Last 1)
            if (-not [string]::IsNullOrWhiteSpace($exePath) -and (Test-Path $exePath)) {
                return [pscustomobject]@{
                    Path = $exePath
                    Version = $version
                    Source = $candidate.Source
                }
            }
        }
        catch {
            continue
        }
    }

    return $null
}

function Install-SupportedPython {
    $winget = Resolve-Executable -CommandName 'winget'
    if ($null -eq $winget) {
        Write-Warn "winget not found. Cannot auto-install Python. Install Python 3.10-3.11 manually."
        return $false
    }

        Write-Host "  Installing Python 3.11 via winget..."
        $installResult = Invoke-NativeCommandCapture -Command {
            & $winget install --id Python.Python.3.11 --accept-source-agreements --accept-package-agreements --silent --disable-interactivity
        }

        if ($installResult.ExitCode -eq 0) {
            Write-Pass "Installed Python 3.11 via winget"
        return $true
    }

        Write-Warn "Python auto-install did not complete successfully (winget exit code $($installResult.ExitCode))."
    return $false
}

function Normalize-Provider {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return 'sapi'
    }

    return $Value.Trim().ToLowerInvariant()
}

function Join-Url {
    param(
        [Parameter(Mandatory)][string]$BaseUrl,
        [Parameter(Mandatory)][string]$Path
    )

    return ($BaseUrl.TrimEnd('/') + '/' + $Path.TrimStart('/'))
}

function Invoke-WithRetries {
    param(
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][scriptblock]$Operation,
        [int]$MaxAttempts = 2,
        [int]$DelaySeconds = 3
    )

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $result = Invoke-NativeCommandCapture -Command $Operation
        if ($result.ExitCode -eq 0) {
            return [pscustomobject]@{
                Success = $true
                Result = $result
                Attempts = $attempt
            }
        }

        if ($attempt -lt $MaxAttempts) {
            Write-Warn "$Label failed on attempt $attempt/$MaxAttempts. Retrying in $DelaySeconds seconds..."
            Start-Sleep -Seconds $DelaySeconds
        }
    }

    return [pscustomobject]@{
        Success = $false
        Result = $result
        Attempts = $MaxAttempts
    }
}

function Test-CommandAvailable {
    param([Parameter(Mandatory)][string]$CommandName)

    $cmd = Get-Command $CommandName -ErrorAction SilentlyContinue
    return ($null -ne $cmd)
}

function Ensure-FFmpegInstalled {
    if (Test-CommandAvailable -CommandName 'ffmpeg') {
        return $true
    }

    $winget = Resolve-Executable -CommandName 'winget'
    if ($null -eq $winget) {
        return $false
    }

    Write-Host "  Attempting to install FFmpeg via winget..."
    $installResult = Invoke-NativeCommandCapture -Command {
        & $winget install --id Gyan.FFmpeg --accept-source-agreements --accept-package-agreements --silent --disable-interactivity
    }

    if ($installResult.ExitCode -ne 0) {
        return $false
    }

    return (Test-CommandAvailable -CommandName 'ffmpeg')
}

function Ensure-PythonPackageVersion {
    param(
        [Parameter(Mandatory)][string]$PythonExe,
        [Parameter(Mandatory)][string]$ModuleName,
        [Parameter(Mandatory)][string]$ExpectedVersion,
        [string]$PackageName = $null
    )

    $effectivePackage = if ([string]::IsNullOrWhiteSpace($PackageName)) { $ModuleName } else { $PackageName }
    $currentVersion = Get-PythonModuleVersion -PythonExe $PythonExe -ModuleName $ModuleName
    if ($currentVersion -eq $ExpectedVersion) {
        Write-Pass "$ModuleName version is pinned correctly: $currentVersion"
        return $true
    }

    Write-Warn "$ModuleName version drift detected (found '$currentVersion', expected '$ExpectedVersion'). Repairing..."
    $repairResult = Invoke-WithRetries -Label "pip install $effectivePackage==$ExpectedVersion" -MaxAttempts 2 -Operation {
        & $PythonExe -m pip install --upgrade --force-reinstall "$effectivePackage==$ExpectedVersion"
    }

    if (-not $repairResult.Success) {
        Write-Warn "Failed to repair $ModuleName to $ExpectedVersion"
        return $false
    }

    $recheckedVersion = Get-PythonModuleVersion -PythonExe $PythonExe -ModuleName $ModuleName
    if ($recheckedVersion -eq $ExpectedVersion) {
        Write-Pass "$ModuleName repaired to expected version: $recheckedVersion"
        return $true
    }

    Write-Warn "$ModuleName still not pinned after repair (found '$recheckedVersion', expected '$ExpectedVersion')"
    return $false
}

function Test-XttsHealth {
    param(
        [Parameter(Mandatory)][string]$HealthUrl,
        [int]$TimeoutSeconds = 10
    )

    try {
        $health = Invoke-RestMethod -Uri $HealthUrl -Method Get -TimeoutSec $TimeoutSeconds -ErrorAction Stop
        if ($null -ne $health) {
            return [pscustomobject]@{
                Healthy = $true
                Payload = $health
            }
        }
    }
    catch {
        return [pscustomobject]@{
            Healthy = $false
            Payload = $null
        }
    }

    return [pscustomobject]@{
        Healthy = $false
        Payload = $null
    }
}

function Start-XttsService {
    param(
        [Parameter(Mandatory)][string]$PythonExe,
        [Parameter(Mandatory)][string]$ScriptPath,
        [Parameter(Mandatory)][string]$WorkingDirectory
    )

    if (-not (Test-Path $PythonExe) -or -not (Test-Path $ScriptPath)) {
        return $false
    }

    try {
        Start-Process -FilePath $PythonExe -ArgumentList @($ScriptPath) -WorkingDirectory $WorkingDirectory -WindowStyle Hidden | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

$configPath = Join-Path $PSScriptRoot 'input\config.json'
$config = $null
if (Test-Path $configPath) {
    try {
        $configRaw = Get-Content -Path $configPath -Raw -Encoding UTF8
        $config = $configRaw | ConvertFrom-Json
    }
    catch {
        Write-Fail "input\\config.json is not valid JSON: $($_.Exception.Message)"
    }
}

# ──────────────────────────────────────────────────────────────────────────────
# 1. Operating System
# ──────────────────────────────────────────────────────────────────────────────
Write-Section "Operating System"

$isWindowsVar = $false
$isWindowsRef = Get-Variable -Name IsWindows -ErrorAction SilentlyContinue
if ($null -ne $isWindowsRef) {
    $isWindowsVar = [bool]$isWindowsRef.Value
}

if ($isWindowsVar -or $env:OS -eq 'Windows_NT') {
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
# 3. Configuration
# ──────────────────────────────────────────────────────────────────────────────
Write-Section "Configuration"

if (Test-Path $configPath) {
    if ($null -ne $config) {
        Write-Pass "Loaded: input\\config.json"
    }
}
else {
    Write-Warn "Missing: input\\config.json  (optional - defaults will be used)"
}

$audioEnabled = To-Bool (Get-ConfigValue -Config $config -Path @('audio', 'enabled') -DefaultValue $true) $true
$ttsProvider = Normalize-Provider (Get-ConfigValue -Config $config -Path @('audio', 'ttsProvider') -DefaultValue 'sapi')
$llmEnabled = To-Bool (Get-ConfigValue -Config $config -Path @('llm', 'enabled') -DefaultValue $false) $false

Write-Info "Audio enabled: $audioEnabled"
Write-Info "TTS provider: $ttsProvider"
Write-Info "LLM enabled: $llmEnabled"

# ──────────────────────────────────────────────────────────────────────────────
# 4. FFmpeg
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

$ffmpegFromConfig = Resolve-AppPath -PathValue (Get-ConfigValue -Config $config -Path @('tools', 'ffmpegPath'))
if (-not [string]::IsNullOrWhiteSpace($ffmpegFromConfig) -and (Test-Path $ffmpegFromConfig)) {
    $ffmpegCandidates = @($ffmpegFromConfig) + $ffmpegCandidates
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
    if (-not $NoSelfHeal) {
        $ffmpegInstalled = Ensure-FFmpegInstalled
        if ($ffmpegInstalled) {
            $ffmpegOnPath = Get-Command ffmpeg -ErrorAction SilentlyContinue
            if ($ffmpegOnPath) {
                $ver = & ffmpeg -version 2>&1 | Select-Object -First 1
                Write-Pass "FFmpeg auto-installed: $($ffmpegOnPath.Source)"
                Write-Host "       $ver"
                $ffmpegFound = $true
            }
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
}

# ──────────────────────────────────────────────────────────────────────────────
# 5. Required folders
# ──────────────────────────────────────────────────────────────────────────────
Write-Section "Required Folders"

$requiredFolders = @(
    'input\featuredApps\images',
    'input\myApps\images',
    'output\images',
    'output\videos',
    'output\manifests',
    'voices\brand',
    'temp\audio',
    'temp\scripts',
    'tts-service',
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
# 6. Input files
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

if (Test-Path $configPath) {
    if ($null -ne $config) {
        Write-Pass "Found: input\\config.json"
    }
    else {
        Write-Fail "Found: input\\config.json but failed to parse."
    }
}

# ──────────────────────────────────────────────────────────────────────────────
# 7. Python + XTTS environment
# ──────────────────────────────────────────────────────────────────────────────
Write-Section "Python + XTTS Environment"

$xttsPythonCommand = [string](Get-ConfigValue -Config $config -Path @('audio', 'xtts', 'pythonCommand') -DefaultValue 'python')
$pythonSelection = Get-SupportedPythonInterpreter -PreferredCommand $xttsPythonCommand
$pythonVersionSupported = $false
$venvPath = Join-Path $PSScriptRoot '.venv-xtts'
$venvPython = Join-Path $venvPath 'Scripts\python.exe'
$venvReady = $false

if ($null -eq $pythonSelection) {
    Write-Warn "No supported Python (3.10-3.11) detected. Attempting auto-install."
    $installedPython = Install-SupportedPython
    if ($installedPython) {
        $pythonSelection = Get-SupportedPythonInterpreter -PreferredCommand $xttsPythonCommand
    }
}

$pythonExe = $null
if ($null -ne $pythonSelection) {
    $pythonExe = [string]$pythonSelection.Path
    $pythonVersion = [version]$pythonSelection.Version
    Write-Pass "Python $pythonVersion selected from $($pythonSelection.Source)"
    $pythonVersionSupported = $true
}

if ($null -eq $pythonExe) {
    Write-Warn "Python not found for command '$xttsPythonCommand'. Install Python 3.10-3.11 to enable XTTS setup."
}

if ($pythonVersionSupported) {
    $recreateVenv = $false
    if (Test-Path $venvPython) {
        $existingVenvVersion = Get-PythonVersion -PythonExe $venvPython
        if ($null -eq $existingVenvVersion) {
            Write-Warn "Existing .venv-xtts interpreter could not be inspected. Recreating environment."
            $recreateVenv = $true
        }
        elseif (($existingVenvVersion.Major -ne $pythonVersion.Major) -or ($existingVenvVersion.Minor -ne $pythonVersion.Minor)) {
            Write-Warn "Existing .venv-xtts uses Python $existingVenvVersion, expected Python $pythonVersion. Recreating environment."
            $recreateVenv = $true
        }

        if ($recreateVenv) {
            Remove-Item -Path $venvPath -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    if (-not (Test-Path $venvPython)) {
        Write-Host "  Creating .venv-xtts..."
        & $pythonExe -m venv $venvPath 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0 -and (Test-Path $venvPython)) {
            Write-Pass "Created virtual environment: .venv-xtts"
        }
        else {
            Write-Warn "Failed to create .venv-xtts environment."
        }
    }
    else {
        Write-Pass "Found virtual environment: .venv-xtts"
    }

    if (Test-Path $venvPython) {
        $venvReady = $true
        $xttsStrictMode = ($StrictXtts.IsPresent -or $ttsProvider -eq 'xtts')
        $requiredTorchVersion = [version]'2.4.0'
        $torchIndexUrl = 'https://download.pytorch.org/whl/cu121'
        $torchNightlyIndexUrl = 'https://download.pytorch.org/whl/nightly/cu128'

        function Test-IsCpuTorchBuild {
            param([string]$TorchVersionText)

            if ([string]::IsNullOrWhiteSpace($TorchVersionText)) {
                return $false
            }

            return $TorchVersionText.Trim().ToLowerInvariant().Contains('+cpu')
        }

        Write-Host "  Upgrading pip in .venv-xtts..."
        $pipUpgradeResult = Invoke-WithRetries -Label "pip upgrade" -MaxAttempts 2 -Operation {
            & $venvPython -m pip install --upgrade pip
        }
        if ($pipUpgradeResult.Success) {
            Write-Pass "Pip upgraded in .venv-xtts"
        }
        else {
            Write-Warn "Failed to upgrade pip in .venv-xtts"
        }

        $requirementsPath = Join-Path $PSScriptRoot 'tts-service\requirements.txt'
        if (Test-Path $requirementsPath) {
            Write-Host "  Installing XTTS requirements (this may take several minutes)..."
            $pipInstallResult = Invoke-WithRetries -Label "pip install requirements" -MaxAttempts 2 -Operation {
                & $venvPython -m pip install -r $requirementsPath
            }
            if ($pipInstallResult.Success) {
                Write-Pass "Installed XTTS Python requirements"
            }
            else {
                $venvReady = $false
                Write-Fail "Failed to install XTTS requirements from tts-service\\requirements.txt"
            }
        }
        else {
            $venvReady = $false
            Write-Fail "Missing requirements file: tts-service\\requirements.txt"
        }

        if ($venvReady) {
            $ttsPinned = Ensure-PythonPackageVersion -PythonExe $venvPython -ModuleName 'TTS' -ExpectedVersion '0.22.0'
            $transformersPinned = Ensure-PythonPackageVersion -PythonExe $venvPython -ModuleName 'transformers' -ExpectedVersion '4.46.3'
            if (-not ($ttsPinned -and $transformersPinned)) {
                $venvReady = $false
            }
        }

        Write-Host "  Reinstalling CUDA-enabled PyTorch (cu121) for XTTS..."
        $torchInstallResult = Invoke-WithRetries -Label "pip install torch cu121" -MaxAttempts 2 -Operation {
            & $venvPython -m pip install --upgrade --force-reinstall --index-url $torchIndexUrl "torch>=$requiredTorchVersion"
        }
        if ($torchInstallResult.Success) {
            Write-Pass "Installed PyTorch from $torchIndexUrl"
        }
        else {
            Write-Fail "Failed to install CUDA-enabled PyTorch from $torchIndexUrl"
            $venvReady = $false
        }

        $torchVersionRaw = Get-PythonModuleVersion -PythonExe $venvPython -ModuleName 'torch'
        if ([string]::IsNullOrWhiteSpace($torchVersionRaw)) {
            Write-Warn "Unable to determine torch version in .venv-xtts"
        }
        else {
            $torchVersionCore = $torchVersionRaw.Split('+')[0]
            try {
                $torchVersion = [version]$torchVersionCore
                if ($torchVersion -ge $requiredTorchVersion -and -not (Test-IsCpuTorchBuild -TorchVersionText $torchVersionRaw)) {
                    Write-Pass "Torch version is compatible for RTX 5090: $torchVersionRaw"
                }
                elseif (Test-IsCpuTorchBuild -TorchVersionText $torchVersionRaw) {
                    Write-Warn "Torch build is CPU-only ($torchVersionRaw). Attempting repair with CUDA build from $torchIndexUrl"
                    if (-not $NoSelfHeal) {
                        $cpuRepairResult = Invoke-WithRetries -Label "repair torch cpu build" -MaxAttempts 2 -Operation {
                            & $venvPython -m pip install --upgrade --force-reinstall --index-url $torchIndexUrl "torch>=$requiredTorchVersion"
                        }
                        if (-not $cpuRepairResult.Success) {
                            $venvReady = $false
                        }
                    }
                }
                else {
                    Write-Warn "Torch version $torchVersionRaw may be too old for RTX 5090. Expected >= $requiredTorchVersion"
                }
            }
            catch {
                Write-Warn "Could not parse torch version '$torchVersionRaw'"
            }
        }

        $torchDiagnostics = Get-TorchDiagnostics -PythonExe $venvPython
        if ($null -ne $torchDiagnostics -and [bool]$torchDiagnostics.cudaAvailable) {
            $archList = @($torchDiagnostics.archList | ForEach-Object { [string]$_ })
            $deviceName = [string]$torchDiagnostics.deviceName
            $isRtx5090 = $deviceName -match 'RTX\s*5090'
            $supportsSm120 = $archList -contains 'sm_120'

            if ($isRtx5090 -and -not $supportsSm120) {
                Write-Warn "Detected $deviceName but installed torch build does not include sm_120 support. Installing nightly CUDA build."
                $nightlyInstallResult = Invoke-WithRetries -Label "pip install torch nightly cu128" -MaxAttempts 2 -Operation {
                    & $venvPython -m pip install --upgrade --force-reinstall --pre --index-url $torchNightlyIndexUrl torch
                }

                if ($nightlyInstallResult.Success) {
                    Write-Pass "Installed nightly PyTorch build for RTX 5090 from $torchNightlyIndexUrl"
                    $torchVersionAfterNightly = Get-PythonModuleVersion -PythonExe $venvPython -ModuleName 'torch'
                    if (-not [string]::IsNullOrWhiteSpace($torchVersionAfterNightly)) {
                        Write-Pass "Torch version after nightly upgrade: $torchVersionAfterNightly"
                    }

                    $diagAfterNightly = Get-TorchDiagnostics -PythonExe $venvPython
                    $archAfterNightly = @($diagAfterNightly.archList | ForEach-Object { [string]$_ })
                    if (-not ($archAfterNightly -contains 'sm_120')) {
                        Write-Warn "Nightly torch installed but sm_120 is still missing; trying one no-cache reinstall repair."
                        if (-not $NoSelfHeal) {
                            $repairNightlyNoCache = Invoke-WithRetries -Label "pip install torch nightly cu128 no-cache" -MaxAttempts 1 -Operation {
                                & $venvPython -m pip install --upgrade --force-reinstall --pre --no-cache-dir --index-url $torchNightlyIndexUrl torch
                            }
                            if (-not $repairNightlyNoCache.Success) {
                                $venvReady = $false
                            }
                        }
                    }
                }
                else {
                    Write-Fail "Failed to install nightly PyTorch build for RTX 5090 from $torchNightlyIndexUrl"
                    $venvReady = $false
                }
            }
        }

        if ($venvReady) {
            $torchCheck = & $venvPython -c "import torch; print('CUDA=' + str(bool(torch.cuda.is_available())))" 2>&1
            if ($LASTEXITCODE -eq 0) {
                $torchCheckText = [string]($torchCheck | Select-Object -Last 1)
                if ($torchCheckText -match 'CUDA=True') {
                    Write-Pass "CUDA-capable torch is available in .venv-xtts"
                }
                else {
                    Write-Warn "CUDA-capable torch not detected in .venv-xtts. Attempting one torch repair pass."
                    if (-not $NoSelfHeal) {
                        $cudaRepair = Invoke-WithRetries -Label "repair torch cuda availability" -MaxAttempts 1 -Operation {
                            & $venvPython -m pip install --upgrade --force-reinstall --index-url $torchIndexUrl "torch>=$requiredTorchVersion"
                        }

                        $torchCheckAfterRepair = & $venvPython -c "import torch; print('CUDA=' + str(bool(torch.cuda.is_available())))" 2>&1
                        $torchCheckAfterRepairText = [string]($torchCheckAfterRepair | Select-Object -Last 1)
                        if (-not $cudaRepair.Success -or $torchCheckAfterRepairText -notmatch 'CUDA=True') {
                            if ($xttsStrictMode) {
                                Write-Fail "CUDA-capable torch is still unavailable after repair attempts."
                                $venvReady = $false
                            }
                            else {
                                Write-Warn "CUDA-capable torch not detected in .venv-xtts. XTTS will be treated as not ready."
                            }
                        }
                        else {
                            Write-Pass "CUDA restored after repair pass"
                        }
                    }
                }
            }
            else {
                Write-Warn "Unable to import torch in .venv-xtts. Output: $torchCheck"
            }
        }
    }
}

$xttsScriptPath = Resolve-AppPath -PathValue (Get-ConfigValue -Config $config -Path @('audio', 'xtts', 'scriptPath')) -DefaultRelativePath 'tts-service/xtts_service.py'
$xttsVoicePath = Resolve-AppPath -PathValue (Get-ConfigValue -Config $config -Path @('audio', 'xtts', 'voicePath')) -DefaultRelativePath 'voices/brand'
$xttsBaseUrl = [string](Get-ConfigValue -Config $config -Path @('audio', 'xtts', 'baseUrl') -DefaultValue 'http://localhost:8020')
$xttsHealthPath = [string](Get-ConfigValue -Config $config -Path @('audio', 'xtts', 'healthPath') -DefaultValue '/health')

if ($null -ne $xttsScriptPath -and (Test-Path $xttsScriptPath)) {
    Write-Pass "Found XTTS service script: $xttsScriptPath"
}
else {
    Write-Fail "XTTS service script missing: $xttsScriptPath"
    $venvReady = $false
}

if ($null -ne $xttsVoicePath -and (Test-Path $xttsVoicePath)) {
    $wavFiles = @(Get-ChildItem -Path $xttsVoicePath -Filter '*.wav' -File -ErrorAction SilentlyContinue)
    if ($wavFiles.Count -gt 0) {
        Write-Pass "XTTS reference voice files found: $($wavFiles.Count)"
        if ($venvReady -and -not $NoSelfHeal) {
            $voiceProbeResult = Invoke-NativeCommandCapture -Command {
                & $venvPython -c "from pathlib import Path; import soundfile as sf; [sf.info(str(p)) for p in Path(r'$xttsVoicePath').glob('*.wav')]; print('ok')"
            }

            if ($voiceProbeResult.ExitCode -eq 0) {
                Write-Pass "XTTS reference voice files are readable"
            }
            else {
                Write-Fail "XTTS voice references were found but one or more .wav files are not readable"
                $venvReady = $false
            }
        }
    }
    else {
        Write-Fail "No .wav files found in XTTS voice path: $xttsVoicePath"
        $venvReady = $false
    }
}
else {
    Write-Fail "XTTS voice path missing: $xttsVoicePath"
    $venvReady = $false
}

$xttsHealthUrl = Join-Url -BaseUrl $xttsBaseUrl -Path $xttsHealthPath
if ($venvReady) {
    $healthResult = Test-XttsHealth -HealthUrl $xttsHealthUrl -TimeoutSeconds 5
    if (-not $healthResult.Healthy -and -not $NoSelfHeal) {
        Write-Warn "XTTS service not reachable at $xttsHealthUrl. Attempting auto-start."
        $started = Start-XttsService -PythonExe $venvPython -ScriptPath $xttsScriptPath -WorkingDirectory $PSScriptRoot
        if ($started) {
            Write-Info "XTTS service start requested. Waiting for health..."
            $healthTimeouts = @(20, 40, 60)
            foreach ($timeoutSec in $healthTimeouts) {
                $healthResult = Test-XttsHealth -HealthUrl $xttsHealthUrl -TimeoutSeconds $timeoutSec
                if ($healthResult.Healthy) {
                    break
                }
            }
        }
    }

    if ($healthResult.Healthy) {
        Write-Pass "XTTS service reachable: $xttsHealthUrl"
    }
    else {
        if ($StrictXtts.IsPresent -or $ttsProvider -eq 'xtts') {
            Write-Fail "XTTS service not reachable at $xttsHealthUrl after remediation attempts"
            $venvReady = $false
        }
        else {
            Write-Warn "XTTS service not reachable at $xttsHealthUrl"
        }
    }
}

# ──────────────────────────────────────────────────────────────────────────────
# 8. Provider-specific checks
# ──────────────────────────────────────────────────────────────────────────────
Write-Section "TTS Provider Checks"

if (-not $audioEnabled) {
    Write-Warn "Audio is disabled in config. Provider checks skipped."
}
else {
    switch ($ttsProvider) {
        'piper' {
            $piperExe = Resolve-AppPath -PathValue (Get-ConfigValue -Config $config -Path @('audio', 'piper', 'exePath'))
            $piperModel = Resolve-AppPath -PathValue (Get-ConfigValue -Config $config -Path @('audio', 'piper', 'modelPath'))

            if ($null -ne $piperExe -and (Test-Path $piperExe)) {
                $piperVersion = & $piperExe --version 2>&1
                if ($LASTEXITCODE -eq 0) {
                    $piperVersionLine = [string]($piperVersion | Select-Object -First 1)
                    Write-Pass "Piper executable found: $piperExe"
                    if (-not [string]::IsNullOrWhiteSpace($piperVersionLine)) {
                        Write-Host "       $piperVersionLine"
                    }
                }
                else {
                    Write-Fail "Piper executable exists but failed to run: $piperExe"
                }
            }
            else {
                Write-Fail "Piper executable not found: $piperExe"
            }

            if ($null -ne $piperModel -and (Test-Path $piperModel)) {
                Write-Pass "Piper model found: $piperModel"
                $piperModelMeta = "$piperModel.json"
                if (Test-Path $piperModelMeta) {
                    Write-Pass "Piper model metadata found: $piperModelMeta"
                }
                else {
                    Write-Fail "Piper model metadata missing: $piperModelMeta"
                }
            }
            else {
                Write-Fail "Piper model not found: $piperModel"
            }
        }
        'xtts' {
            if ($venvReady) {
                Write-Pass "XTTS selected and environment is ready"
            }
            else {
                Write-Fail "XTTS is selected but environment is not ready after remediation"
            }
        }
        'sapi' {
            Write-Pass "SAPI selected - no external runtime dependencies required."
        }
        default {
            Write-Fail "Unknown ttsProvider '$ttsProvider'. Expected one of: sapi, piper, xtts"
        }
    }
}

# ──────────────────────────────────────────────────────────────────────────────
# 9. Ollama checks
# ──────────────────────────────────────────────────────────────────────────────
Write-Section "Ollama"

if (-not $llmEnabled) {
    Write-Pass "LLM is disabled. Ollama checks skipped."
}
else {
    $ollamaBaseUrl = [string](Get-ConfigValue -Config $config -Path @('llm', 'ollama', 'baseUrl') -DefaultValue 'http://localhost:11434')
    $ollamaModel = [string](Get-ConfigValue -Config $config -Path @('llm', 'ollama', 'model') -DefaultValue '')
    $ollamaTagsUrl = Join-Url -BaseUrl $ollamaBaseUrl -Path '/api/tags'

    try {
        $tagsResult = Invoke-RestMethod -Uri $ollamaTagsUrl -Method Get -TimeoutSec 5 -ErrorAction Stop
        Write-Pass "Ollama service reachable: $ollamaBaseUrl"

        if ([string]::IsNullOrWhiteSpace($ollamaModel)) {
            Write-Warn "llm.ollama.model is empty in config."
        }
        else {
            $availableModels = @()
            $models = Get-ObjectValue -Object $tagsResult -Name 'models'
            if ($null -ne $models) {
                $availableModels = @($models | ForEach-Object { [string](Get-ObjectValue -Object $_ -Name 'name') })
            }

            if ($availableModels -contains $ollamaModel) {
                Write-Pass "Configured Ollama model is installed: $ollamaModel"
            }
            else {
                Write-Warn "Configured Ollama model not found locally: $ollamaModel"
            }
        }
    }
    catch {
        Write-Warn "Ollama is enabled but service is not reachable: $ollamaBaseUrl"
    }
}

# ──────────────────────────────────────────────────────────────────────────────
# 10. Build check
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
    Write-Warn "Skipping build check - .NET SDK not found"
}

# ──────────────────────────────────────────────────────────────────────────────
# 11. Optional: generate placeholder images
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
