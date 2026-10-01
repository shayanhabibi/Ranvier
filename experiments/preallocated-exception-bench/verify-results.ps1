param([string] $Path = "$PSScriptRoot/results-native.json")

$ErrorActionPreference = 'Stop'
$report = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
if (-not $report.verification.verified -or -not $report.verification.sameInstanceAfterCollection) {
    throw 'Missing successful runtime identity, throw/catch, and compacting-GC verification.'
}
if ($report.environment.DOTNET_TieredCompilation -ne '0') {
    throw 'The measurements did not disable tiered compilation.'
}
$native = @($report.results | Where-Object scenario -eq 'native-oom')
$ordinary = @($report.results | Where-Object scenario -eq 'cached-oom')
if ($native.Count -ne 2 -or $ordinary.Count -ne 2) {
    throw 'Expected both OOM scenarios at both call depths.'
}
foreach ($row in $native) {
    if ($row.samples.Count -ne 7) { throw 'Missing native measurement samples.' }
    foreach ($sample in $row.samples) {
        if (-not $sample.diagnostic.immutable -or $sample.diagnostic.rawFrames -ne 0 -or $sample.bytesOp -ne 0) {
            throw 'The native scenario did not demonstrate allocation-free throws with zero captured frames.'
        }
        if ($sample.acquisition.symbol -ne 'coreclr!g_pPreallocatedOutOfMemoryException') {
            throw 'The native sample is missing the intended runtime symbol provenance.'
        }
    }
}
foreach ($row in $ordinary) {
    if ($row.samples.Count -ne 7) { throw 'Missing ordinary measurement samples.' }
    foreach ($sample in $row.samples) {
        if ($sample.diagnostic.immutable -or $sample.diagnostic.rawFrames -ne ($row.depth + 2) -or $sample.bytesOp -le 0) {
            throw 'The ordinary OOM control did not capture the expected frames and allocate.'
        }
    }
}
'PASS: native and ordinary controls verified across both depths and all seven samples.'
