<#
.SYNOPSIS
  Runs the Placemaker in-game test suite: one game launch, a flat new world, a road grid, many scenarios, screenshots.

.DESCRIPTION
  powershell -File tools/suite/Run-Suite.ps1 -Set core

  1. Refuses to start while a Cities2.exe it did not start is running (the user's session is never touched).
  2. Builds and deploys this worktree's mod into Mods/Placemaker (it stays installed afterwards).
  3. Backs up the files a session rewrites (root *.coc, launcher-settings.json, continue_game.json, ModsSettings,
     ModsData/Placemaker, Player*.log, Logs/), swaps in a light window/graphics profile, arms the run
     (ModsData/Placemaker/suite/run.json + runOnce.txt) and starts Cities2.exe directly (route A:
     -disablePdxSdk -noSplash -placemakerSuite=<runId>, SteamAppId set). Route B goes through steam://rungameid.
  4. Watches the heartbeat; the mod quits the game itself (GameManager.QuitGame). Only the PID this script started is
     ever killed, and only when the clean quit did not happen in time or the heartbeat stopped.
  5. Copies the session logs into the run folder, restores every backed-up file and verifies the hashes.
  Results: .forensics/suite/runs/<runId>/ (summary.json, summary.md, scenarios/*.json, shots/*.png, logs/, restore.txt).
#>
param(
    [ValidateSet('smoke', 'core', 'full', 'bootstrap')][string]$Set = 'smoke',
    [string]$RunId = '',
    [switch]$NoBuild,
    [ValidateSet('A', 'B')][string]$Route = 'A',
    [int]$TimeoutMin = 0,
    [int]$MaxScenarios = 0,
    [switch]$NoSwitching,
    [switch]$KeepGraphics,
    [float]$GrowSeconds = 100,
    [float]$PregrowSeconds = 75,
    [string]$ResultsRoot = 'C:\Users\jack\cs2-area-tool\.forensics\suite\runs'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$gameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II'
$exe = Join-Path $gameDir 'Cities2.exe'
$ud = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
$suiteData = Join-Path $ud 'ModsData\Placemaker\suite'
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

# ───────────── 1. preflight ─────────────
$running = @(Get-Process -Name 'Cities2' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Say ("STOP: Cities2.exe is already running (PID {0}); the suite never touches a session it did not start." -f (($running | ForEach-Object { $_.Id }) -join ', '))
    exit 2
}
$launcher = @(Get-Process -Name 'Paradox Launcher' -ErrorAction SilentlyContinue)
if ($launcher.Count -gt 0) { Say 'STOP: the Paradox launcher is open (the user may be about to play).'; exit 2 }

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

# ───────────── 3. backup ─────────────
$backup = Join-Path $runDir 'backup'
New-Item -ItemType Directory -Force -Path $backup | Out-Null
$items = New-Object System.Collections.ArrayList
Get-ChildItem -Path $ud -File | Where-Object { $_.Extension -eq '.coc' -or $_.Name -in @('launcher-settings.json', 'continue_game.json', 'Player.log', 'Player-prev.log') } | ForEach-Object { [void]$items.Add($_.FullName) }
foreach ($dir in @('ModsSettings', 'ModsData\Placemaker', 'Logs')) {
    $p = Join-Path $ud $dir
    if (Test-Path $p) {
        Get-ChildItem -Path $p -File -Recurse | Where-Object { $_.FullName -notlike "$suiteData*" } | ForEach-Object { [void]$items.Add($_.FullName) }
    }
}
if ($Route -eq 'B') {
    foreach ($f in @('launcher-v2.sqlite')) { $p = Join-Path $ud $f; if (Test-Path $p) { [void]$items.Add($p) } }
    $lc = Join-Path $ud '.launcher-cache'
    if (Test-Path $lc) { Get-ChildItem $lc -File -Recurse | ForEach-Object { [void]$items.Add($_.FullName) } }
}
$manifest = @()
foreach ($f in $items) {
    $rel = $f.Substring($ud.Length + 1)
    $dst = Join-Path $backup $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
    Copy-Item -LiteralPath $f -Destination $dst -Force
    $manifest += [pscustomobject]@{ rel = $rel; sha = (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash }
}
$manifest | ConvertTo-Json -Depth 3 | Set-Content -Path (Join-Path $runDir 'backup-manifest.json') -Encoding UTF8
$backupDirs = @('ModsSettings', 'ModsData\Placemaker', 'Logs')
Say ("backed up {0} files" -f $manifest.Count)

# ───────────── profile: small window, no vsync, no blur, silent ─────────────
if (-not $KeepGraphics) {
    $ls = Join-Path $ud 'launcher-settings.json'
    if (Test-Path $ls) {
        $j = Get-Content $ls -Raw | ConvertFrom-Json
        $j.system.display_mode = 'windowed'
        $j.system.windowed_resolution = '1280x720'
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
    Say 'suite profile: 1280x720 window, vsync off, no DOF/motion blur/dynamic resolution/volumetric clouds, muted'
}

# ───────────── arm ─────────────
New-Item -ItemType Directory -Force -Path $suiteData | Out-Null
$setName = if ($Set -eq 'bootstrap') { 'smoke' } else { $Set }
$scenarioFile = Join-Path $repo "suite\scenarios\$setName.json"
$cfg = [ordered]@{
    runId = $RunId; set = $Set; outDir = $runDir; scenarioFile = $scenarioFile; git = $git
    expiresUtc = (Get-Date).ToUniversalTime().AddMinutes(25).ToString('o')
    speed = 4; pregrowSeconds = $PregrowSeconds; growSeconds = $GrowSeconds; maxScenarios = $MaxScenarios
    noSwitching = [bool]$NoSwitching; fixtureOnly = ($Set -eq 'bootstrap'); quit = $true; shotWidth = 1280; shotHeight = 720
}
Write-NoBom (Join-Path $suiteData 'run.json') ($cfg | ConvertTo-Json -Depth 3)
$gameArgs = "-disablePdxSdk -noSplash -placemakerSuite=$RunId"
Set-Content -Path (Join-Path $ud 'runOnce.txt') -Value $gameArgs -Encoding ASCII -NoNewline

$restored = $false
function Restore-All {
    if ($script:restored) { return }
    $script:restored = $true
    Say 'restoring user files ...'
    $report = New-Object System.Collections.ArrayList
    $ro = Join-Path $ud 'runOnce.txt'
    if (Test-Path $ro) { Remove-Item $ro -Force; [void]$report.Add('runOnce.txt was still there: removed') }
    foreach ($f in @('run.json', 'run.active.json')) { $p = Join-Path $suiteData $f; if (Test-Path $p) { Remove-Item $p -Force } }
    # files the session added inside the backed-up folders
    $known = @{}
    foreach ($m in $manifest) { $known[$m.rel.ToLowerInvariant()] = $true }
    foreach ($dir in $backupDirs) {
        $p = Join-Path $ud $dir
        if (-not (Test-Path $p)) { continue }
        Get-ChildItem -Path $p -File -Recurse | Where-Object { $_.FullName -notlike "$suiteData*" } | ForEach-Object {
            $rel = $_.FullName.Substring($ud.Length + 1)
            if (-not $known.ContainsKey($rel.ToLowerInvariant())) { Remove-Item -LiteralPath $_.FullName -Force; [void]$report.Add("removed new file $rel") }
        }
    }
    $changed = 0; $bad = 0
    foreach ($m in $manifest) {
        $src = Join-Path $backup $m.rel
        $dst = Join-Path $ud $m.rel
        $now = if (Test-Path -LiteralPath $dst) { (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash } else { '' }
        if ($now -ne $m.sha) {
            New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
            Copy-Item -LiteralPath $src -Destination $dst -Force
            $changed++
            [void]$report.Add("restored $($m.rel)")
        }
        $after = (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash
        if ($after -ne $m.sha) { $bad++; [void]$report.Add("HASH MISMATCH $($m.rel)") }
    }
    $root = Get-ChildItem -Path $ud -File | Where-Object { $_.Extension -eq '.coc' -or $_.Name -in @('launcher-settings.json', 'continue_game.json') }
    foreach ($r in $root) {
        $rel = $r.Name
        if (-not $known.ContainsKey($rel.ToLowerInvariant())) { Remove-Item -LiteralPath $r.FullName -Force; [void]$report.Add("removed new root file $rel") }
    }
    $line = "restore: $($manifest.Count) files checked, $changed restored, $bad hash mismatches"
    [void]$report.Insert(0, $line)
    $report | Set-Content -Path (Join-Path $runDir 'restore.txt') -Encoding UTF8
    Say $line
}

# ───────────── launch ─────────────
$launchedAt = Get-Date
$proc = $null
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
    $lastBeat = $null; $lastBeatChange = Get-Date; $lastPhase = ''; $status = 'running'; $phaseTimes = [ordered]@{}
    while ($true) {
        Start-Sleep -Seconds 3
        $now = Get-Date
        # route A relaunched through Steam, or route B: adopt the Cities2 process started after our launch
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
            elseif (($now - $launchedAt).TotalSeconds -lt 60 -and $Route -eq 'A') { continue }
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
            while ((Get-Process -Id $ourPid -ErrorAction SilentlyContinue) -and ((Get-Date) - $quitAt).TotalSeconds -lt 90) { Start-Sleep -Seconds 2 }
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
    # ───────────── collect logs, restore ─────────────
    $logs = Join-Path $runDir 'logs'
    New-Item -ItemType Directory -Force -Path $logs | Out-Null
    foreach ($f in @('Player.log')) { $p = Join-Path $ud $f; if (Test-Path $p) { Copy-Item $p $logs -Force } }
    foreach ($f in @('Placemaker.log', 'SceneFlow.log', 'Modding.log', 'UI.log', 'Prefabs.log')) { $p = Join-Path $ud "Logs\$f"; if (Test-Path $p) { Copy-Item $p $logs -Force } }
    Restore-All
}

$total = ((Get-Date) - $t0).TotalSeconds
$hostInfo = [ordered]@{ runId = $RunId; set = $Set; route = $Route; git = $git; status = $status; launchedAt = $launchedAt.ToString('o'); totalSec = [math]::Round($total, 1); phaseAtSec = $phaseTimes; pid = $ourPid }
($hostInfo | ConvertTo-Json -Depth 4) | Set-Content -Path (Join-Path $runDir 'host.json') -Encoding UTF8
$md = Join-Path $runDir 'summary.md'
$extra = @('', '## Host', "status **$status**, route $Route, total $([math]::Round($total)) s (build, backup, boot, run, quit, restore)", "phases (s after launch): $(($phaseTimes.GetEnumerator() | ForEach-Object { "$($_.Key) $($_.Value)" }) -join ', ')", (Get-Content (Join-Path $runDir 'restore.txt') -TotalCount 1))
Add-Content -Path $md -Value $extra
Say "done: $status in $([math]::Round($total)) s -> $runDir"
if ($status -ne 'quit') { exit 1 }
exit 0
