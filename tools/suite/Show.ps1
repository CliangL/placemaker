<#
.SYNOPSIS
  See builds in the game: pick category, type(s) and style(s); get pictures of each finished build and of the plan it
  was built from, and where each build is in the log. One game launch for all of them.

.EXAMPLE
  powershell -File tools/suite/Show.ps1                                          # list categories, types, styles
  powershell -File tools/suite/Show.ps1 -Category parks                          # the types and styles of a category
  powershell -File tools/suite/Show.ps1 -Category parks -Type park -Style formal # the usual: 2 pictures
  powershell -File tools/suite/Show.ps1 -Category parking -Type parking -Style all -Views top -Modes clean,zoning
  powershell -File tools/suite/Show.ps1 -Category shops -Type all -Views 45,top -NoPlan

.DESCRIPTION
  Launches the game (about 70 s, then about 10 s per build; the game must be closed), builds each one on its own flat
  test block the way a person does (bucket the block, pick the type and style, entrances, Review, Build) and quits.

  Pictures. Start with the default two; ask for more only when they did not show what you needed:
    plan              the build's plan in Review before building (first view, overlay: the outline and the ghost)
    <view>-<mode>     the finished build, for every view x every mode asked
  Views: 45 (default; 45 degrees from the south), top (straight down), low (20 degrees), close (a close look at the
  middle). Every view but close is framed on the build.
  Modes: clean (default; no overlays), zoning (the game's zone grid: zoned cells in their zone colour), overlay
  (Placemaker's and the game's overlays), ui (the real screen, with the game's bars), terrain (a map of the ground's
  height from above, once per build whatever the views: blue below the outline's typical ground, red above, contour
  lines every 0.5 m, the outline in white; no buildings, trees or roads).

  Logs: Placemaker.log and the lines of each build; Player.log for the game's own errors; suite.log for the runner's
  steps with timings. Everything is in the run folder it prints; contact.html shows every picture in order.
#>
param(
    [string]$Category = '',
    [string[]]$Type = @(),
    [string[]]$Style = @(),
    [string[]]$Views = @('45'),
    [string[]]$Modes = @('clean'),
    [switch]$NoPlan,
    # flat | slope | rough | zoned (housing grown next to it) | survive (two plopped parks on the block)
    [ValidateSet('flat', 'slope', 'rough', 'zoned', 'survive')][string]$Site = 'flat',
    [switch]$AllMods,
    [switch]$NoBuild,
    [string]$ResultsRoot = 'C:\Users\jack\cs2-area-tool\.forensics\suite\runs'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
function Split-List([string[]]$a) { @($a | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
$Type = Split-List $Type; $Style = Split-List $Style; $Views = Split-List $Views; $Modes = Split-List $Modes

# The Make row's categories and the type groups they show (UI/src/stages/PresetBrowser.tsx MAKE_GROUPS / MERGED_GROUPS).
$categories = [ordered]@{
    roads   = @('roads')
    parking = @('parking')
    parks   = @('parks', 'gardens', 'water')
    shops   = @('commercial', 'squares', 'events')
    nature  = @('nature')
    sports  = @('sports')
    farms   = @('farms')
}
$aliases = @{ 'shops & squares' = 'shops'; 'shops&squares' = 'shops'; 'commercial' = 'shops'; 'park' = 'parks'; 'road' = 'roads' }
$viewSteps = @{
    '45'  = [ordered]@{ view = 'oblique'; pitch = 45 }
    'top' = [ordered]@{ view = 'top' }
    'low' = [ordered]@{ view = 'low' }
    'close' = [ordered]@{ view = 'closeup' }
}
$modeSteps = @{ clean = @('render', $false); zoning = @('render', $true); overlay = @('overlay', $false); ui = @('ui', $false); terrain = $null }
$catalog = (Get-Content (Join-Path $repo 'suite\catalog.json') -Raw | ConvertFrom-Json).types
function CategoryOf($t) { foreach ($k in $categories.Keys) { if ($categories[$k] -contains $t.group) { return $k } }; return $null }
function ListCategory($k) {
    Write-Host "${k}:"
    foreach ($t in $catalog | Where-Object { $categories[$k] -contains $_.group }) { Write-Host ("  {0,-20} styles: {1}" -f $t.layer, (@($t.presets) -join ', ')) }
}
function Stop-With([string]$why) { Write-Host $why; exit 2 }

$cat = $Category.Trim().ToLowerInvariant()
if ($aliases.ContainsKey($cat)) { $cat = $aliases[$cat] }
if ($cat -and -not $categories.Contains($cat)) { Stop-With "No category '$Category'. Categories: $(@($categories.Keys) -join ', ')" }
if ($Type.Count -eq 0) {
    if ($cat) { ListCategory $cat } else { foreach ($k in $categories.Keys) { ListCategory $k } }
    Write-Host "`nThen: Show.ps1 -Category <category> -Type <type> -Style <style>"
    exit 0
}
foreach ($v in $Views) { if (-not $viewSteps.ContainsKey($v)) { Stop-With "No view '$v'. Views: 45, top, low, close" } }
foreach ($m in $Modes) { if (-not $modeSteps.ContainsKey($m)) { Stop-With "No mode '$m'. Modes: clean, zoning, overlay, ui, terrain" } }
$viewModes = @($Modes | Where-Object { $_ -ne 'terrain' })
$terrainMap = $Modes -contains 'terrain'

# ───────────── the builds: every type x every style ─────────────
$types = @()
if ($Type.Count -eq 1 -and $Type[0] -ieq 'all') {
    if (-not $cat) { Stop-With '-Type all needs a -Category.' }
    $types = @($catalog | Where-Object { $categories[$cat] -contains $_.group })
} else {
    foreach ($name in $Type) {
        $t = $catalog | Where-Object { $_.layer -ieq $name } | Select-Object -First 1
        if (-not $t) { Write-Host "No type '$name'."; if ($cat) { ListCategory $cat } else { Write-Host 'Run Show.ps1 with no arguments for the list.' }; exit 2 }
        $tcat = CategoryOf $t
        if (-not $tcat) { Stop-With "'$($t.layer)' is hidden from the Make row (group $($t.group)); it cannot be built from the panel." }
        if ($cat -and $tcat -ne $cat) { Write-Host "'$($t.layer)' is in $tcat, not $cat."; ListCategory $tcat; exit 2 }
        $types += $t
    }
}
$builds = @()
foreach ($t in $types) {
    $styles = if ($Style.Count -eq 0) { @('') } elseif ($Style.Count -eq 1 -and $Style[0] -ieq 'all') { @('') + @($t.presets) } else { $Style }
    foreach ($s in $styles) {
        $preset = if ($s -and $s -ine 'default') { @($t.presets) | Where-Object { $_ -ieq $s } | Select-Object -First 1 } else { '' }
        if ($s -and $s -ine 'default' -and -not $preset) { Stop-With "'$($t.layer)' has no style '$s'. Styles: default, $(@($t.presets) -join ', ')" }
        $builds += [pscustomobject]@{ Layer = $t.layer; Category = (CategoryOf $t); Preset = $preset; Id = $t.layer + '-' + $(if ($preset) { $preset } else { 'default' }) }
    }
}

$first = $viewSteps[$Views[0]]
$scenarios = foreach ($b in $builds) {
    $steps = @(
        [ordered]@{ do = 'area'; tool = 'bucket' }
        [ordered]@{ do = 'type'; layer = $b.Layer; preset = $b.Preset }
        [ordered]@{ do = 'entrances' }
    )
    $planCam = [ordered]@{ target = 'build' }; foreach ($k in $first.Keys) { $planCam[$k] = $first[$k] }
    if ($NoPlan) { $steps += [ordered]@{ do = 'preview' } }
    else { $steps += [ordered]@{ do = 'preview'; shot = 'plan'; shotMode = 'overlay'; camera = $planCam } }
    $steps += [ordered]@{ do = 'build' }
    foreach ($v in $Views) {
        foreach ($m in $viewModes) {
            $st = [ordered]@{ do = 'camera'; target = 'build' }
            foreach ($k in $viewSteps[$v].Keys) { $st[$k] = $viewSteps[$v][$k] }
            $st['shot'] = "$v-$m"
            $st['shotMode'] = $modeSteps[$m][0]
            if ($modeSteps[$m][1]) { $st['zones'] = $true }
            $steps += $st
        }
    }
    if ($terrainMap) { $steps += [ordered]@{ do = 'terrain'; name = 'terrain' } }
    [ordered]@{ id = $b.Id; site = $Site; pace = 'settled'; steps = $steps }
}

$label = if ($builds.Count -eq 1) { $builds[0].Id } else { "$($builds.Count)-builds" }
$runId = 'show-' + $label + '-' + (Get-Date -Format 'MMdd-HHmmss')
$setDir = Join-Path (Split-Path $ResultsRoot) 'show'
New-Item -ItemType Directory -Force -Path $setDir | Out-Null
$setFile = Join-Path $setDir "$runId.json"
[IO.File]::WriteAllText($setFile, ([ordered]@{ scenarios = @($scenarios) } | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding $false))

$perBuild = (@($Views).Count * $viewModes.Count) + $(if ($terrainMap) { 1 } else { 0 }) + $(if ($NoPlan) { 0 } else { 1 })
Write-Host ("Building {0} in the game: {1}; {2} picture(s) each (about {3} s)..." -f $builds.Count, (($builds | ForEach-Object { $_.Id }) -join ', '), $perBuild, (70 + 12 * $builds.Count))
$runArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'Run-Suite.ps1'), '-Set', $setFile, '-RunId', $runId)
if ($AllMods) { $runArgs += '-AllMods' }
if ($NoBuild) { $runArgs += '-NoBuild' }
$ErrorActionPreference = 'Continue'   # the game writes to stderr; that is not a failure
$hostOut = & powershell @runArgs 2>&1 | ForEach-Object { "$_" }
$ErrorActionPreference = 'Stop'
$runDir = Join-Path $ResultsRoot $runId

# ───────────── what to look at ─────────────
$summaryFile = Join-Path $runDir 'summary.json'
if (-not (Test-Path $summaryFile)) {
    Write-Host 'The run did not get as far as the game. Last lines:'
    $hostOut | Select-Object -Last 15 | ForEach-Object { Write-Host "  $_" }
    exit 1
}
$rows = @((Get-Content $summaryFile -Raw -Encoding UTF8 | ConvertFrom-Json).scenarios)
$log = Join-Path $runDir 'logs\Placemaker.log'
$lines = if (Test-Path $log) { @(Get-Content $log -Encoding UTF8) } else { @() }
$pictures = 0
Write-Host ''
foreach ($sc in $rows) {
    $from = $null; $to = $null
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if (-not $from -and $lines[$i] -like "*Placemaker suite: scenario $($sc.id) on *") { $from = $i + 1 }
        if ($lines[$i] -like "*Placemaker suite: scenario $($sc.id): *") { $to = $i + 1 }
    }
    $why = if ($sc.status -eq 'built') { '' } elseif ($sc.failure) { ": $($sc.failure)" } elseif ($sc.unready) { ": $($sc.unready)" } elseif ($sc.refused) { ": $($sc.refused)" } else { '' }
    Write-Host ("{0}: {1}{2} ({3})" -f $sc.id, $sc.status, $why, $sc.block)
    if ($sc.shots) {
        foreach ($p in $sc.shots.PSObject.Properties) {
            Write-Host ("  {0,-14} {1}" -f $p.Name, (Join-Path $runDir $p.Value))
            if ("$($p.Value)" -notlike 'ERROR*') { $pictures++ }
        }
    }
    if ($terrainMap) {
        $scFile = Get-ChildItem (Join-Path $runDir 'scenarios') -Filter "*-$($sc.id).json" -ErrorAction SilentlyContinue | Select-Object -First 1
        $tm = if ($scFile) { @((Get-Content $scFile.FullName -Raw -Encoding UTF8 | ConvertFrom-Json).steps) | Where-Object { $_.do -eq 'terrain' -and $_.terrain } | Select-Object -First 1 } else { $null }
        if ($tm) { Write-Host ("  {0,-14} ground {1} m to +{2} m around its typical level; colours +/-{3} m; contours every {4} m" -f 'heights', $tm.terrain.minM, $tm.terrain.maxM, $tm.terrain.rangeM, $tm.terrain.contourM) }
    }
    Write-Host ("  {0,-14} {1}{2}" -f 'log', $log, $(if ($from -and $to) { ", lines $from-$to" } else { '' }))
}
Write-Host ''
Write-Host "Game errors:    $(Join-Path $runDir 'logs\Player.log')"
Write-Host "Runner steps:   $(Join-Path $runDir 'suite.log')"
Write-Host "All pictures:   $(Join-Path $runDir 'contact.html')"
if ($pictures -gt 0) { exit 0 } else { exit 1 }
