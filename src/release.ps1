# Builds the release attachments into src\release :
#   WinDeck-<version>.zip           download package: 'WinDeck 설치.exe', 'WinDeck 제거.exe', app\  (for people)
#   WinDeck-Setup-<version>.exe     setup with the package inside  (for the in-app auto-update of 1.3 installs and 1.4+)
#   WinDeck-Portable-<version>.zip  WinDeck/WinDeck.exe + guide  (only so that 1.3 portable copies can auto-update;
#                                   the new exe then moves their settings to %APPDATA%\WinDeck and offers to install)
#   WinDeck-SHA256-<version>.txt    hashes of all three - upload it too, the in-app updater requires it
# The version comes from src\WinDeck\AssemblyInfo.cs. Optional team default buttons: src\deploy\WinDeck.defaults.json
# Usage: double-click src\release.bat. This file must stay UTF-8 with BOM (Korean file names).
$ErrorActionPreference = 'Stop'
$src = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $src

& (Join-Path $src 'build.ps1')

$info = [IO.File]::ReadAllText((Join-Path $src 'WinDeck\AssemblyInfo.cs'))
if ($info -notmatch 'AssemblyVersion\("(\d+)\.(\d+)\.(\d+)') { throw 'AssemblyVersion not found in src\WinDeck\AssemblyInfo.cs' }
$version = "$($Matches[1]).$($Matches[2]).$($Matches[3])"

$release = Join-Path $src 'release'
New-Item -ItemType Directory -Force $release | Out-Null
$installer = Join-Path $root 'WinDeck 설치.exe'
$uninstaller = Join-Path $root 'WinDeck 제거.exe'
$appDir = Join-Path $root 'app'

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
# Write entries by hand: ZipFile.CreateFromDirectory on Windows PowerShell 5.1 stores '\' separators,
# which some unzip tools treat as part of the file name. Use '/' and UTF-8 names (Korean file names).
function New-Zip([string]$zip, [object[]]$entries) {
    if (Test-Path $zip) { Remove-Item $zip -Force }
    $stream = [IO.File]::Open($zip, [IO.FileMode]::CreateNew)
    $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $false, [Text.Encoding]::UTF8)
    try {
        foreach ($e in $entries) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $e[0], $e[1], [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
        $stream.Dispose()
    }
}

# ---- download package: the zip's top holds only the two setup programs and app\ ----
$entries = @(, @($installer, 'WinDeck 설치.exe'))
$entries += , @($uninstaller, 'WinDeck 제거.exe')
Get-ChildItem $appDir -Recurse -File | ForEach-Object { $entries += , @($_.FullName, ('app/' + $_.FullName.Substring($appDir.Length + 1).Replace('\', '/'))) }
$package = Join-Path $release "WinDeck-$version.zip"
New-Zip $package $entries

# ---- setup for the auto-update (carries the package inside) ----
$bridge = Join-Path $release "WinDeck-Setup-$version.exe"
& (Join-Path $src 'build-setup.ps1') -Kind Bridge -Version $version -Out $bridge -PackageDir $root -Icon (Join-Path $src 'build\app.ico')

# ---- portable zip for 1.3 portable copies (their updater takes WinDeck/WinDeck.exe and WinDeck/*.txt only) ----
$entries = @(, @((Join-Path $appDir 'WinDeck.exe'), 'WinDeck/WinDeck.exe'))
Get-ChildItem $appDir -Filter '*.txt' | ForEach-Object { $entries += , @($_.FullName, ('WinDeck/' + $_.Name)) }
$portable = Join-Path $release "WinDeck-Portable-$version.zip"
New-Zip $portable $entries

# ---- SHA-256 list (the in-app updater installs a download only if its hash matches this list) ----
$sums = Join-Path $release "WinDeck-SHA256-$version.txt"
$lines = foreach ($name in @("WinDeck-$version.zip", "WinDeck-Setup-$version.exe", "WinDeck-Portable-$version.zip")) {
    $hash = (Get-FileHash (Join-Path $release $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name"
}
[IO.File]::WriteAllText($sums, (($lines -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))

Write-Host ""
Write-Host "Release $version ready in src\release:"
Get-ChildItem $release -File | Where-Object { $_.Name -like "*-$version.*" } | ForEach-Object {
    Write-Host ("  {0}  ({1:N0} KB)" -f $_.Name, ($_.Length / 1KB))
}
