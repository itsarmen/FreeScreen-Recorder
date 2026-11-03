<#
.SYNOPSIS
    Builds a distributable FreeScreen Recorder .exe and Windows installer.

.DESCRIPTION
    Publishes the WinUI 3 app as a self-contained build (no .NET install required),
    zips the portable output, and - when Inno Setup is installed - compiles a
    single Setup .exe. Optional Authenticode signing is supported.

.PARAMETER Runtime
    Target runtime: win-x64 (default), win-x86, or win-arm64.

.PARAMETER Configuration
    Build configuration. Release is recommended for distribution.

.PARAMETER Version
    Product version written into the installer and output file names.
    Defaults to the first <Version> in the project, or 1.0.0.

.PARAMETER Publisher
    Company/publisher name shown by the installer and file properties.

.PARAMETER ProductUrl
    Product URL shown by the installer.

.PARAMETER FfmpegPath
    Optional path to ffmpeg.exe to bundle next to the app.

.PARAMETER BundleFfmpeg
    Resolve ffmpeg.exe from PATH and bundle it.

.PARAMETER SkipArchive
    Do not create the portable .zip.

.PARAMETER SkipInstaller
    Do not build the Inno Setup installer.

.PARAMETER OutputRoot
    Output folder. Defaults to .\dist.

.PARAMETER SignCertificateThumbprint
    SHA1 thumbprint of an Authenticode certificate in the current user's store.

.PARAMETER TimestampUrl
    RFC 3161 timestamp server used while signing.

.EXAMPLE
    .\build.ps1

.EXAMPLE
    .\build.ps1 -Runtime win-arm64 -Version 1.2.0 -Publisher "Acme Software"

.EXAMPLE
    .\build.ps1 -BundleFfmpeg -SignCertificateThumbprint 0123456789ABCDEF0123456789ABCDEF01234567
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Version,
    [string]$Publisher = 'AppPublisher',
    [string]$ProductUrl = 'https://example.com',

    [string]$FfmpegPath,
    [switch]$BundleFfmpeg,

    [switch]$SkipArchive,
    [switch]$SkipInstaller,

    [string]$OutputRoot,

    [string]$SignCertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = $PSScriptRoot
$Project = Join-Path $Root 'FreeScreenRecorder.csproj'
$InstallerScript = Join-Path $Root 'FreeScreenRecorder.iss'
$IconPath = Join-Path $Root 'Assets\AppIcon.ico'

if (-not (Test-Path -LiteralPath $Project)) {
    throw "Project not found: $Project"
}

if (-not $OutputRoot) {
    $OutputRoot = Join-Path $Root 'dist'
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)

function Resolve-DotNet {
    $candidates = @()
    if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
    if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} 'dotnet\dotnet.exe' }
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }
    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    throw 'dotnet.exe was not found. Install the .NET 8 SDK.'
}

function Resolve-Ffmpeg {
    if ($FfmpegPath) {
        if (-not (Test-Path -LiteralPath $FfmpegPath)) {
            throw "ffmpeg.exe not found: $FfmpegPath"
        }
        return (Resolve-Path -LiteralPath $FfmpegPath).Path
    }

    if ($BundleFfmpeg) {
        $command = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
        if (-not $command) {
            throw 'BundleFfmpeg was set but ffmpeg.exe is not on PATH.'
        }
        return $command.Source
    }

    return $null
}

function Resolve-InnoSetup {
    $candidates = @()
    if (${env:ProgramFiles(x86)}) {
        $candidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
        $candidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 5\ISCC.exe'
    }
    if ($env:ProgramFiles) {
        $candidates += Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'
        $candidates += Join-Path $env:ProgramFiles 'Inno Setup 5\ISCC.exe'
    }
    if ($env:LOCALAPPDATA) {
        $candidates += Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
        $candidates += Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 5\ISCC.exe'
    }
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    return $null
}

function Resolve-SignTool {
    $kitsRoot = $null
    if (${env:ProgramFiles(x86)}) {
        $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    }
    if ($kitsRoot -and (Test-Path -LiteralPath $kitsRoot)) {
        $versions = Get-ChildItem -LiteralPath $kitsRoot -Directory |
            Sort-Object Name -Descending
        foreach ($version in $versions) {
            foreach ($arch in @('x64', 'x86', 'arm64')) {
                $candidate = Join-Path $version.FullName "$arch\signtool.exe"
                if (Test-Path -LiteralPath $candidate) {
                    return $candidate
                }
            }
        }
    }
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    return $null
}

function Get-ProjectVersion {
    if ($Version) { return $Version }
    $text = Get-Content -LiteralPath $Project -Raw
    $match = [regex]::Match($text, '<Version>\s*([^<]+?)\s*</Version>')
    if ($match.Success) { return $match.Groups[1].Value }
    return '1.0.0'
}

function Sign-File {
    param([string]$Path)

    if (-not $SignCertificateThumbprint) { return }
    $signTool = Resolve-SignTool
    if (-not $signTool) {
        Write-Warning "signtool.exe was not found; skipping signing of $Path"
        return
    }

    Write-Host "Signing $(Split-Path -Leaf $Path)..." -ForegroundColor Cyan
    & $signTool sign /sha1 $SignCertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $Path
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed for $Path (exit code $LASTEXITCODE)"
    }
}

$dotnet = Resolve-DotNet
$ffmpeg = Resolve-Ffmpeg
$Version = Get-ProjectVersion

if ($Version -notmatch '^\d+(\.\d+){1,3}$') {
    throw "Version must be numeric dotted, e.g. 1.0.0 (got '$Version')."
}

$platform = switch ($Runtime) {
    'win-x64' { 'x64' }
    'win-x86' { 'x86' }
    'win-arm64' { 'ARM64' }
}

$innoArch = switch ($Runtime) {
    'win-x64' { 'x64compatible' }
    'win-x86' { 'x86compatible' }
    'win-arm64' { 'arm64' }
}

$baseName = "FreeScreenRecorder-$Version-$Runtime"
$publishDir = Join-Path $OutputRoot $baseName
$zipPath = Join-Path $OutputRoot "$baseName.zip"
$installerPath = Join-Path $OutputRoot "$baseName-Setup.exe"

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

Write-Host "Publishing $Configuration / $Runtime (self-contained)..." -ForegroundColor Cyan
$publishArgs = @(
    'publish', $Project,
    '-c', $Configuration,
    "-p:Platform=$platform",
    '-r', $Runtime,
    '--self-contained', 'true',
    '-p:PublishSingleFile=false',
    '-p:PublishReadyToRun=false',
    '-p:WindowsAppSDKSelfContained=true',
    '-p:WindowsPackageType=None',
    '-o', $publishDir
)
& $dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$appExe = Join-Path $publishDir 'FreeScreenRecorder.exe'
if (-not (Test-Path -LiteralPath $appExe)) {
    throw "Published executable was not produced: $appExe"
}

$license = Join-Path $Root 'LICENSE'
if (Test-Path -LiteralPath $license) {
    Copy-Item -LiteralPath $license -Destination $publishDir -Force
}

if ($ffmpeg) {
    Write-Host "Bundling ffmpeg: $ffmpeg" -ForegroundColor Cyan
    Copy-Item -LiteralPath $ffmpeg -Destination (Join-Path $publishDir 'ffmpeg.exe') -Force
}

Sign-File -Path $appExe

if (-not $SkipArchive) {
    Write-Host "Creating portable archive..." -ForegroundColor Cyan
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $publishDir,
        $zipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $true
    )
}

if (-not $SkipInstaller) {
    $iscc = Resolve-InnoSetup
    if (-not $iscc) {
        Write-Warning 'Inno Setup 6 was not found. Portable build created, installer skipped.'
        Write-Warning 'Install it from https://jrsoftware.org/isdl.php and run this script again.'
    }
    elseif (-not (Test-Path -LiteralPath $InstallerScript)) {
        Write-Warning "Installer script not found: $InstallerScript"
    }
    else {
        Write-Host 'Compiling Windows installer...' -ForegroundColor Cyan
        $innoArgs = @(
            "/DAppVersion=$Version",
            "/DAppPublisher=$Publisher",
            "/DAppURL=$ProductUrl",
            "/DSourceDir=$publishDir",
            "/DOutputDir=$OutputRoot",
            "/DRuntime=$Runtime",
            "/DWinArch=$innoArch",
            "/DSetupIcon=$IconPath",
            $InstallerScript
        )
        if ($Runtime -eq 'win-x64') {
            $innoArgs = @('/DInno64BitMode') + $innoArgs
        }
        & $iscc @innoArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Inno Setup failed with exit code $LASTEXITCODE"
        }
        if (Test-Path -LiteralPath $installerPath) {
            Sign-File -Path $installerPath
        }
        else {
            Write-Warning "Expected installer was not produced: $installerPath"
        }
    }
}

Write-Host ''
Write-Host 'Build outputs:' -ForegroundColor Green
Write-Host "  App folder : $publishDir"
if (-not $SkipArchive) { Write-Host "  Portable   : $zipPath" }
if ((Test-Path -LiteralPath $installerPath)) { Write-Host "  Installer  : $installerPath" }
if ($ffmpeg) { Write-Host "  ffmpeg     : bundled" }
