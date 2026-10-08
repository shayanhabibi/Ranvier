$ErrorActionPreference = 'Stop'
$comparisonRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$comparisonProbe = Join-Path $comparisonRoot '.superpowers/ripple-comparison/probe'
Expand-Archive -LiteralPath (Join-Path $PSScriptRoot '2026-10-02-harness.zip') -DestinationPath $comparisonProbe -Force
rtk proxy node (Join-Path $comparisonProbe 'reproduce.mjs') @args
exit $LASTEXITCODE
