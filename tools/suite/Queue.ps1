<#
.SYNOPSIS
  The game queue at a glance: the game that is open, what runs in it, who is in line with an ETA each, launches.

.EXAMPLE
  powershell -File tools/suite/Queue.ps1                 # once
  powershell -File tools/suite/Queue.ps1 -Watch          # every 5 s until Ctrl+C
  powershell -File tools/suite/Queue.ps1 -Log 40         # the dispatcher's last 40 log lines too
  powershell -File tools/suite/Queue.ps1 -Cancel <id>    # take a waiting request out of the line
#>
param([switch]$Watch, [int]$Log = 0, [string]$Cancel = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GameQueue.ps1')
Q-Init
if ($Cancel) { if (-not (Q-Cancel $Cancel 'cancelled with Queue.ps1')) { Write-Host "no waiting request $Cancel" }; exit 0 }
do {
    $disp = Q-DispatcherInfo
    $st = Q-ReadJson $script:Q.Status
    $queued = @(Q-Queued)
    Write-Host ('{0:HH:mm:ss} dispatcher: {1}' -f (Get-Date), $(if ($disp) { "PID $($disp.pid), up since $($disp.at)" } else { 'not running (a client starts it)' }))
    if ($st -and $disp -and $st.dispatcherPid -eq $disp.pid) {
        Write-Host "state: $($st.state)$(if ($st.note) { " - $($st.note)" })"
        if ($st.game) { Write-Host "game: $($st.game.id) ($($st.game.kind), $($st.game.state)), build $($st.game.build) $($st.game.label) from $($st.game.who), $($st.game.served) request(s) served, grid $($st.game.grid)" }
        if ($st.current) { Write-Host "now: $($st.current.who) $($st.current.kind) $(if ($st.current.total) { "$($st.current.done) of $($st.current.total)" } else { $st.current.scenarios }) build(s), $(Q-Duration $st.current.runningSec) in$(if ([double]$st.current.overSec -gt 0) { ", $(Q-Duration $st.current.overSec) over its estimate" } elseif ($st.current.etaSec) { ", about $(Q-Duration $st.current.etaSec) left" })" }
        $i = 0
        foreach ($p in @($st.plan)) {
            $i++
            Write-Host ("  {0}. {1,-28} {2,-5} {3,2} build(s) {4,3} pictures  build {5}{6}  ETA {7}" -f $i, $p.who, $p.kind, $p.scenarios, $p.pictures, $p.build, $(if ($p.relaunch) { ' (launch)' } elseif ($p.sharesWith) { " (shares $($p.sharesWith)'s launch)" } else { '' }), $(if ($p.unknown) { 'unknown' } else { 'about ' + (Q-Duration $p.doneInSec) }))
        }
        if (@($st.plan).Count -eq 0) { Write-Host '  nobody in line' }
        Write-Host "launches by this dispatcher: $($st.launches); measured: boot $($st.rates.bootSec) s, a build $($st.rates.perBuildSec) s + $($st.rates.perPictureSec) s per picture (+ $($st.rates.landSec) s with its own land), quit $($st.rates.quitSec) s"
    } else {
        Write-Host "in line: $($queued.Count)"
        foreach ($q in $queued) { Write-Host "  $($q.id) $($q.who) $($q.kind) $($q.scenarios) build(s)" }
    }
    Write-Host (MI-StatusLine)
    if ($Log -gt 0 -and (Test-Path $script:Q.Log)) { Write-Host '--- dispatcher.log'; Get-Content $script:Q.Log -Tail $Log | ForEach-Object { Write-Host $_ } }
    if ($Watch) { Start-Sleep -Seconds 5; Write-Host '' }
} while ($Watch)
