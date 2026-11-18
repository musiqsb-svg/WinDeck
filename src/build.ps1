# WinDeck build script (uses the C# compiler that ships with Windows / .NET Framework 4.8)
# Output at the top of the project folder (not in git; the same shape as the download package):
#   WinDeck 설치.exe     WinDeck 제거.exe     app\WinDeck.exe (+ LICENSE.txt, guide, team defaults)
# Intermediate files go to src\build. Usage: double-click src\build.bat
# This file must stay UTF-8 with BOM (Korean file names).
$ErrorActionPreference = 'Stop'
$src = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $src

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe (.NET Framework 4.x) not found.' }

$build = Join-Path $src 'build'
$appOut = Join-Path $root 'app'
New-Item -ItemType Directory -Force $build, $appOut | Out-Null

$info = [IO.File]::ReadAllText((Join-Path $src 'WinDeck\AssemblyInfo.cs'))
if ($info -notmatch 'AssemblyVersion\("(\d+)\.(\d+)\.(\d+)') { throw 'AssemblyVersion not found in src\WinDeck\AssemblyInfo.cs' }
$version = "$($Matches[1]).$($Matches[2]).$($Matches[3])"

# 1) Generate the application icon from the same drawing code the app uses.
$iconGen = Join-Path $build 'IconGen.exe'
$ico = Join-Path $build 'app.ico'
& $csc /nologo /codepage:65001 /target:exe "/out:$iconGen" /r:System.Drawing.dll `
    (Join-Path $src 'tools\IconGen.cs') (Join-Path $src 'WinDeck\AppIcon.cs')
if ($LASTEXITCODE -ne 0) { throw 'IconGen compile failed.' }
& $iconGen $ico
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }

# 2) Compile app\WinDeck.exe (close a running copy of app\WinDeck.exe first, restart it afterwards)
$sources = Get-ChildItem (Join-Path $src 'WinDeck') -Filter *.cs | ForEach-Object { $_.FullName }
# Built-in icon library (assets\icons\*.png + manifest.tsv) is embedded as WinDeck.Icons.* resources
$resources = @()
$iconDir = Join-Path $src 'assets\icons'
if (Test-Path (Join-Path $iconDir 'manifest.tsv')) {
    $resources += "/resource:$(Join-Path $iconDir 'manifest.tsv'),WinDeck.Icons.manifest.tsv"
    Get-ChildItem $iconDir -Filter *.png | ForEach-Object { $resources += "/resource:$($_.FullName),WinDeck.Icons.$($_.Name)" }
}
# Copyright notice kept inside the exe (full text, Korean + English)
$resources += "/resource:$(Join-Path $src 'NOTICE.md'),WinDeck.NOTICE.md"
$exe = Join-Path $appOut 'WinDeck.exe'
$running = @(Get-Process WinDeck -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
if ($running.Count -gt 0) {
    Write-Host 'Closing running app\WinDeck.exe for rebuild (saving settings first)...'
    # 1.3+ : the exit signal makes WinDeck save its settings and quit by itself. Force-kill only if it does not.
    try { ([Threading.EventWaitHandle]::OpenExisting('Local\WinDeck.Exit')).Set() | Out-Null } catch { }
    $running | Wait-Process -Timeout 6 -ErrorAction SilentlyContinue
    $still = @($running | Where-Object { -not $_.HasExited })
    if ($still.Count -gt 0) { $still | Stop-Process -Force }
    Start-Sleep -Milliseconds 300
}
# Start from an empty app\ so files removed from the package do not linger.
Get-ChildItem $appOut -Force | Remove-Item -Recurse -Force
& $csc /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu `
    "/out:$exe" "/win32icon:$ico" "/win32manifest:$(Join-Path $src 'WinDeck\app.manifest')" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    $resources $sources
$compileExit = $LASTEXITCODE
if ($compileExit -ne 0) { throw 'WinDeck compile failed.' }

# 3) Files that ship next to the exe: license, guide, optional team default buttons
Copy-Item (Join-Path $src 'LICENSE') (Join-Path $appOut 'LICENSE.txt')
Copy-Item (Join-Path $src 'NOTICE.md') (Join-Path $appOut 'NOTICE.txt')
Get-ChildItem (Join-Path $src 'deploy') -Filter '*.txt' | ForEach-Object { Copy-Item $_.FullName $appOut }
$defaults = Join-Path $src 'deploy\WinDeck.defaults.json'
if (Test-Path $defaults) {
    Write-Host 'Including team defaults: src\deploy\WinDeck.defaults.json'
    Copy-Item $defaults $appOut
}

# 4) Setup programs at the top of the project folder
$setup = Join-Path $src 'build-setup.ps1'
& $setup -Kind Installer -Version $version -Out (Join-Path $root 'WinDeck 설치.exe') -Icon $ico
& $setup -Kind Uninstaller -Version $version -Out (Join-Path $root 'WinDeck 제거.exe') -Icon $ico

if ($running.Count -gt 0) { Start-Process $exe }
Write-Host "Build OK ($version): $root"
Write-Host '  WinDeck 설치.exe, WinDeck 제거.exe, app\'
