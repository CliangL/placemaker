# Playground for the user: launches CS2 with only Placemaker, in a window, builds the flat test grid,
# points the camera at it at 45 degrees and hands over the controls. Nothing runs, nothing quits.
# The user's own Placemaker options are kept; their game settings are put back when they close the game.
#   powershell -File tools/suite/Play.ps1                 # default grid
#   powershell -File tools/suite/Play.ps1 -Grid 12x8      # a bigger grid
#   powershell -File tools/suite/Play.ps1 -AllMods        # with the whole playset (slow boot)
param([string]$Grid = '', [switch]$AllMods, [string]$Window = '1600x900', [ValidateSet('suite', 'game')][string]$Look = 'suite', [switch]$NoBuild)
$runId = 'play-' + (Get-Date -Format 'MMdd-HHmmss')
$args2 = @('-File', (Join-Path $PSScriptRoot 'Run-Suite.ps1'), '-Playground', '-RunId', $runId, '-Window', $Window, '-Look', $Look)
if ($Grid) { $args2 += @('-Grid', $Grid) }
if ($AllMods) { $args2 += '-AllMods' }
if ($NoBuild) { $args2 += '-NoBuild' }
& powershell -NoProfile -ExecutionPolicy Bypass @args2
