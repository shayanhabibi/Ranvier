<#
.SYNOPSIS
Runs the counter bench on .NET and under Node.js, and writes one combined
Markdown and JSON report for the current commit.

.DESCRIPTION
Compiles fable/Ranvier.Counters twice with Fable: `plain`, the library
as shipped, and `counters`, the library with RANVIER_COUNTERS. Then runs
bench/Ranvier.Counters with --fable, which measures the .NET engines,
then the Fable harness, and writes
docs/.ai/benchmarks/counters/<sha>[-dirty][-nopmc].md and .json.

Processor counters need an administrator shell. Elevated, the measured .NET
build excludes the library counters, because their increments add
instructions; a second .NET build with RanvierCounters, in
bench/Ranvier.Counters/bin/counters, supplies the library counter
columns, as the `counters` Fable build does for Node.js. Not elevated, the
script stops unless -NoPmc or -PmcDryRun is given.

.PARAMETER Repeat
Processes per measurement, for the .NET worker and for each Node.js run.

.PARAMETER Scale
Multiplies every scenario's N.

.PARAMETER NoPmc
Measures allocations and library counters only. Runs without elevation.

.PARAMETER PmcDryRun
As -NoPmc, and also drives the Fable harness's region handshake without ETW
sessions.

.PARAMETER Out
Output directory, in place of docs/.ai/benchmarks/counters.

.EXAMPLE
./counters.ps1 -NoPmc

.EXAMPLE
./counters.ps1            # from an administrator shell
#>
param(
    [int] $Repeat = 5,
    [int] $Scale = 1,
    [switch] $NoPmc,
    [switch] $PmcDryRun,
    [string] $Out
)

$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$elevated = ([Security.Principal.WindowsPrincipal] $identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$pmc = -not ($NoPmc -or $PmcDryRun)

if ($pmc -and -not $elevated) {
    Write-Error ("Processor counters need an elevated process: ETW kernel sessions and PMC configuration " +
        "are restricted to administrators. Run again from an administrator shell, or pass -NoPmc to " +
        "measure allocations and library counters only.") -ErrorAction Continue
    exit 2
}

function Invoke-Checked {
    param([string] $Command, [string[]] $Arguments)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command $($Arguments -join ' ') exited with $LASTEXITCODE."
    }
}

$fableProject = 'fable/Ranvier.Counters'
$fableOutput = "$fableProject/output"

Push-Location $PSScriptRoot
$savedCounters = $env:RanvierCounters

try {
    Invoke-Checked dotnet @('tool', 'restore')

    # Fable cracks the project with MSBuild, which reads RanvierCounters from the
    # environment.
    Remove-Item Env:RanvierCounters -ErrorAction SilentlyContinue
    Invoke-Checked dotnet @('fable', $fableProject, '-e', '.fs.js', '-o', "$fableOutput/plain", '--noCache')
    $env:RanvierCounters = 'true'
    Invoke-Checked dotnet @('fable', $fableProject, '-e', '.fs.js', '-o', "$fableOutput/counters", '--noCache')
    Remove-Item Env:RanvierCounters

    $countersWorker = 'bench/Ranvier.Counters/bin/counters'

    if ($pmc) {
        # Built before the measured build, which then recompiles the shared
        # intermediate files without RANVIER_COUNTERS.
        Invoke-Checked dotnet @('build', 'bench/Ranvier.Counters', '-c', 'Release',
            '-p:RanvierCounters=true', '-o', $countersWorker)
    }

    $arguments = @('run', '--project', 'bench/Ranvier.Counters', '-c', 'Release')
    $arguments += if ($pmc) { '-p:RanvierCounters=false' } else { '-p:RanvierCounters=true' }
    $arguments += @('--', '--repeat', "$Repeat", '--scale', "$Scale", '--fable', $fableOutput)

    if ($pmc) {
        $arguments += @('--counters-worker', "$countersWorker/Ranvier.Counters.dll")
    }

    if ($PmcDryRun) { $arguments += '--pmc-dry-run' }
    elseif ($NoPmc) { $arguments += '--no-pmc' }

    if ($Out) { $arguments += @('--out', $Out) }

    Invoke-Checked dotnet $arguments
}
finally {
    if ($null -eq $savedCounters) {
        Remove-Item Env:RanvierCounters -ErrorAction SilentlyContinue
    }
    else {
        $env:RanvierCounters = $savedCounters
    }

    Pop-Location
}
