<#
.SYNOPSIS
  Compares two suite runs: per scenario status, failed checks, left-out count and time; switching and timeline.

.EXAMPLE
  powershell -File tools/suite/Compare-Suite.ps1 -Old 20261015-140000-core -New 20261015-150000-core
  Writes runs/<New>/compare-<Old>.md and prints it.
#>
param(
    [Parameter(Mandatory = $true)][string]$Old,
    [Parameter(Mandatory = $true)][string]$New,
    [string]$ResultsRoot = 'C:\Users\jack\cs2-area-tool\.forensics\suite\runs'
)
$ErrorActionPreference = 'Stop'
function Load([string]$id) {
    $p = Join-Path (Join-Path $ResultsRoot $id) 'summary.json'
    if (-not (Test-Path $p)) { throw "no summary.json in run $id" }
    return Get-Content $p -Raw | ConvertFrom-Json
}
$a = Load $Old
$b = Load $New
$rowsA = @{}; foreach ($r in $a.scenarios) { $rowsA[$r.id] = $r }
$rowsB = @{}; foreach ($r in $b.scenarios) { $rowsB[$r.id] = $r }
$ids = @($a.scenarios | ForEach-Object { $_.id }) + @($b.scenarios | ForEach-Object { $_.id }) | Select-Object -Unique

function Failed($r) { if ($r -and $r.failed) { return (@($r.failed) -join ', ') } else { return '' } }

$out = New-Object System.Collections.ArrayList
[void]$out.Add("# Suite compare: $Old -> $New")
[void]$out.Add('')
[void]$out.Add("git $($a.git) -> $($b.git); scenarios $(@($a.scenarios).Count) -> $(@($b.scenarios).Count); errors $($a.errorsTotal)+$($a.exceptionsTotal) -> $($b.errorsTotal)+$($b.exceptionsTotal)")
if ($a.switching -or $b.switching) { [void]$out.Add("switching worst frame: $($a.switching.worstFrameMs) ms -> $($b.switching.worstFrameMs) ms") }
foreach ($k in @('worldReadySec', 'fixtureDoneSec', 'scenariosDoneSec', 'doneSec')) { [void]$out.Add("timeline $k`: $($a.timeline.$k) -> $($b.timeline.$k)") }
[void]$out.Add('')
[void]$out.Add('| scenario | status | failed checks (old -> new) | left out | sec |')
[void]$out.Add('|---|---|---|---|---|')
$changed = 0
foreach ($id in $ids) {
    $x = $rowsA[$id]; $y = $rowsB[$id]
    $sx = if ($x) { $x.status } else { '-' }; $sy = if ($y) { $y.status } else { '-' }
    $fx = Failed $x; $fy = Failed $y
    $lx = if ($x) { $x.leftOut } else { '' }; $ly = if ($y) { $y.leftOut } else { '' }
    $tx = if ($x) { $x.sec } else { '' }; $ty = if ($y) { $y.sec } else { '' }
    $mark = if ($sx -ne $sy -or $fx -ne $fy -or "$lx" -ne "$ly") { $changed++; '**' } else { '' }
    [void]$out.Add("| $mark$id$mark | $sx -> $sy | $fx -> $fy | $lx -> $ly | $tx -> $ty |")
}
[void]$out.Add('')
[void]$out.Add("$changed scenario(s) changed (bold).")
$text = $out -join "`r`n"
$dst = Join-Path (Join-Path $ResultsRoot $New) "compare-$Old.md"
Set-Content -Path $dst -Value $text -Encoding UTF8
Write-Host $text
