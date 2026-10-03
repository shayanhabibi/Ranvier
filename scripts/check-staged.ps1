#requires -Version 7.0
$ErrorActionPreference = 'Stop'

function Read-GitBytes([string[]] $GitArguments) {
    $info = [System.Diagnostics.ProcessStartInfo]::new('rtk')
    foreach ($argument in @('proxy', 'git') + $GitArguments) { $info.ArgumentList.Add($argument) }
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $process = [System.Diagnostics.Process]::Start($info)
    $bytes = [System.IO.MemoryStream]::new()
    try {
        $errors = $process.StandardError.ReadToEndAsync()
        $process.StandardOutput.BaseStream.CopyTo($bytes)
        $process.WaitForExit()
        $errorText = $errors.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Git failed: $errorText" }
        return ,$bytes.ToArray()
    }
    finally { $bytes.Dispose(); $process.Dispose() }
}

function Write-IndexFile([string] $GitPath, [string] $Destination) {
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($Destination)) | Out-Null
    [System.IO.File]::WriteAllBytes($Destination, (Read-GitBytes @('show', ":$GitPath")))
}

$snapshot = $null
Push-Location (Join-Path $PSScriptRoot '..')
try {
    & rtk proxy git diff --cached --check
    if ($LASTEXITCODE -ne 0) { throw 'Fix the staged whitespace errors and stage the corrected files.' }

    $names = [System.Text.Encoding]::UTF8.GetString((Read-GitBytes @('diff', '--cached', '--name-only', '-z', '--diff-filter=ACMR')))
    $files = @($names.Split([char]0, [System.StringSplitOptions]::RemoveEmptyEntries) |
        Where-Object { [System.IO.Path]::GetExtension($_) -in '.fs', '.fsi', '.fsx' })
    if ($files.Count -eq 0) { exit 0 }

    $snapshot = Join-Path ([System.IO.Path]::GetTempPath()) ('ranvier-staged-' + [guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($snapshot) | Out-Null
    $sourceRoot = Join-Path $snapshot 'source'
    $commentRoot = Join-Path $snapshot 'comments'
    $tracked = [System.Text.Encoding]::UTF8.GetString((Read-GitBytes @('ls-files', '-z')))
    $configs = @($tracked.Split([char]0, [System.StringSplitOptions]::RemoveEmptyEntries) |
        Where-Object { [System.IO.Path]::GetFileName($_) -in '.editorconfig', '.fantomasignore' })
    foreach ($config in $configs) { Write-IndexFile $config (Join-Path $sourceRoot $config) }
    Write-IndexFile '.config/dotnet-tools.json' (Join-Path $sourceRoot '.config/dotnet-tools.json')
    if ('global.json' -in $tracked.Split([char]0, [System.StringSplitOptions]::RemoveEmptyEntries)) {
        Write-IndexFile 'global.json' (Join-Path $sourceRoot 'global.json')
    }

    $paths = @(foreach ($file in $files) {
        $path = [System.IO.Path]::GetFullPath((Join-Path $sourceRoot $file))
        if (-not $path.StartsWith($sourceRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Staged path escapes the snapshot: $file"
        }
        Write-IndexFile $file $path
        $commentPath = Join-Path $commentRoot ($file + '.fs')
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($commentPath)) | Out-Null
        [System.IO.File]::Copy($path, $commentPath)
        $path
    })

    Write-Host "Checking $($files.Count) staged F# file(s):"
    foreach ($file in $files) { Write-Host "  $file" }
    Push-Location $sourceRoot
    try { & rtk proxy dotnet fantomas check @paths }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw 'Format the reported files with dotnet fantomas, then stage the corrections.' }
    & rtk proxy pwsh -NoLogo -NoProfile -File (Join-Path $PSScriptRoot 'vendor/comment-hygiene/review-comments.ps1') -Path $commentRoot -Check -Exclude '(?!)'
    if ($LASTEXITCODE -ne 0) { throw 'Remove FOR-REVIEW comments from the staged F# files before committing.' }
}
catch {
    Write-Host "Pre-commit failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    if ($snapshot -and (Test-Path -LiteralPath $snapshot)) {
        $resolved = [System.IO.Path]::GetFullPath($snapshot)
        $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        if ($resolved.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
            [System.IO.Path]::GetFileName($resolved).StartsWith('ranvier-staged-')) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
    Pop-Location
}
