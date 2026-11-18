# Compiles a WinDeck setup program with the C# compiler that ships with Windows (.NET Framework 4.8).
#   -Kind Installer    'WinDeck 설치.exe'   installs the app\ folder that sits next to it
#   -Kind Uninstaller  'WinDeck 제거.exe'
#   -Kind Bridge       'WinDeck-Setup-x.y.z.exe'  carries app\ + 'WinDeck 제거.exe' inside (release attachment
#                      for the in-app auto-update of 1.3 and later; same command-line switches as the old Inno setup)
# Called by build.ps1, release.ps1 and tests\run-tests.ps1. This file must stay UTF-8 with BOM (Korean names).
param(
    [Parameter(Mandatory = $true)][ValidateSet('Installer', 'Uninstaller', 'Bridge')][string]$Kind,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$PackageDir,
    [string]$SrcRoot,
    [string]$Icon
)
$ErrorActionPreference = 'Stop'
if (-not $SrcRoot) { $SrcRoot = Split-Path -Parent $MyInvocation.MyCommand.Path }
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe (.NET Framework 4.x) not found.' }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.4.0 (got '$Version')." }

$setupDir = Join-Path $SrcRoot 'Setup'
$appSrc = Join-Path $SrcRoot 'WinDeck'
$work = Join-Path ([IO.Path]::GetTempPath()) ('WinDeck-setupbuild-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $work | Out-Null
try {
    $title = if ($Kind -eq 'Uninstaller') { 'WinDeck 제거' } else { 'WinDeck 설치' }
    $verFile = Join-Path $work 'SetupVersion.cs'
    $lines = @(
        'using System.Reflection;',
        "[assembly: AssemblyTitle(""$title"")]",
        "[assembly: AssemblyDescription(""$title"")]",
        '[assembly: AssemblyProduct("WinDeck")]',
        '[assembly: AssemblyCopyright("Copyright © 2026 musiqsb-svg (MIT License)")]',
        "[assembly: AssemblyVersion(""$Version.0"")]",
        "[assembly: AssemblyFileVersion(""$Version.0"")]"
    )
    [IO.File]::WriteAllText($verFile, ($lines -join "`r`n"), (New-Object Text.UTF8Encoding($true)))

    # AppInstance.cs / InstallInfo.cs: the same names and paths the app uses (single instance, exit signal, install folder)
    $sources = @((Join-Path $setupDir 'SetupCore.cs'), (Join-Path $appSrc 'AppInstance.cs'), (Join-Path $appSrc 'InstallInfo.cs'), $verFile)
    if ($Kind -eq 'Uninstaller') { $sources += Join-Path $setupDir 'UninstallerProgram.cs' }
    else { $sources += (Join-Path $setupDir 'InstallerProgram.cs'), (Join-Path $setupDir 'InstallForm.cs') }

    $extra = @()
    if ($Kind -eq 'Bridge') {
        if (-not $PackageDir) { throw '-PackageDir is required for -Kind Bridge.' }
        $appDir = Join-Path $PackageDir 'app'
        $uninstaller = Join-Path $PackageDir 'WinDeck 제거.exe'
        foreach ($need in @((Join-Path $appDir 'WinDeck.exe'), $uninstaller)) { if (-not (Test-Path $need)) { throw "Missing for the bridge setup: $need" } }
        # payload.zip: 'app/...' + 'WinDeck 제거.exe' with '/' separators and UTF-8 names
        Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
        $payload = Join-Path $work 'payload.zip'
        $stream = [IO.File]::Open($payload, [IO.FileMode]::CreateNew)
        $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $false, [Text.Encoding]::UTF8)
        try {
            Get-ChildItem $appDir -Recurse -File | ForEach-Object {
                $name = 'app/' + $_.FullName.Substring($appDir.Length + 1).Replace('\', '/')
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $uninstaller, 'WinDeck 제거.exe', [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
        finally {
            $archive.Dispose()
            $stream.Dispose()
        }
        $extra += '/define:EMBEDDED', "/resource:$payload,payload.zip"
    }
    if ($Icon) { $extra += "/win32icon:$Icon" }

    New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null
    # The manifest (asInvoker) also stops Windows from asking for admin rights because the name contains 'Setup'.
    & $csc /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu "/out:$Out" `
        "/win32manifest:$(Join-Path $appSrc 'app.manifest')" `
        /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
        $extra $sources
    if ($LASTEXITCODE -ne 0) { throw "$Kind compile failed." }
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
