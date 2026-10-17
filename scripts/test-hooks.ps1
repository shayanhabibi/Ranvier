#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('ranvier-hooks-test-' + [guid]::NewGuid().ToString('N'))
$utf8 = [System.Text.UTF8Encoding]::new($false)
$passed = 0

function Invoke-TestGit([string[]] $GitArguments, [bool] $ShouldPass = $true) {
    $output = @(& rtk proxy git @GitArguments 2>&1)
    $code = $LASTEXITCODE
    if (($code -eq 0) -ne $ShouldPass) { throw "Unexpected exit $code for git $GitArguments`n$($output -join "`n")" }
    if (-not $ShouldPass -and ($output -join "`n") -notmatch 'Pre-commit failed:') {
        throw "Git failed outside the hook: $($output -join "`n")"
    }
    return $output -join "`n"
}

function Commit-Fixture([string] $Name, [bool] $ShouldPass = $true, [string] $FailureText = 'Pre-commit failed:') {
    $output = Invoke-TestGit @('-c', 'commit.gpgsign=false', 'commit', '-m', $Name) $ShouldPass
    if (-not $ShouldPass -and -not $output.Contains($FailureText)) { throw "Wrong failure reason: $output" }
    $script:passed++
    Write-Host "PASS $Name"
}

[System.IO.Directory]::CreateDirectory($testRoot) | Out-Null
Push-Location $testRoot
try {
    Copy-Item -LiteralPath (Join-Path $repoRoot '.githooks') -Destination $testRoot -Recurse
    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts') -Destination $testRoot -Recurse
    Copy-Item -LiteralPath (Join-Path $repoRoot '.config') -Destination $testRoot -Recurse
    foreach ($name in '.editorconfig', '.gitattributes') {
        Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $testRoot
    }
    Invoke-TestGit @('init', '--quiet') | Out-Null
    Invoke-TestGit @('config', 'user.name', 'Hook Test') | Out-Null
    Invoke-TestGit @('config', 'user.email', 'hook-test@example.invalid') | Out-Null
    $otherHook = Join-Path $testRoot '.git/hooks/pre-push'
    [System.IO.File]::WriteAllText($otherHook, "#!/bin/sh`nexit 0`n", $utf8)
    $refused = @(& rtk proxy pwsh -NoProfile -File scripts/setup-hooks.ps1 2>&1)
    if ($LASTEXITCODE -eq 0 -or ($refused -join "`n") -notmatch 'Existing hook files: pre-push') {
        throw 'Setup failed to protect the existing pre-push hook.'
    }
    $passed++
    Write-Host 'PASS setup protects other existing hooks'
    Remove-Item -LiteralPath $otherHook
    & rtk proxy pwsh -NoProfile -File scripts/setup-hooks.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Fixture hook setup failed.' }
    Invoke-TestGit @('add', '.editorconfig', '.gitattributes', '.config/dotnet-tools.json') | Out-Null
    Commit-Fixture 'non-F# commit passes'

    $good = "module Sample`r`n`r`nlet value = 1`r`n"
    $bad = "module Sample`r`nlet value=1`r`n"
    [System.IO.File]::WriteAllText((Join-Path $testRoot 'Sample.fs'), $good, $utf8)
    Invoke-TestGit @('add', 'Sample.fs') | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $testRoot 'Sample.fs'), $bad, $utf8)
    Commit-Fixture 'formatted index passes despite unformatted working tree'
    if ([System.IO.File]::ReadAllText((Join-Path $testRoot 'Sample.fs')) -ne $bad) { throw 'Hook modified the working tree.' }

    Invoke-TestGit @('add', 'Sample.fs') | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $testRoot 'Sample.fs'), $good, $utf8)
    Commit-Fixture 'unformatted index fails despite formatted working tree' $false
    $staged = Invoke-TestGit @('show', ':Sample.fs')
    if ($staged -notmatch 'let value=1') { throw 'Hook changed the index.' }
    Invoke-TestGit @('restore', '--staged', 'Sample.fs') | Out-Null

    $review = "module Review`r`n`r`n//FOR-REVIEW remove me`r`nlet value = 1`r`n"
    [System.IO.File]::WriteAllText((Join-Path $testRoot 'Review.fs'), $review, $utf8)
    Invoke-TestGit @('add', 'Review.fs') | Out-Null
    Commit-Fixture 'review comment fails' $false 'FOR-REVIEW'
    $literal = "module Review`r`n`r`nlet text = `"//FOR-REVIEW literal`"`r`n"
    [System.IO.File]::WriteAllText((Join-Path $testRoot 'Review.fs'), $literal, $utf8)
    Invoke-TestGit @('add', 'Review.fs') | Out-Null
    Commit-Fixture 'review marker in string passes'

    [System.IO.File]::WriteAllText((Join-Path $testRoot 'whitespace.txt'), "trailing  `n", $utf8)
    Invoke-TestGit @('add', 'whitespace.txt') | Out-Null
    Commit-Fixture 'staged whitespace fails' $false
    [System.IO.File]::WriteAllText((Join-Path $testRoot 'whitespace.txt'), "clean`n", $utf8)
    Invoke-TestGit @('add', 'whitespace.txt') | Out-Null
    Commit-Fixture 'corrected whitespace passes'

    Invoke-TestGit @('mv', 'Sample.fs', 'café name.fs') | Out-Null
    Commit-Fixture 'rename with spaces and Unicode passes'
    Invoke-TestGit @('rm', 'café name.fs') | Out-Null
    Commit-Fixture 'deleted F# file passes'

    [System.IO.File]::WriteAllText((Join-Path $testRoot 'Script.fsx'), "//FOR-REVIEW remove me`r`nlet value = 1`r`n", $utf8)
    Invoke-TestGit @('add', 'Script.fsx') | Out-Null
    Commit-Fixture 'review comments in scripts fail' $false 'FOR-REVIEW'
    [System.IO.File]::WriteAllText((Join-Path $testRoot 'Script.fsx'), "let value = 1`r`n", $utf8)
    Invoke-TestGit @('add', 'Script.fsx') | Out-Null
    Commit-Fixture 'corrected script passes'

    [System.IO.File]::WriteAllText((Join-Path $testRoot 'Config.fs'), "module Config`r`n`r`nlet answer () =`r`n    if true then`r`n        42`r`n    else`r`n        0`r`n", $utf8)
    & rtk proxy dotnet fantomas Config.fs
    if ($LASTEXITCODE -ne 0) { throw 'Could not format the configuration fixture.' }
    Invoke-TestGit @('add', 'Config.fs') | Out-Null
    $configPath = Join-Path $testRoot '.editorconfig'
    $config = [System.IO.File]::ReadAllText($configPath)
    [System.IO.File]::WriteAllText($configPath, $config.Replace('indent_size = 4', 'indent_size = 2'), $utf8)
    Commit-Fixture 'formatter uses staged configuration despite unstaged settings'
    [System.IO.File]::WriteAllText($configPath, $config, $utf8)

    $manifestPath = Join-Path $testRoot '.config/dotnet-tools.json'
    $manifest = [System.IO.File]::ReadAllText($manifestPath)
    [System.IO.File]::WriteAllText($manifestPath, $manifest.Replace('8.0.5', '0.0.0'), $utf8)
    [System.IO.File]::WriteAllText((Join-Path $testRoot 'Version.fs'), "module Version`r`n`r`nlet value = 1`r`n", $utf8)
    Invoke-TestGit @('add', 'Version.fs') | Out-Null
    Commit-Fixture 'formatter uses staged tool version despite unstaged manifest'
    [System.IO.File]::WriteAllText($manifestPath, $manifest, $utf8)

    [System.IO.Directory]::CreateDirectory((Join-Path $testRoot 'dist')) | Out-Null
    $distPath = Join-Path $testRoot 'dist/Review.fs'
    [System.IO.File]::WriteAllText($distPath, "module Dist`r`n`r`n//FOR-REVIEW remove me`r`nlet value = 1`r`n", $utf8)
    Invoke-TestGit @('add', 'dist/Review.fs') | Out-Null
    Commit-Fixture 'staged sources under dist are checked' $false 'FOR-REVIEW'
    Write-Host "$passed hook integration cases passed."
}
finally {
    Pop-Location
    $resolved = [System.IO.Path]::GetFullPath($testRoot)
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolved.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
        [System.IO.Path]::GetFileName($resolved).StartsWith('ranvier-hooks-test-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
