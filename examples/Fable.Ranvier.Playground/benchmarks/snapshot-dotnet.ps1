$ErrorActionPreference = 'Stop'
$snapshotRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$snapshotOut = Join-Path $snapshotRoot 'docs/research/branch-snapshots/dotnet-results.json'
$previousTiered = $env:DOTNET_TieredCompilation
$rows = [System.Collections.Generic.List[object]]::new()
try {
    $env:DOTNET_TieredCompilation = '0'
    for ($replicate = 0; $replicate -lt 3; $replicate++) {
        $raw = & rtk proxy dotnet run --project (Join-Path $snapshotRoot 'experiments/Ranvier.View.Foundation/SnapshotBench/SnapshotBench.fsproj') --configuration Release
        if ($LASTEXITCODE -ne 0) { throw 'Snapshot benchmark failed' }
        $payload = ($raw | Where-Object { $_.StartsWith('{"configuration"') }) | ConvertFrom-Json
        if ($null -eq $payload) { throw 'Snapshot benchmark returned no JSON' }
        foreach ($row in $payload.samples) {
            $row | Add-Member -NotePropertyName replicate -NotePropertyValue $replicate
            $rows.Add($row)
        }
        Write-Host "Completed .NET replicate $($replicate + 1)"
    }
    $result = [ordered]@{
        runtime = $payload.runtime
        configuration = 'Release'
        tieredCompilation = $false
        measuredAt = [DateTime]::UtcNow.ToString('O')
        baseCommit = (& rtk git -C $snapshotRoot rev-parse HEAD).Trim()
        dirtyExperiment = $true
        independentProcesses = 3
        warmups = 3
        rounds = 11
        samples = $rows
    }
    [System.IO.Directory]::CreateDirectory((Split-Path $snapshotOut)) | Out-Null
    [System.IO.File]::WriteAllText($snapshotOut, ($result | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
} finally {
    $env:DOTNET_TieredCompilation = $previousTiered
}
