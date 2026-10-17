#requires -Version 7.0
$ErrorActionPreference = 'Stop'

foreach ($tool in 'rtk', 'git', 'dotnet', 'pwsh') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "Required tool '$tool' was not found on PATH."
    }
}

Push-Location (Join-Path $PSScriptRoot '..')
try {
    $existing = & rtk proxy git config --get core.hooksPath
    if ($LASTEXITCODE -notin 0, 1) { throw 'Could not read the Git hook configuration.' }
    if ($existing -and $existing -ne '.githooks') {
        throw "core.hooksPath is already '$existing'. Integrate the pre-commit hook there before changing it."
    }
    if (-not $existing) {
        $hooksDirectory = & rtk proxy git rev-parse --git-path hooks
        if ($LASTEXITCODE -ne 0) { throw 'Could not locate Git hooks.' }
        $existingHooks = @()
        if (Test-Path -LiteralPath $hooksDirectory) {
            $existingHooks = @(Get-ChildItem -LiteralPath $hooksDirectory -File -Force |
                Where-Object { $_.Name -notlike '*.sample' })
        }
        if ($existingHooks.Count -gt 0) {
            throw "Existing hook files: $($existingHooks.Name -join ', '). Integrate them before changing core.hooksPath."
        }
    }
    & rtk proxy dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore the pinned .NET tools.' }
    & rtk proxy git config --local core.hooksPath .githooks
    if ($LASTEXITCODE -ne 0) { throw 'Could not enable the repository hooks.' }
    Write-Host 'Enabled .githooks/pre-commit: staged whitespace, F# formatting and comment hygiene.'
}
finally { Pop-Location }
