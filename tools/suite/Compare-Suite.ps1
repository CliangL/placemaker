<#
.SYNOPSIS
  Compares two suite runs, check by check: what got FIXED, what is a NEW FAIL, what STILL FAILS, which numbers changed.

.EXAMPLE
  powershell -File tools/suite/Compare-Suite.ps1 -Old 20261015-140000-core -New 20261015-150000-core
  Writes runs/<New>/compare-<Old>.md and prints it.

.DESCRIPTION
  Scenarios are matched by id. A scenario that ran on a different block or a block with different streets is marked
  (its checks are not comparable one to one). Numbers compared: left out, kept, seen/objects, pit cells and depth, plan
  compute ms, seconds. Shots with the same file name suffix in both runs get a mean pixel difference (downscaled), so a
  changed picture is visible without opening both.
#>
param(
    [Parameter(Mandatory = $true)][string]$Old,
    [Parameter(Mandatory = $true)][string]$New,
    [string]$ResultsRoot = 'C:\Users\jack\cs2-area-tool\.forensics\suite\runs',
    [switch]$NoImages
)
$ErrorActionPreference = 'Stop'
function Load([string]$id) {
    $p = Join-Path (Join-Path $ResultsRoot $id) 'summary.json'
    if (-not (Test-Path $p)) { throw "no summary.json in run $id" }
    return Get-Content $p -Raw | ConvertFrom-Json
}
function ScenarioFile([string]$run, [string]$id) {
    $d = Join-Path (Join-Path $ResultsRoot $run) 'scenarios'
    $f = Get-ChildItem $d -Filter "*-$id.json" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($f) { return Get-Content $f.FullName -Raw | ConvertFrom-Json } else { return $null }
}
$a = Load $Old
$b = Load $New
$rowsA = @{}; foreach ($r in $a.scenarios) { $rowsA[$r.id] = $r }
$rowsB = @{}; foreach ($r in $b.scenarios) { $rowsB[$r.id] = $r }
$ids = @($a.scenarios | ForEach-Object { $_.id }) + @($b.scenarios | ForEach-Object { $_.id }) | Select-Object -Unique

$out = New-Object System.Collections.ArrayList
[void]$out.Add("# Suite compare: $Old -> $New")
[void]$out.Add('')
[void]$out.Add("build $($a.git) ($($a.dllHash)) -> $($b.git) ($($b.dllHash)); profile $($a.profile) -> $($b.profile); scenarios $(@($a.scenarios).Count) -> $(@($b.scenarios).Count)")
[void]$out.Add("headline old: $($a.headline)")
[void]$out.Add("headline new: $($b.headline)")
if ($a.switching -or $b.switching) { [void]$out.Add("switching worst frame: $($a.switching.worstFrameMs) ms -> $($b.switching.worstFrameMs) ms") }
[void]$out.Add('')

$fixed = @(); $newFail = @(); $still = @(); $changedNum = @(); $moved = @()
foreach ($id in $ids) {
    $x = $rowsA[$id]; $y = $rowsB[$id]
    if (-not $x -or -not $y) { [void]$out.Add("- $id only in $(if ($x) { $Old } else { $New })"); continue }
    if ($x.block -ne $y.block -or $x.streets -ne $y.streets) { $moved += "$id ($($x.block) $($x.streets) -> $($y.block) $($y.streets))" }
    $fa = @($x.failed); $fb = @($y.failed)
    foreach ($c in $fa) { if ($c -and -not ($fb -contains $c)) { $fixed += "$id`: $c" } }
    foreach ($c in $fb) { if ($c -and -not ($fa -contains $c)) { $newFail += "$id`: $c" } elseif ($c) { $still += "$id`: $c" } }
    foreach ($k in @('result', 'status', 'leftOut', 'kept', 'seen', 'objects', 'pitCells', 'maxPitDepth', 'planComputeMs', 'buildPath')) {
        $u = $x.$k; $v = $y.$k
        if ("$u" -ne "$v") { $changedNum += "$id $k`: $u -> $v" }
    }
}
[void]$out.Add("## FIXED ($($fixed.Count))"); $fixed | ForEach-Object { [void]$out.Add("- $_") }
[void]$out.Add("## NEW FAIL ($($newFail.Count))"); $newFail | ForEach-Object { [void]$out.Add("- $_") }
[void]$out.Add("## STILL FAIL ($($still.Count))"); $still | ForEach-Object { [void]$out.Add("- $_") }
[void]$out.Add("## CHANGED NUMBERS ($($changedNum.Count))"); $changedNum | ForEach-Object { [void]$out.Add("- $_") }
if ($moved.Count -gt 0) { [void]$out.Add("## DIFFERENT BLOCK OR STREETS (checks not comparable one to one)"); $moved | ForEach-Object { [void]$out.Add("- $_") } }

if (-not $NoImages) {
    Add-Type -AssemblyName System.Drawing
    function Small([string]$p) {
        $img = [System.Drawing.Image]::FromFile($p)
        $bmp = New-Object System.Drawing.Bitmap 200, 112
        $g = [System.Drawing.Graphics]::FromImage($bmp); $g.DrawImage($img, 0, 0, 200, 112); $g.Dispose(); $img.Dispose()
        return $bmp
    }
    $da = Join-Path (Join-Path $ResultsRoot $Old) 'shots'
    $db = Join-Path (Join-Path $ResultsRoot $New) 'shots'
    $pairs = @()
    if ((Test-Path $da) -and (Test-Path $db)) {
        $mapA = @{}
        foreach ($f in Get-ChildItem $da -Filter '*.png' | Where-Object { $_.Name -notmatch 'heat' }) { $mapA[($f.Name -replace '^\d+-', '')] = $f.FullName }
        foreach ($f in Get-ChildItem $db -Filter '*.png' | Where-Object { $_.Name -notmatch 'heat' }) {
            $key = $f.Name -replace '^\d+-', ''
            if (-not $mapA.ContainsKey($key)) { continue }
            $p = Small $mapA[$key]; $q = Small $f.FullName
            $sum = 0
            for ($yy = 0; $yy -lt 112; $yy++) { for ($xx = 0; $xx -lt 200; $xx++) { $c1 = $p.GetPixel($xx, $yy); $c2 = $q.GetPixel($xx, $yy); $sum += [math]::Max([math]::Abs($c1.R - $c2.R), [math]::Max([math]::Abs($c1.G - $c2.G), [math]::Abs($c1.B - $c2.B))) } }
            $p.Dispose(); $q.Dispose()
            $pairs += [pscustomobject]@{ shot = $key; mean = [math]::Round($sum / (200 * 112), 1) }
        }
    }
    [void]$out.Add("## SHOTS (mean max-channel difference, 200x112; 0 = same picture)")
    $pairs | Sort-Object mean -Descending | ForEach-Object { [void]$out.Add("- $($_.shot): $($_.mean)") }
}
$text = $out -join "`r`n"
$dst = Join-Path (Join-Path $ResultsRoot $New) "compare-$Old.md"
Set-Content -Path $dst -Value $text -Encoding UTF8
Write-Host $text
