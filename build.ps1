# WinDeck build script (uses the C# compiler that ships with Windows / .NET Framework 4.8)
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe (.NET Framework 4.x) not found.' }

$build = Join-Path $root 'build'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $build, $dist | Out-Null

# 1) Generate the application icon from the same drawing code the app uses.
$iconGen = Join-Path $build 'IconGen.exe'
$ico = Join-Path $build 'app.ico'
& $csc /nologo /codepage:65001 /target:exe "/out:$iconGen" /r:System.Drawing.dll `
    (Join-Path $root 'tools\IconGen.cs') (Join-Path $root 'src\AppIcon.cs')
if ($LASTEXITCODE -ne 0) { throw 'IconGen compile failed.' }
& $iconGen $ico
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }

# 2) Compile WinDeck.exe (close a running copy of dist\WinDeck.exe first, restart it afterwards)
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
# Built-in icon library (assets\icons\*.png + manifest.tsv) is embedded as WinDeck.Icons.* resources
$resources = @()
$iconDir = Join-Path $root 'assets\icons'
if (Test-Path (Join-Path $iconDir 'manifest.tsv')) {
    $resources += "/resource:$(Join-Path $iconDir 'manifest.tsv'),WinDeck.Icons.manifest.tsv"
    Get-ChildItem $iconDir -Filter *.png | ForEach-Object { $resources += "/resource:$($_.FullName),WinDeck.Icons.$($_.Name)" }
}
$exe = Join-Path $dist 'WinDeck.exe'
$running = @(Get-Process WinDeck -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
if ($running.Count -gt 0) {
    Write-Host 'Closing running dist\WinDeck.exe for rebuild...'
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}
& $csc /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu `
    "/out:$exe" "/win32icon:$ico" "/win32manifest:$(Join-Path $root 'app.manifest')" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll `
    $resources $sources
$compileExit = $LASTEXITCODE
if ($running.Count -gt 0) { Start-Process $exe }
if ($compileExit -ne 0) { throw 'WinDeck compile failed.' }

Write-Host "Build OK: $exe"
