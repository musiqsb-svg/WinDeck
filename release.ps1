# Builds the team release packages into .\release :
#   WinDeck-Setup-<version>.exe     (Inno Setup installer, per-user, no admin)
#   WinDeck-Portable-<version>.zip  (unzip and run; settings in WinDeckData next to the exe)
#   WinDeck-SHA256-<version>.txt    (hashes of both; upload it too - the in-app updater requires it)
# The version comes from src\AssemblyInfo.cs. Optional team default buttons: deploy\WinDeck.defaults.json
# Usage: powershell -ExecutionPolicy Bypass -File release.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$info = [IO.File]::ReadAllText((Join-Path $root 'src\AssemblyInfo.cs'))
if ($info -notmatch 'AssemblyVersion\("(\d+)\.(\d+)\.(\d+)') { throw 'AssemblyVersion not found in src\AssemblyInfo.cs' }
$version = "$($Matches[1]).$($Matches[2]).$($Matches[3])"

$release = Join-Path $root 'release'
$stage = Join-Path $root 'build\stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $release, $stage | Out-Null

$exe = Join-Path $root 'dist\WinDeck.exe'
$guides = Get-ChildItem (Join-Path $root 'deploy') -Filter '*.txt'
$defaults = Join-Path $root 'deploy\WinDeck.defaults.json'
if (Test-Path $defaults) { Write-Host "Including team defaults: deploy\WinDeck.defaults.json" }

# ---- portable zip ----
$portable = Join-Path $stage 'WinDeck'
New-Item -ItemType Directory -Force $portable, (Join-Path $portable 'WinDeckData') | Out-Null
Copy-Item $exe $portable
$guides | ForEach-Object { Copy-Item $_.FullName $portable }
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $portable 'LICENSE.txt')
if (Test-Path $defaults) { Copy-Item $defaults $portable }
$zip = Join-Path $release "WinDeck-Portable-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# Write entries by hand: ZipFile.CreateFromDirectory on Windows PowerShell 5.1 stores '\' separators,
# which some unzip tools treat as part of the file name. Use '/' and UTF-8 names (Korean guide file).
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$stream = [IO.File]::Open($zip, [IO.FileMode]::CreateNew)
$archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $false, [Text.Encoding]::UTF8)
try {
    $archive.CreateEntry('WinDeck/') | Out-Null
    Get-ChildItem $portable -Recurse | ForEach-Object {
        $name = 'WinDeck/' + $_.FullName.Substring($portable.Length + 1).Replace('\', '/')
        if ($_.PSIsContainer) { $archive.CreateEntry($name + '/') | Out-Null }
        else { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null }
    }
}
finally {
    $archive.Dispose()
    $stream.Dispose()
}

# ---- installer ----
$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found. Install: winget install JRSoftware.InnoSetup' }
& $iscc /Q "/DAppVersion=$version" (Join-Path $root 'installer\WinDeck.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compile failed.' }

# ---- SHA-256 list (the in-app updater installs a download only if its hash matches this list) ----
$sums = Join-Path $release "WinDeck-SHA256-$version.txt"
$lines = foreach ($name in @("WinDeck-Setup-$version.exe", "WinDeck-Portable-$version.zip")) {
    $hash = (Get-FileHash (Join-Path $release $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name"
}
[IO.File]::WriteAllText($sums, (($lines -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))

Write-Host ""
Write-Host "Release $version ready:"
Get-ChildItem $release -File | Where-Object { $_.Name -like "*$version*" } | ForEach-Object {
    Write-Host ("  {0}  ({1:N0} KB)" -f $_.Name, ($_.Length / 1KB))
}
