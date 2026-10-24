param([string] $Root, [int] $Rounds = 20, [string] $Out)
$ErrorActionPreference = 'Stop'
function Load { (Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average }
$before = Load
$rows = @()
for ($r = 1; $r -le $Rounds; $r++) {
    $order = if ($r % 2) { @('base', 'head') } else { @('head', 'base') }
    $result = @{}
    foreach ($side in $order) {
        $text = & dotnet (Join-Path $Root "timing-$side/Timing.dll")
        $parts = $text.Trim() -split ' '
        $result[$side] = @([double] $parts[0], [double] $parts[1])
    }
    $rows += [pscustomobject] @{
        Round = $r
        SignalBase = $result.base[0]; SignalHead = $result.head[0]
        MemoBase = $result.base[1]; MemoHead = $result.head[1]
    }
}
$after = Load
$rows | Export-Csv $Out -NoTypeInformation
function Interval([double[]] $ratios) {
    $logs = $ratios | ForEach-Object { [Math]::Log($_) }
    $n = $logs.Count
    $mean = ($logs | Measure-Object -Average).Average
    $sd = [Math]::Sqrt((($logs | ForEach-Object { ($_ - $mean) * ($_ - $mean) }) | Measure-Object -Sum).Sum / ($n - 1))
    $half = 2.093 * $sd / [Math]::Sqrt($n)   # t(0.975, 19)
    '{0:F3} [{1:F3}, {2:F3}]' -f [Math]::Exp($mean), [Math]::Exp($mean - $half), [Math]::Exp($mean + $half)
}
"CPU load before $before%, after $after%"
"signal write, head/base: $(Interval ($rows | ForEach-Object { $_.SignalHead / $_.SignalBase }))"
"memo chain, head/base:   $(Interval ($rows | ForEach-Object { $_.MemoHead / $_.MemoBase }))"
"signal base median ns: $(($rows.SignalBase | Sort-Object)[[int]($Rounds/2)]), head: $(($rows.SignalHead | Sort-Object)[[int]($Rounds/2)])"
"memo base median ns: $(($rows.MemoBase | Sort-Object)[[int]($Rounds/2)]), head: $(($rows.MemoHead | Sort-Object)[[int]($Rounds/2)])"
