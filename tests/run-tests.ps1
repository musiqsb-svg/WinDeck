# Builds tests\SmokeTest.exe against dist\WinDeck.exe and runs it.
# Usage: powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1 <outDir> [-TypeTest] [-E2E] [-Actions] [-Process]
#   -Process  starts real WinDeck.exe copies (separate test instance name, settings in <outDir>) to check
#             'WinDeck.exe --exit', the update window and a full portable update to a fake 99.0.0 release.
param([Parameter(Mandatory = $true)][string]$OutDir, [switch]$TypeTest, [switch]$E2E, [switch]$Actions, [switch]$Process)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$build = Join-Path $root 'build\tests'
New-Item -ItemType Directory -Force $build, $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

Copy-Item (Join-Path $root 'dist\WinDeck.exe') $build -Force
$exe = Join-Path $build 'SmokeTest.exe'
& $csc /nologo /codepage:65001 /target:exe "/out:$exe" "/r:$(Join-Path $build 'WinDeck.exe')" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    (Join-Path $root 'tests\SmokeTest.cs') (Join-Path $root 'tests\SmokeTest.V13.cs')
if ($LASTEXITCODE -ne 0) { throw 'SmokeTest compile failed.' }

$testArgs = @($OutDir)
if ($TypeTest) { $testArgs += '--input' }
if ($E2E) { $testArgs += '--e2e' }
if ($Actions) { $testArgs += '--actions' }

if ($Process) {
    # Fake release 99.0.0: WinDeck.exe built from src with a different version + portable zip + SHA-256 list + release.json (file:// links)
    $fake = Join-Path $OutDir 'fake-release'
    if (Test-Path $fake) { Remove-Item $fake -Recurse -Force }
    $srcCopy = Join-Path $fake 'src'
    $stage = Join-Path $fake 'stage\WinDeck'
    New-Item -ItemType Directory -Force $srcCopy, $stage, (Join-Path $stage 'WinDeckData') | Out-Null
    Copy-Item (Join-Path $root 'src\*.cs') $srcCopy
    $ai = Join-Path $srcCopy 'AssemblyInfo.cs'
    $text = [IO.File]::ReadAllText($ai) -replace 'Version\("\d+\.\d+\.\d+\.\d+"\)', 'Version("99.0.0.0")'
    [IO.File]::WriteAllText($ai, $text, (New-Object Text.UTF8Encoding($true)))
    $resources = @()
    $iconDir = Join-Path $root 'assets\icons'
    $resources += "/resource:$(Join-Path $iconDir 'manifest.tsv'),WinDeck.Icons.manifest.tsv"
    Get-ChildItem $iconDir -Filter *.png | ForEach-Object { $resources += "/resource:$($_.FullName),WinDeck.Icons.$($_.Name)" }
    & $csc /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu "/out:$(Join-Path $stage 'WinDeck.exe')" `
        "/win32icon:$(Join-Path $root 'build\app.ico')" "/win32manifest:$(Join-Path $root 'app.manifest')" `
        /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll `
        /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
        $resources (Get-ChildItem $srcCopy -Filter *.cs | ForEach-Object { $_.FullName })
    if ($LASTEXITCODE -ne 0) { throw 'Fake 99.0.0 build failed.' }
    [IO.File]::WriteAllText((Join-Path $stage 'WinDeck 사용 안내.txt'), 'fake-guide-99', (New-Object Text.UTF8Encoding($true)))
    [IO.File]::WriteAllText((Join-Path $stage 'WinDeck.defaults.json'), '{}')

    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $zip = Join-Path $fake 'WinDeck-Portable-99.0.0.zip'
    $stream = [IO.File]::Open($zip, [IO.FileMode]::CreateNew)
    $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $false, [Text.Encoding]::UTF8)
    try {
        $archive.CreateEntry('WinDeck/') | Out-Null
        Get-ChildItem $stage -Recurse | ForEach-Object {
            $name = 'WinDeck/' + $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
            if ($_.PSIsContainer) { $archive.CreateEntry($name + '/') | Out-Null }
            else { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $name) | Out-Null }
        }
    }
    finally {
        $archive.Dispose()
        $stream.Dispose()
    }
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $utf8 = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText((Join-Path $fake 'WinDeck-SHA256-99.0.0.txt'), "$hash  WinDeck-Portable-99.0.0.zip`n", $utf8)
    [IO.File]::WriteAllText((Join-Path $fake 'bad-SHA256.txt'), ('0' * 64) + "  WinDeck-Portable-99.0.0.zip`n", $utf8)
    foreach ($pair in @(@('release.json', 'WinDeck-SHA256-99.0.0.txt'), @('release-bad.json', 'bad-SHA256.txt'))) {
        $release = [ordered]@{
            tag_name = 'v99.0.0'
            html_url = 'https://github.com/musiqsb-svg/WinDeck/releases'
            body     = "점검용 가짜 릴리스`n- 설정 되돌아감 방지`n- 자동 업데이트"
            assets   = @(
                [ordered]@{ name = 'WinDeck-Portable-99.0.0.zip'; browser_download_url = ([Uri]$zip).AbsoluteUri },
                [ordered]@{ name = 'WinDeck-SHA256-99.0.0.txt'; browser_download_url = ([Uri](Join-Path $fake $pair[1])).AbsoluteUri }
            )
        }
        [IO.File]::WriteAllText((Join-Path $fake $pair[0]), ($release | ConvertTo-Json -Depth 5), $utf8)
    }
    $testArgs += @('--process', $fake)
}

[Console]::OutputEncoding = [Text.Encoding]::UTF8
& $exe @testArgs
exit $LASTEXITCODE
