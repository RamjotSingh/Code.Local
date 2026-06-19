<#
.SYNOPSIS
  Build self-contained single-file Code.Local binaries for one or more RIDs.

.DESCRIPTION
  Produces a runtime-free single binary per RID and packages it (zip for Windows,
  tar.gz for unix) into the output directory with a SHA256 checksums file.

  Because Code.Local uses self-contained single-file publishing (not Native AOT),
  every RID cross-publishes from a single machine. The canonical release path is the
  GitHub Actions workflow (.github/workflows/release.yml), which runs on Linux and
  sets the unix executable bit reliably; this script is for local builds/testing.

.EXAMPLE
  pwsh scripts/publish.ps1 -Version 0.1.0
  pwsh scripts/publish.ps1 -Version 0.1.0 -Rids win-x64,linux-x64
#>
[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    [string[]]$Rids = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64'),
    [string]$OutDir = 'dist'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$proj = 'src/Code.Local'
$publishRoot = 'publish'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

foreach ($rid in $Rids) {
    Write-Host "Publishing $rid ..." -ForegroundColor Cyan
    $out = Join-Path $publishRoot $rid
    dotnet publish $proj -c Release -r $rid --self-contained true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none -p:Version=$Version -o $out --nologo
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $rid" }

    if ($rid -like 'win-*') {
        $zip = Join-Path $OutDir "codelocal-$Version-$rid.zip"
        if (Test-Path $zip) { Remove-Item $zip }
        Compress-Archive -Path (Join-Path $out 'codelocal.exe') -DestinationPath $zip
    }
    else {
        # tar.exe ships with Windows 10+. NOTE: the unix +x bit is set reliably by the
        # CI (Linux) build; tar on Windows may not preserve it.
        tar -C $out -czf (Join-Path $OutDir "codelocal-$Version-$rid.tar.gz") 'codelocal'
    }
}

$checksums = Get-ChildItem $OutDir -File | Where-Object { $_.Name -ne 'checksums.txt' } | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name
}
$checksums | Set-Content (Join-Path $OutDir 'checksums.txt')

Write-Host "Artifacts in $OutDir" -ForegroundColor Green
Get-ChildItem $OutDir | Format-Table Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } }
