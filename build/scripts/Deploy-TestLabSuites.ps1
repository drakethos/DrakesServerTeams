<#
.SYNOPSIS
  Copies a mod's DrakesTestLab suites (Tests/suites/<Suite>/) into test profiles after a local build.

.DESCRIPTION
  The repository is the source of truth: each suite folder the mod owns is replaced in
  <profile>\BepInEx\config\DrakesTestLab\suites\<Suite>\ (other suites there are left alone).
  If Tests\gen_*.py exists it is run first (python), so generated suites are current.
  Never part of the mod package; the build only calls this for local deploys (not CI).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ProjectDir,
    # profile folders separated by ';' (powershell -File passes a single string)
    [string]$ProfileRoots = ''
)

$ErrorActionPreference = 'Stop'
$tests = Join-Path $ProjectDir 'Tests'
$suites = Join-Path $tests 'suites'
if (-not (Test-Path -LiteralPath $suites)) { exit 0 }

foreach ($gen in Get-ChildItem -LiteralPath $tests -Filter 'gen_*.py' -File -ErrorAction SilentlyContinue) {
    $python = Get-Command python -ErrorAction SilentlyContinue
    if (-not $python) {
        Write-Warning "TestLab suites: python not found, skipped $($gen.Name) (copying the committed JSON as is)."
        break
    }

    & $python.Source -I $gen.FullName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "TestLab suites: $($gen.Name) failed (exit $LASTEXITCODE)." }
}

$owned = Get-ChildItem -LiteralPath $suites -Directory
foreach ($root in ($ProfileRoots -split ';')) {
    if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path -LiteralPath $root)) { continue }
    $dest = Join-Path $root 'BepInEx\config\DrakesTestLab\suites'
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    foreach ($suite in $owned) {
        $target = Join-Path $dest $suite.Name
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
        Copy-Item -LiteralPath $suite.FullName -Destination $target -Recurse
    }

    Write-Host "TestLab suites: $($owned.Count) suite(s) -> $dest"
}
