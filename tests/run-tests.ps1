# Builds tests\SmokeTest.exe against dist\WinDeck.exe and runs it.
# Usage: powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1 <outDir> [-TypeTest] [-E2E]
param([Parameter(Mandatory = $true)][string]$OutDir, [switch]$TypeTest, [switch]$E2E, [switch]$Actions)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$build = Join-Path $root 'build\tests'
New-Item -ItemType Directory -Force $build, $OutDir | Out-Null

Copy-Item (Join-Path $root 'dist\WinDeck.exe') $build -Force
$exe = Join-Path $build 'SmokeTest.exe'
& $csc /nologo /codepage:65001 /target:exe "/out:$exe" "/r:$(Join-Path $build 'WinDeck.exe')" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    (Join-Path $root 'tests\SmokeTest.cs')
if ($LASTEXITCODE -ne 0) { throw 'SmokeTest compile failed.' }

$testArgs = @($OutDir)
if ($TypeTest) { $testArgs += '--input' }
if ($E2E) { $testArgs += '--e2e' }
if ($Actions) { $testArgs += '--actions' }
[Console]::OutputEncoding = [Text.Encoding]::UTF8
& $exe @testArgs
exit $LASTEXITCODE
