<#
.SYNOPSIS
  Runs the Placemaker in-game test suite: one game launch, a flat new world, a road grid, many scenarios, screenshots.

.DESCRIPTION
  powershell -File tools/suite/Run-Suite.ps1 -Set core

  1. Stops (exit 2) while a Cities2.exe it did not start is running: the user's session is never touched.
  2. Builds and deploys this worktree's mod into Mods/Placemaker (it stays installed afterwards).
  3. Saves the user's settings (root *.coc, launcher-settings.json, continue_game.json, ModsSettings/) into
     .forensics/suite/user-settings/, switches to a small window (no vsync, muted), arms the run
     (ModsData/Placemaker/suite/run.json + runOnce.txt) and starts Cities2.exe directly with only local mods
     (-disableModding -disableCodeModding-: Placemaker and nothing else) and -placemakerSuite=<runId>.
  4. Watches the heartbeat; the mod quits through the game's own quit. Only the PID this script started is ever
     killed, and only when the clean quit did not happen in time or the heartbeat stopped.
  5. Copies the session logs into the run folder and puts the user's settings back. If a run was cut off before
     that, the next run puts them back first (user-settings/pending.txt).
  Results: .forensics/suite/runs/<runId>/ (summary.json, summary.md, scenarios/*.json, shots/*.png, logs/).
  -Opt 'key=value;key=value' passes extra run.json fields (world=map:Name, roads=single, trace=true, ...).
#>
param(
    [ValidateSet('smoke', 'core', 'full', 'bootstrap')][string]$Set = 'smoke',
    [string]$RunId = '',
    [switch]$NoBuild,
    [ValidateSet('A', 'B')][string]$Route = 'A',
    [int]$TimeoutMin = 0,
    [int]$MaxScenarios = 0,
    [switch]$NoSwitching,
    # Only Placemaker loads: -disableModding stops the Paradox mods from being registered (GameManager gates the PDX SDK's
    # RegisterDatabase on it) and the negated -disableCodeModding- (Mono.Options '-flag-' form, parsed after it) turns local
    # code mods back on. -disablePdxSdk cannot be used on 1.6.2f1: EditorHierarchyUISystem.GetSystemsForContextActions
    # dereferences the missing PdxSdk platform and the boot dies in CreateSystems (run boot1). -AllMods loads the whole playset.
    [switch]$AllMods,
    [switch]$KeepGraphics,
    [switch]$NoTerrain,
    [string]$Window = '1600x900',
    [float]$GrowSeconds = 100,
    [float]$PregrowSeconds = 75,
    [string]$Opt = '',
    [string]$ResultsRoot = 'C:\Users\jack\cs2-area-tool\.forensics\suite\runs'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$gameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II'
$exe = Join-Path $gameDir 'Cities2.exe'
$ud = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
$suiteData = Join-Path $ud 'ModsData\Placemaker\suite'
$saved = Join-Path (Split-Path $ResultsRoot) 'user-settings'
if (-not $RunId) { $RunId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $Set }
$runDir = Join-Path $ResultsRoot $RunId
New-Item -ItemType Directory -Force -Path $runDir | Out-Null
$hostLog = Join-Path $runDir 'host.log'
$t0 = Get-Date

function Write-NoBom([string]$path, [string]$text) { [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding $false)) }

function Say([string]$m) {
    $line = '{0:HH:mm:ss} {1}' -f (Get-Date), $m
    Write-Host $line
    Add-Content -Path $hostLog -Value $line
}

# The user's settings: saved before the run, put back after it (or at the start of the next run if this one was cut off).
function Get-SettingsFiles {
    $r = @(Get-ChildItem -Path $ud -File | Where-Object { $_.Extension -eq '.coc' -or $_.Name -in @('launcher-settings.json', 'continue_game.json') })
    $ms = Join-Path $ud 'ModsSettings'
    if (Test-Path $ms) { $r += @(Get-ChildItem -Path $ms -File -Recurse) }
    return $r
}

function Restore-Settings([string]$why) {
    $pending = Join-Path $saved 'pending.txt'
    if (-not (Test-Path $pending)) { return }
    $list = @(Get-Content (Join-Path $saved 'files.txt'))
    $n = 0
    foreach ($rel in $list) {
        $src = Join-Path $saved "files\$rel"
        $dst = Join-Path $ud $rel
        if (-not (Test-Path -LiteralPath $src)) { continue }
        New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
        Copy-Item -LiteralPath $src -Destination $dst -Force
        $n++
    }
    # settings files the session created (the game writes some on quit)
    $known = @{}; foreach ($rel in $list) { $known[$rel.ToLowerInvariant()] = $true }
    foreach ($f in Get-SettingsFiles) {
        $rel = $f.FullName.Substring($ud.Length + 1)
        if (-not $known.ContainsKey($rel.ToLowerInvariant())) { Remove-Item -LiteralPath $f.FullName -Force; Say "removed new settings file $rel" }
    }
    $ro = Join-Path $ud 'runOnce.txt'; if (Test-Path $ro) { Remove-Item $ro -Force }
    foreach ($f in @('run.json', 'run.active.json')) { $p = Join-Path $suiteData $f; if (Test-Path $p) { Remove-Item $p -Force } }
    Remove-Item $pending -Force
    Say "user settings put back ($n files, $why)"
}

# ───────────── 1. preflight ─────────────
$running = @(Get-Process -Name 'Cities2' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Say ("STOP: Cities2.exe is already running (PID {0}); the suite never touches a session it did not start." -f (($running | ForEach-Object { $_.Id }) -join ', '))
    exit 2
}
Restore-Settings 'left over from an earlier run'

# ───────────── 2. build + deploy ─────────────
Push-Location $repo
try {
    $git = (git rev-parse --short HEAD).Trim()
    $dirty = (git status --porcelain --untracked-files=no)
    if ($dirty) { $git = "$git-dirty" }
    if (-not $NoBuild) {
        Say "building and deploying $git ..."
        $out = & dotnet build Placemaker.csproj -c Release 2>&1
        if ($LASTEXITCODE -ne 0) { $out | Select-Object -Last 30 | ForEach-Object { Say $_ }; Say 'STOP: build failed'; exit 3 }
        Say 'build + deploy ok'
    }
} finally { Pop-Location }
$dll = Join-Path $ud 'Mods\Placemaker\Placemaker.dll'
$dllInfo = Get-Item $dll
Say ("installed Placemaker.dll {0} bytes, {1:yyyy-MM-dd HH:mm:ss}" -f $dllInfo.Length, $dllInfo.LastWriteTime)

# ───────────── 3. save the user's settings, small window ─────────────
if (Test-Path (Join-Path $saved 'files')) { Remove-Item (Join-Path $saved 'files') -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $saved 'files') | Out-Null
$rels = @()
foreach ($f in Get-SettingsFiles) {
    $rel = $f.FullName.Substring($ud.Length + 1)
    $dst = Join-Path $saved "files\$rel"
    New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
    Copy-Item -LiteralPath $f.FullName -Destination $dst -Force
    $rels += $rel
}
Set-Content -Path (Join-Path $saved 'files.txt') -Value $rels -Encoding UTF8
Set-Content -Path (Join-Path $saved 'pending.txt') -Value $RunId -Encoding ASCII
Say ("saved {0} settings files" -f $rels.Count)

$w, $h = $Window -split 'x'
if (-not $KeepGraphics) {
    $ls = Join-Path $ud 'launcher-settings.json'
    if (Test-Path $ls) {
        $j = Get-Content $ls -Raw | ConvertFrom-Json
        $j.system.display_mode = 'windowed'
        $j.system.windowed_resolution = $Window
        $j.system.vsync = $false
        Write-NoBom $ls ($j | ConvertTo-Json -Depth 5)
    }
    $sc = Join-Path $ud 'Settings.coc'
    if (Test-Path $sc) {
        $t = Get-Content $sc -Raw
        $t = $t -replace '"masterVolume":\s*[0-9.]+', '"masterVolume": 0'
        $t = $t -replace '"vSync":\s*true', '"vSync": false'
        $t = $t -replace '"displayMode":\s*"[A-Za-z]+"', '"displayMode": "Window"'
        $t = $t -replace '"volumetricCloudsEnabled":\s*true', '"volumetricCloudsEnabled": false'
        $t = $t -replace '("enabled":\s*)true(,\s*"isAdaptive")', '${1}false$2'
        $t = $t -replace '("enabled":\s*)true(,\s*"nearSampleCount")', '${1}false$2'
        $t = $t -replace '("enabled":\s*)true(,\s*"sampleCount")', '${1}false$2'
        Write-NoBom $sc $t
    }
    Say "suite profile: $Window window, vsync off, no DOF/motion blur/dynamic resolution/volumetric clouds, muted"
}

# ───────────── arm ─────────────
New-Item -ItemType Directory -Force -Path $suiteData | Out-Null
$setName = if ($Set -eq 'bootstrap') { 'smoke' } else { $Set }
$scenarioFile = Join-Path $repo "suite\scenarios\$setName.json"
$cfg = [ordered]@{
    runId = $RunId; set = $Set; outDir = $runDir; scenarioFile = $scenarioFile; git = $git
    expiresUtc = (Get-Date).ToUniversalTime().AddMinutes(25).ToString('o')
    speed = 4; pregrowSeconds = $PregrowSeconds; growSeconds = $GrowSeconds; maxScenarios = $MaxScenarios
    noSwitching = [bool]$NoSwitching; fixtureOnly = ($Set -eq 'bootstrap'); quit = $true; shotWidth = [int]$w; shotHeight = [int]$h; noTerrain = [bool]$NoTerrain
}
foreach ($kv in ($Opt -split ';' | Where-Object { $_ -match '=' })) {
    $k, $v = $kv -split '=', 2
    $k = $k.Trim(); $v = $v.Trim()
    if ($v -match '^(true|false)$') { $cfg[$k] = ($v -eq 'true') } elseif ($v -match '^-?[0-9.]+$') { $cfg[$k] = [double]$v } else { $cfg[$k] = $v }
}
Write-NoBom (Join-Path $suiteData 'run.json') ($cfg | ConvertTo-Json -Depth 3)
Say ("run.json: " + (($cfg.GetEnumerator() | Where-Object { $_.Key -notin @('outDir', 'scenarioFile', 'expiresUtc') } | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ' '))
# Order matters on the real command line: -disableModding also sets disableCodeModding, the later '-disableCodeModding-'
# clears it again. runOnce.txt is merged through a HashSet (order not kept), so for route A it carries only the token.
$modFlags = if ($AllMods) { '' } else { '-disableModding -disableCodeModding- ' }
$windowFlags = "-screen-fullscreen 0 -screen-width $w -screen-height $h"
$gameArgs = "$modFlags-noSplash $windowFlags -placemakerSuite=$RunId"
$runOnce = if ($Route -eq 'A') { "-placemakerSuite=$RunId" } else { "$modFlags-noSplash -placemakerSuite=$RunId" }
Set-Content -Path (Join-Path $ud 'runOnce.txt') -Value $runOnce -Encoding ASCII -NoNewline

# ───────────── launch ─────────────
$launchedAt = Get-Date
$proc = $null
$status = 'not started'; $phaseTimes = [ordered]@{}; $ourPid = 0
try {
    if ($Route -eq 'A') {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $exe
        $psi.Arguments = $gameArgs
        $psi.WorkingDirectory = $gameDir
        $psi.UseShellExecute = $false
        $psi.EnvironmentVariables['SteamAppId'] = '949230'
        $psi.EnvironmentVariables['SteamGameId'] = '949230'
        $proc = [System.Diagnostics.Process]::Start($psi)
        Say "route A: started Cities2.exe PID $($proc.Id) $gameArgs"
    } else {
        Start-Process 'steam://rungameid/949230'
        Say 'route B: asked Steam to start the game (runOnce.txt carries the flags)'
    }

    $bootLimit = 420; $staleLimit = 240
    if ($TimeoutMin -le 0) { $TimeoutMin = switch ($Set) { 'bootstrap' { 15 } 'smoke' { 25 } 'core' { 75 } 'full' { 75 } } }
    $hb = Join-Path $runDir 'heartbeat.json'
    $ourPid = if ($proc) { $proc.Id } else { 0 }
    $lastBeat = $null; $lastBeatChange = Get-Date; $lastPhase = ''; $status = 'running'
    while ($true) {
        Start-Sleep -Seconds 2
        $now = Get-Date
        # route B: adopt the Cities2 process started after our launch
        if ($ourPid -eq 0 -or -not (Get-Process -Id $ourPid -ErrorAction SilentlyContinue)) {
            $cand = @(Get-Process -Name 'Cities2' -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -ge $launchedAt.AddSeconds(-2) })
            if ($cand.Count -gt 0 -and $cand[0].Id -ne $ourPid) {
                Say "adopting Cities2 PID $($cand[0].Id) (started $($cand[0].StartTime))"
                $ourPid = $cand[0].Id
            }
        }
        $alive = $ourPid -ne 0 -and (Get-Process -Id $ourPid -ErrorAction SilentlyContinue)
        $beat = $null
        if (Test-Path $hb) { try { $beat = Get-Content $hb -Raw | ConvertFrom-Json } catch { } }
        if ($beat) {
            if ($beat.utc -ne $lastBeat) { $lastBeat = $beat.utc; $lastBeatChange = $now }
            if ($beat.phase -ne $lastPhase) {
                $lastPhase = $beat.phase
                $phaseTimes[$beat.phase] = [math]::Round(($now - $launchedAt).TotalSeconds, 1)
                Say ("phase {0} at {1:F0} s (game says {2} s since process start)" -f $beat.phase, ($now - $launchedAt).TotalSeconds, $beat.sinceStart)
            }
        }
        if (-not $alive) {
            if ($lastPhase -eq 'quit') { $status = 'quit'; Say 'game exited after the clean quit' }
            elseif (($now - $launchedAt).TotalSeconds -lt 60 -and -not $beat) { continue }   # a relaunch through Steam gets adopted above
            else { $status = 'crashed'; Say "game process gone in phase '$lastPhase' without the clean quit" }
            break
        }
        $elapsed = ($now - $launchedAt).TotalSeconds
        if (-not $beat -and $elapsed -gt $bootLimit) { $status = 'boot-timeout'; Say "no heartbeat after $bootLimit s"; break }
        if ($beat -and ($now - $lastBeatChange).TotalSeconds -gt $staleLimit) { $status = 'hung'; Say "heartbeat stale for $staleLimit s in phase '$lastPhase' (scenario $($beat.scenario))"; break }
        if ($elapsed -gt $TimeoutMin * 60) { $status = 'timeout'; Say "overall timeout ($TimeoutMin min)"; break }
        if ($beat -and $beat.phase -eq 'quit') {
            # give the game's own quit time to finish
            $quitAt = Get-Date
            while ((Get-Process -Id $ourPid -ErrorAction SilentlyContinue) -and ((Get-Date) - $quitAt).TotalSeconds -lt 90) { Start-Sleep -Seconds 1 }
            if (Get-Process -Id $ourPid -ErrorAction SilentlyContinue) { $status = 'quit-timeout'; Say 'clean quit did not finish in 90 s' }
            else { $status = 'quit'; Say ("game exited cleanly {0:F0} s after the quit request" -f ((Get-Date) - $quitAt).TotalSeconds) }
            break
        }
    }
    if ($ourPid -ne 0 -and (Get-Process -Id $ourPid -ErrorAction SilentlyContinue)) {
        Say "killing PID $ourPid (status $status)"
        Stop-Process -Id $ourPid -Force
        Start-Sleep -Seconds 3
    }
}
finally {
    # ───────────── collect logs, put the settings back ─────────────
    $logs = Join-Path $runDir 'logs'
    New-Item -ItemType Directory -Force -Path $logs | Out-Null
    foreach ($f in @('Player.log')) { $p = Join-Path $ud $f; if (Test-Path $p) { Copy-Item $p $logs -Force } }
    Get-ChildItem (Join-Path $ud 'Logs') -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $launchedAt.AddSeconds(-5) } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $logs -Force }
    Restore-Settings "after run $RunId"
}

$total = ((Get-Date) - $t0).TotalSeconds
$hostInfo = [ordered]@{ runId = $RunId; set = $Set; route = $Route; git = $git; status = $status; launchedAt = $launchedAt.ToString('o'); totalSec = [math]::Round($total, 1); phaseAtSec = $phaseTimes; pid = $ourPid }
($hostInfo | ConvertTo-Json -Depth 4) | Set-Content -Path (Join-Path $runDir 'host.json') -Encoding UTF8
$md = Join-Path $runDir 'summary.md'
$extra = @('', '## Host', "status **$status**, route $Route, total $([math]::Round($total)) s (build, boot, run, quit)", "phases (s after launch): $(($phaseTimes.GetEnumerator() | ForEach-Object { "$($_.Key) $($_.Value)" }) -join ', ')")
Add-Content -Path $md -Value $extra
Say "done: $status in $([math]::Round($total)) s -> $runDir"
if ($status -ne 'quit') { exit 1 }
exit 0
