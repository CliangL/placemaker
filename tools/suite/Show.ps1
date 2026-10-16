<#
.SYNOPSIS
  See one build in the game: pick a category, type and style; get a picture of the finished build, a picture of the
  plan it was built from, and where it is in the log.

.EXAMPLE
  powershell -File tools/suite/Show.ps1                                    # list the categories and their types
  powershell -File tools/suite/Show.ps1 -Category parks                    # list the types and styles of a category
  powershell -File tools/suite/Show.ps1 -Category parks -Type park -Style formal
  powershell -File tools/suite/Show.ps1 -Type parking                      # the type's default style

.DESCRIPTION
  Launches the game (about 75 s; the game must be closed), builds on a flat test block the way a person does (bucket
  the block, pick the type and style, entrances, Review, Build) and quits. It prints:
    Final picture   the finished build, 45 degrees, framed on the build
    Plan picture    the same view in Review before building (the plan the build came from)
    Log             Placemaker.log and the lines that belong to this build; Player.log for the game's own errors
  Everything else from the run (suite.log: the runner's steps with timings) is in the run folder it prints.
#>
param(
    [string]$Category = '',
    [string]$Type = '',
    [string]$Style = '',
    # flat | slope | rough | zoned (housing grown next to it) | survive (two plopped parks on the block)
    [ValidateSet('flat', 'slope', 'rough', 'zoned', 'survive')][string]$Site = 'flat',
    [switch]$AllMods,
    [switch]$NoBuild,
    [string]$ResultsRoot = 'C:\Users\jack\cs2-area-tool\.forensics\suite\runs'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

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
$catalog = (Get-Content (Join-Path $repo 'suite\catalog.json') -Raw | ConvertFrom-Json).types
function CategoryOf($t) { foreach ($k in $categories.Keys) { if ($categories[$k] -contains $t.group) { return $k } }; return $null }
function ListCategory($k) {
    Write-Host "${k}:"
    foreach ($t in $catalog | Where-Object { $categories[$k] -contains $_.group }) { Write-Host ("  {0,-20} styles: {1}" -f $t.layer, (@($t.presets) -join ', ')) }
}

$cat = $Category.Trim().ToLowerInvariant()
if ($aliases.ContainsKey($cat)) { $cat = $aliases[$cat] }
if ($cat -and -not $categories.Contains($cat)) { Write-Host "No category '$Category'. Categories: $(@($categories.Keys) -join ', ')"; exit 2 }
if (-not $Type) {
    if ($cat) { ListCategory $cat } else { foreach ($k in $categories.Keys) { ListCategory $k } }
    Write-Host "`nThen: Show.ps1 -Category <category> -Type <type> -Style <style>"
    exit 0
}
$t = $catalog | Where-Object { $_.layer -ieq $Type } | Select-Object -First 1
if (-not $t) { Write-Host "No type '$Type'."; if ($cat) { ListCategory $cat } else { Write-Host 'Run Show.ps1 with no arguments for the list.' }; exit 2 }
$tcat = CategoryOf $t
if (-not $tcat) { Write-Host "'$($t.layer)' is hidden from the Make row (group $($t.group)); it cannot be built from the panel."; exit 2 }
if ($cat -and $tcat -ne $cat) { Write-Host "'$($t.layer)' is in $tcat, not $cat."; ListCategory $tcat; exit 2 }
$preset = if ($Style -and $Style -ine 'default') { $Style } else { '' }
if ($preset -and -not (@($t.presets) | Where-Object { $_ -ieq $preset })) { Write-Host "'$($t.layer)' has no style '$Style'. Styles: default, $(@($t.presets) -join ', ')"; exit 2 }
if ($preset) { $preset = @($t.presets) | Where-Object { $_ -ieq $preset } | Select-Object -First 1 }

# ───────────── one build, two pictures ─────────────
$id = $t.layer + '-' + $(if ($preset) { $preset } else { 'default' })
$runId = 'show-' + $id + '-' + (Get-Date -Format 'MMdd-HHmmss')
$frame = [ordered]@{ target = 'build'; view = 'oblique'; pitch = 45 }
$set = [ordered]@{
    scenarios = @([ordered]@{
        id = $id; site = $Site; pace = 'settled'
        steps = @(
            [ordered]@{ do = 'area'; tool = 'bucket' }
            [ordered]@{ do = 'type'; layer = $t.layer; preset = $preset }
            [ordered]@{ do = 'entrances' }
            [ordered]@{ do = 'preview'; shot = 'plan'; shotMode = 'overlay'; camera = $frame }
            [ordered]@{ do = 'build' }
            [ordered]@{ do = 'camera'; target = 'build'; view = 'oblique'; pitch = 45; shot = 'final'; shotMode = 'render' }
        )
    })
}
$setDir = Join-Path (Split-Path $ResultsRoot) 'show'
New-Item -ItemType Directory -Force -Path $setDir | Out-Null
$setFile = Join-Path $setDir "$runId.json"
[IO.File]::WriteAllText($setFile, ($set | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding $false))

Write-Host "Building $($t.layer) ($tcat), style $(if ($preset) { $preset } else { 'default' }), in the game (about 75 s)..."
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
$sc = @((Get-Content $summaryFile -Raw -Encoding UTF8 | ConvertFrom-Json).scenarios) | Select-Object -First 1
$shots = Join-Path $runDir 'shots'
$final = Get-ChildItem $shots -Filter '*-final.png' -ErrorAction SilentlyContinue | Select-Object -First 1
$plan = Get-ChildItem $shots -Filter '*-plan.png' -ErrorAction SilentlyContinue | Select-Object -First 1
$log = Join-Path $runDir 'logs\Placemaker.log'
$from = $null; $to = $null
if (Test-Path $log) {
    $lines = Get-Content $log -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if (-not $from -and $lines[$i] -like "*Placemaker suite: scenario $id on *") { $from = $i + 1 }
        if ($lines[$i] -like "*Placemaker suite: scenario ${id}: *") { $to = $i + 1 }
    }
}
Write-Host ''
if ($sc.status -ne 'built') { Write-Host ("Not built: {0}{1}" -f $sc.status, $(if ($sc.failure) { ": $($sc.failure)" } elseif ($sc.unready) { ": $($sc.unready)" } else { '' })) }
Write-Host ("Final picture:  {0}" -f $(if ($final) { $final.FullName } else { '(none)' }))
Write-Host ("Plan picture:   {0}" -f $(if ($plan) { $plan.FullName } else { '(none)' }))
Write-Host ("Log:            {0}{1}" -f $log, $(if ($from -and $to) { ", lines $from-$to (this build)" } else { '' }))
Write-Host ("Game errors:    {0}" -f (Join-Path $runDir 'logs\Player.log'))
Write-Host ("Run folder:     {0}" -f $runDir)
if ($final) { exit 0 } else { exit 1 }
