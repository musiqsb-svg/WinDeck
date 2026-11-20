# Builds src\build\tests\SmokeTest.exe against app\WinDeck.exe and runs it.
# Usage: powershell -ExecutionPolicy Bypass -File src\tests\run-tests.ps1 <outDir> [-TypeTest] [-E2E] [-Actions] [-Process]
#   (run src\build.ps1 first: the tests use app\WinDeck.exe and the two setup programs at the top of the project folder)
#   Setup programs are always tested: install / reinstall / uninstall in an isolated folder and test registry keys.
#   -Process  starts real WinDeck.exe copies (separate test instance name, settings in <outDir>) to check
#             'WinDeck.exe --exit', the update window, an update through a fake 99.0.0 release (setup + portable zip),
#             and updates from the real 1.3.0 (src\release\WinDeck-Portable-1.3.0.zip, if present).
# This file must stay UTF-8 with BOM.
param([Parameter(Mandatory = $true)][string]$OutDir, [switch]$TypeTest, [switch]$E2E, [switch]$Actions, [switch]$Process)
$ErrorActionPreference = 'Stop'
$src = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$top = Split-Path -Parent $src
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$build = Join-Path $src 'build\tests'
# The tests delete and recreate fixed-name folders (cfg, setup_root, ...) under the output folder, so they only run in
# an empty folder or one made by an earlier run (marker file). This stops a wrong path from losing someone's work.
$marker = '.windeck-test-output'
if ((Test-Path -LiteralPath $OutDir) -and @(Get-ChildItem -LiteralPath $OutDir -Force).Count -gt 0 -and
    -not (Test-Path -LiteralPath (Join-Path $OutDir $marker))) {
    throw "'$OutDir' is not empty and was not made by these tests (no $marker). Pass an empty folder."
}
New-Item -ItemType Directory -Force $build, $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path
[IO.File]::WriteAllText((Join-Path $OutDir $marker), "WinDeck test output folder. The tests delete and recreate folders in here.`r`n")
foreach ($need in @('app\WinDeck.exe', 'WinDeck 설치.exe', 'WinDeck 제거.exe')) {
    if (-not (Test-Path (Join-Path $top $need))) { throw "Missing $need - run src\build.ps1 first." }
}

Copy-Item (Join-Path $top 'app\WinDeck.exe') $build -Force
$exe = Join-Path $build 'SmokeTest.exe'
& $csc /nologo /codepage:65001 /target:exe "/out:$exe" "/r:$(Join-Path $build 'WinDeck.exe')" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    (Get-ChildItem (Join-Path $src 'tests') -Filter 'SmokeTest*.cs' | ForEach-Object { $_.FullName })
if ($LASTEXITCODE -ne 0) { throw 'SmokeTest compile failed.' }

$testArgs = @($OutDir, '--package', $top)
if ($TypeTest) { $testArgs += '--input' }
if ($E2E) { $testArgs += '--e2e' }
if ($Actions) { $testArgs += '--actions' }

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
function New-Zip([string]$zip, [object[]]$entries) {
    if (Test-Path $zip) { Remove-Item $zip -Force }
    $stream = [IO.File]::Open($zip, [IO.FileMode]::CreateNew)
    $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $false, [Text.Encoding]::UTF8)
    try { foreach ($e in $entries) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $e[0], $e[1]) | Out-Null } }
    finally { $archive.Dispose(); $stream.Dispose() }
}

if ($Process) {
    # Fake release 99.0.0: WinDeck.exe built from src with a different version, its package, the setup with the
    # package inside, a portable zip (for 1.3 portable copies), SHA-256 lists and release.json (file:// links)
    $fake = Join-Path $OutDir 'fake-release'
    if (Test-Path $fake) { Remove-Item $fake -Recurse -Force }
    $srcCopy = Join-Path $fake 'src'
    $pkg = Join-Path $fake 'pkg'
    $pkgApp = Join-Path $pkg 'app'
    New-Item -ItemType Directory -Force $srcCopy, $pkgApp | Out-Null
    Copy-Item (Join-Path $src 'WinDeck\*.cs') $srcCopy
    $ai = Join-Path $srcCopy 'AssemblyInfo.cs'
    $text = [IO.File]::ReadAllText($ai) -replace 'Version\("\d+\.\d+\.\d+\.\d+"\)', 'Version("99.0.0.0")'
    [IO.File]::WriteAllText($ai, $text, (New-Object Text.UTF8Encoding($true)))
    $resources = @()
    $iconDir = Join-Path $src 'assets\icons'
    $resources += "/resource:$(Join-Path $iconDir 'manifest.tsv'),WinDeck.Icons.manifest.tsv"
    Get-ChildItem $iconDir -Filter *.png | ForEach-Object { $resources += "/resource:$($_.FullName),WinDeck.Icons.$($_.Name)" }
    & $csc /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu "/out:$(Join-Path $pkgApp 'WinDeck.exe')" `
        "/win32icon:$(Join-Path $src 'build\app.ico')" "/win32manifest:$(Join-Path $src 'WinDeck\app.manifest')" `
        /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll `
        /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
        $resources (Get-ChildItem $srcCopy -Filter *.cs | ForEach-Object { $_.FullName })
    if ($LASTEXITCODE -ne 0) { throw 'Fake 99.0.0 build failed.' }
    [IO.File]::WriteAllText((Join-Path $pkgApp 'WinDeck 사용 안내.txt'), 'fake-guide-99', (New-Object Text.UTF8Encoding($true)))
    $setupScript = Join-Path $src 'build-setup.ps1'
    & $setupScript -Kind Uninstaller -Version 99.0.0 -Out (Join-Path $pkg 'WinDeck 제거.exe')
    $setup = Join-Path $fake 'WinDeck-Setup-99.0.0.exe'
    & $setupScript -Kind Bridge -Version 99.0.0 -Out $setup -PackageDir $pkg

    $portable = Join-Path $fake 'WinDeck-Portable-99.0.0.zip'
    New-Zip $portable @(@((Join-Path $pkgApp 'WinDeck.exe'), 'WinDeck/WinDeck.exe'), @((Join-Path $pkgApp 'WinDeck 사용 안내.txt'), 'WinDeck/WinDeck 사용 안내.txt'))

    $utf8 = New-Object Text.UTF8Encoding($false)
    $sumLines = foreach ($f in @($setup, $portable)) { (Get-FileHash $f -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path -Leaf $f) }
    [IO.File]::WriteAllText((Join-Path $fake 'WinDeck-SHA256-99.0.0.txt'), (($sumLines -join "`n") + "`n"), $utf8)
    $badLines = foreach ($f in @($setup, $portable)) { ('0' * 64) + '  ' + (Split-Path -Leaf $f) }
    [IO.File]::WriteAllText((Join-Path $fake 'bad-SHA256.txt'), (($badLines -join "`n") + "`n"), $utf8)
    foreach ($pair in @(@('release.json', 'WinDeck-SHA256-99.0.0.txt'), @('release-bad.json', 'bad-SHA256.txt'))) {
        $release = [ordered]@{
            tag_name = 'v99.0.0'
            html_url = 'https://github.com/musiqsb-svg/WinDeck/releases'
            body     = "점검용 가짜 릴리스`n- 설정 되돌아감 방지`n- 자동 업데이트"
            assets   = @(
                [ordered]@{ name = 'WinDeck-Setup-99.0.0.exe'; browser_download_url = ([Uri]$setup).AbsoluteUri },
                [ordered]@{ name = 'WinDeck-Portable-99.0.0.zip'; browser_download_url = ([Uri]$portable).AbsoluteUri },
                [ordered]@{ name = 'WinDeck-SHA256-99.0.0.txt'; browser_download_url = ([Uri](Join-Path $fake $pair[1])).AbsoluteUri }
            )
        }
        [IO.File]::WriteAllText((Join-Path $fake $pair[0]), ($release | ConvertTo-Json -Depth 5), $utf8)
    }
    $testArgs += @('--process', $fake)
    $old = Join-Path $src 'release\WinDeck-Portable-1.3.0.zip'
    if (Test-Path $old) { $testArgs += @('--old13', $old) }
    else { Write-Host "SKIP  1.3.0 update tests (no $old)" }
}

[Console]::OutputEncoding = [Text.Encoding]::UTF8
& $exe @testArgs
exit $LASTEXITCODE
