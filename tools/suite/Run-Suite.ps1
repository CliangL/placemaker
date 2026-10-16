<#
.SYNOPSIS
  Runs the Placemaker in-game test suite: one game launch, a new game on Plains reshaped into a test grid, many
  scenarios, settled screenshots, a contact sheet.

.DESCRIPTION
  powershell -File tools/suite/Run-Suite.ps1 -Set core
  powershell -File tools/suite/Run-Suite.ps1 -Set core -Queue          # keep the game open for Send-Suite afterwards
  powershell -File tools/suite/Run-Suite.ps1 -Set queue                # fixture only, then the queue
  powershell -File tools/suite/Run-Suite.ps1 -Set path\to\my-set.json  # any set file
  powershell -File tools/suite/Run-Suite.ps1 -RestoreOnly              # put the user's settings back after a cut-off run

  1. Takes the suite lock (.forensics/suite/lock); refuses while another Run-Suite holds it. If a Cities2.exe is running:
     when it is a suite game this host left behind (its command line has -placemakerSuite), asks it to quit through its
     inbox and waits; any other game is the user's and the run stops (exit 2) — it is never touched.
  2. Builds and deploys this worktree's mod (Mods/Placemaker; it stays installed) and labels the run with the git hash
     and the SHA-256 of the deployed DLL (the game checks it loaded that DLL). A dirty tree's diff is saved in the run.
  3. Saves the user's settings (root *.coc, launcher-settings.json, continue_game.json, ModsSettings/, the top-level
     files of ModsData/Placemaker), switches to the suite profile (window at the shot size, no vsync, muted, full detail
     without LOD cross-fade, no clouds or cloud shadows), arms the run and starts Cities2.exe with only local mods.
  4. Watches the heartbeat; the mod quits through the game's own quit. Only the PID this script started is killed, and
     only when the clean quit did not happen in time or the heartbeat stopped.
  5. Collects the logs, classifies how the game ended (quit / closed-externally / crashed / killed), puts the user's
     settings back, writes the status at the top of summary.md, and prints which Placemaker.dll stays installed.
  Results: .forensics/suite/runs/<runId>/ (summary.md, summary.json, contact.html, scenarios/*.json, shots/*.png, logs/).
#>
param(
    # smoke | core | full | steps | examples | any suite/scenarios/<name>.json or a path to a set file; bootstrap = fixture only; queue = no set
    [string]$Set = 'smoke',
    [string]$RunId = '',
    [switch]$NoBuild,
    [int]$TimeoutMin = 0,
    [int]$MaxScenarios = 0,
    [switch]$NoSwitching,
    # Only Placemaker loads (-disableModding -disableCodeModding-). -AllMods loads the user's whole playset (slow boot).
    [switch]$AllMods,
    [switch]$KeepGraphics,
    [switch]$NoTerrain,
    [string]$Window = '1600x900',
    [float]$GrowSeconds = 100,
    [float]$PregrowSeconds = 75,
    [string]$Opt = '',
    [switch]$Queue,
    # Grid size in blocks, e.g. '14x9' (default: sized for the set).
    [string]$Grid = '',
    # Keep the user's Placemaker options (default: Placemaker's defaults are pinned for the run, and recorded).
    [switch]$UserModSettings,
    # suite = readable deterministic look (noon, fixed exposure, no weather/wind/icons); game = the map's own.
    [ValidateSet('suite', 'game')][string]$Look = 'suite',
    [ValidateSet('human', 'settled', 'user')][string]$Pace = 'human',
    # Compare with an earlier run when done (writes compare-<Baseline>.md).
    [string]$Baseline = '',
    [switch]$RestoreOnly,
    [string]$ResultsRoot = 'C:\Users\jack\cs2-area-tool\.forensics\suite\runs'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$gameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II'
$exe = Join-Path $gameDir 'Cities2.exe'
$ud = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
$suiteData = Join-Path $ud 'ModsData\Placemaker\suite'
$modsData = Join-Path $ud 'ModsData\Placemaker'
$forensics = Split-Path $ResultsRoot
$saved = Join-Path $forensics 'user-settings'
$lockFile = Join-Path $forensics 'lock'
$activeFile = Join-Path $forensics 'active.json'
New-Item -ItemType Directory -Force -Path $ResultsRoot | Out-Null

function Write-NoBom([string]$path, [string]$text) { [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding $false)) }

$script:hostLog = $null
function Say([string]$m) {
    $line = '{0:HH:mm:ss} {1}' -f (Get-Date), $m
    Write-Host $line
    if ($script:hostLog) { Add-Content -Path $script:hostLog -Value $line }
}

# The user's settings and Placemaker data: saved before the run, put back after it (or at the start of the next run).
function Get-SettingsFiles {
    $r = @(Get-ChildItem -Path $ud -File | Where-Object { $_.Extension -eq '.coc' -or $_.Name -in @('launcher-settings.json', 'continue_game.json') })
    $ms = Join-Path $ud 'ModsSettings'
    if (Test-Path $ms) { $r += @(Get-ChildItem -Path $ms -File -Recurse) }
    if (Test-Path $modsData) { $r += @(Get-ChildItem -Path $modsData -File) }   # catalog-dump.json, remembered lists (top level only)
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
    $known = @{}; foreach ($rel in $list) { $known[$rel.ToLowerInvariant()] = $true }
    foreach ($f in Get-SettingsFiles) {
        $rel = $f.FullName.Substring($ud.Length + 1)
        if (-not $known.ContainsKey($rel.ToLowerInvariant())) { Remove-Item -LiteralPath $f.FullName -Force; Say "removed new settings file $rel" }
    }
    $ro = Join-Path $ud 'runOnce.txt'; if (Test-Path $ro) { Remove-Item $ro -Force }
    foreach ($f in @('run.json', 'run.active.json')) { $p = Join-Path $suiteData $f; if (Test-Path $p) { Remove-Item $p -Force } }
    Remove-Item $pending -Force
    Say "user settings and Placemaker data put back ($n files, $why)"
}

function Release-Lock { if (Test-Path $lockFile) { try { $l = Get-Content $lockFile -Raw | ConvertFrom-Json; if ($l.pid -eq $PID) { Remove-Item $lockFile -Force } } catch { } } }

if ($RestoreOnly) { Restore-Settings 'restore only'; exit 0 }

# ───────────── 1. lock and preflight ─────────────
if (Test-Path $lockFile) {
    $l = $null; try { $l = Get-Content $lockFile -Raw | ConvertFrom-Json } catch { }
    if ($l -and (Get-Process -Id $l.pid -ErrorAction SilentlyContinue)) { Write-Host "STOP: another Run-Suite (PID $($l.pid), run $($l.runId)) holds the suite lock"; exit 2 }
    Remove-Item $lockFile -Force
}
try {
    $fs = [System.IO.File]::Open($lockFile, 'CreateNew', 'Write')
    $bytes = [System.Text.Encoding]::UTF8.GetBytes((@{ pid = $PID; runId = $RunId; start = (Get-Date).ToString('o') } | ConvertTo-Json -Compress))
    $fs.Write($bytes, 0, $bytes.Length); $fs.Close()
} catch { Write-Host "STOP: could not take the suite lock: $($_.Exception.Message)"; exit 2 }

try {
if (-not $RunId) { $RunId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + ([IO.Path]::GetFileNameWithoutExtension($Set)) }
$runDir = Join-Path $ResultsRoot $RunId
if (Test-Path $runDir) {
    $old = "$runDir.old-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Move-Item $runDir $old
    Write-Host "run folder $RunId existed: moved to $old"
}
New-Item -ItemType Directory -Force -Path $runDir | Out-Null
$script:hostLog = Join-Path $runDir 'host.log'
$t0 = Get-Date

$running = @(Get-Process -Name 'Cities2' -ErrorAction SilentlyContinue)
foreach ($p in $running) {
    $cmd = ''
    try { $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)").CommandLine } catch { }
    if ($cmd -match '-placemakerSuite=(\S+)') {
        $orphanRun = $Matches[1]
        Say "a suite game this host left behind is running (PID $($p.Id), run $orphanRun): asking it to quit through its inbox"
        $inbox = Join-Path (Join-Path $ResultsRoot $orphanRun) 'inbox'
        if (Test-Path $inbox) { Write-NoBom (Join-Path $inbox ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-quit.json')) '{"command":"quit"}' }
        $w0 = Get-Date
        while ((Get-Process -Id $p.Id -ErrorAction SilentlyContinue) -and ((Get-Date) - $w0).TotalSeconds -lt 180) { Start-Sleep -Seconds 2 }
        if (Get-Process -Id $p.Id -ErrorAction SilentlyContinue) { Say "STOP: the orphaned suite game (PID $($p.Id)) did not quit within 180 s; it was not killed"; exit 2 }
        Say 'the orphaned suite game quit'
    } else {
        Say ("STOP: Cities2.exe is running (PID {0}) and it is not a suite game; the suite never touches the user's session." -f $p.Id)
        exit 2
    }
}
Restore-Settings 'left over from an earlier run'

# ───────────── 2. build + deploy, identity ─────────────
Push-Location $repo
try {
    $git = (git rev-parse --short HEAD).Trim()
    $dirty = (git status --porcelain --untracked-files=no)
    if ($dirty) {
        $git = "$git-dirty"
        git diff HEAD | Out-File -FilePath (Join-Path $runDir 'dirty.patch') -Encoding utf8
        Say "the tree is dirty: its diff is saved as dirty.patch"
    }
    if (-not $NoBuild) {
        Say "building and deploying $git ..."
        $out = & dotnet build Placemaker.csproj -c Release 2>&1
        if ($LASTEXITCODE -ne 0) { $out | Select-Object -Last 30 | ForEach-Object { Say $_ }; Say 'STOP: build failed'; exit 3 }
        Say 'build + deploy ok'
    }
} finally { Pop-Location }
$dll = Join-Path $ud 'Mods\Placemaker\Placemaker.dll'
$dllInfo = Get-Item $dll
$dllHash = (Get-FileHash $dll -Algorithm SHA256).Hash.Substring(0, 8).ToLowerInvariant()
$label = "$git+$dllHash"
Say ("installed Placemaker.dll {0} bytes, {1:yyyy-MM-dd HH:mm:ss}, sha256 {2}" -f $dllInfo.Length, $dllInfo.LastWriteTime, $dllHash)

# ───────────── 3. save the user's settings, suite profile ─────────────
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
Say ("saved {0} settings and data files" -f $rels.Count)
$auditBefore = @{}
$auditDir = Join-Path $modsData 'audit'
if (Test-Path $auditDir) { Get-ChildItem $auditDir -File | ForEach-Object { $auditBefore[$_.Name] = $true } }

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
        $t = $t -replace '"distanceCloudsEnabled":\s*true', '"distanceCloudsEnabled": false'
        $t = $t -replace '"distanceCloudsShadows":\s*true', '"distanceCloudsShadows": false'
        $t = $t -replace '"lodCrossFade":\s*true', '"lodCrossFade": false'
        $t = $t -replace '"edgeScrolling":\s*true', '"edgeScrolling": false'
        $t = $t -replace '"levelOfDetail":\s*[0-9.]+', '"levelOfDetail": 1'
        $t = $t -replace '"resolution":\s*\{[^}]*\}', ('"resolution": {"width": ' + $w + ', "height": ' + $h + '}')
        $t = $t -replace '("enabled":\s*)true(,\s*"isAdaptive")', '${1}false$2'
        $t = $t -replace '("enabled":\s*)true(,\s*"nearSampleCount")', '${1}false$2'
        $t = $t -replace '("enabled":\s*)true(,\s*"sampleCount")', '${1}false$2'
        Write-NoBom $sc $t
    }
    Say "suite profile: $Window window, vsync off, full detail without LOD cross-fade, no clouds or cloud shadows, no DOF/motion blur/dynamic resolution, muted"
}

# ───────────── arm ─────────────
New-Item -ItemType Directory -Force -Path $suiteData | Out-Null
$setName = if ($Set -in @('bootstrap', 'queue')) { 'smoke' } else { $Set }
$scenarioFile = if ($setName -match "[\/.]" -and (Test-Path $setName -PathType Leaf)) { (Resolve-Path $setName).Path } else { Join-Path $repo "suite\scenarios\$setName.json" }
if (-not (Test-Path $scenarioFile)) { Say "STOP: no scenario set $scenarioFile"; Restore-Settings 'stopped'; exit 2 }
$schemaFile = Join-Path $repo 'suite\schema.json'
$cfg = [ordered]@{
    runId = $RunId; set = $Set; outDir = $runDir; scenarioFile = $scenarioFile; schemaFile = $schemaFile; git = $git; dllHash = $dllHash
    expiresUtc = (Get-Date).ToUniversalTime().AddMinutes(25).ToString('o')
    speed = 4; pregrowSeconds = $PregrowSeconds; growSeconds = $GrowSeconds; maxScenarios = $MaxScenarios
    noSwitching = [bool]$NoSwitching; fixtureOnly = ($Set -eq 'bootstrap'); quit = $true; shotWidth = [int]$w; shotHeight = [int]$h; noTerrain = [bool]$NoTerrain; queue = [bool]$Queue
    userModSettings = [bool]$UserModSettings; look = $Look; pace = $Pace; profile = $(if ($AllMods) { 'full' } else { 'clean' })
}
if ($Grid -match '^(\d+)x(\d+)$') { $cfg['nx'] = [int]$Matches[1]; $cfg['nz'] = [int]$Matches[2] }
foreach ($kv in ($Opt -split ';' | Where-Object { $_ -match '=' })) {
    $k, $v = $kv -split '=', 2
    $k = $k.Trim(); $v = $v.Trim()
    if ($v -match '^(true|false)$') { $cfg[$k] = ($v -eq 'true') } elseif ($v -match '^-?[0-9.]+$') { $cfg[$k] = [double]$v } else { $cfg[$k] = $v }
}
Write-NoBom (Join-Path $suiteData 'run.json') ($cfg | ConvertTo-Json -Depth 3)
Say ("run.json: " + (($cfg.GetEnumerator() | Where-Object { $_.Key -notin @('outDir', 'scenarioFile', 'schemaFile', 'expiresUtc') } | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ' '))
$modFlags = if ($AllMods) { '' } else { '-disableModding -disableCodeModding- ' }
$windowFlags = "-screen-fullscreen 0 -screen-width $w -screen-height $h"
$gameArgs = "$modFlags-noSplash $windowFlags -placemakerSuite=$RunId"
Set-Content -Path (Join-Path $ud 'runOnce.txt') -Value "-placemakerSuite=$RunId" -Encoding ASCII -NoNewline

# ───────────── launch ─────────────
$launchedAt = Get-Date
$proc = $null
$status = 'not started'; $phaseTimes = [ordered]@{}; $ourPid = 0; $lastPhase = ''
try {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = $gameArgs
    $psi.WorkingDirectory = $gameDir
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables['SteamAppId'] = '949230'
    $psi.EnvironmentVariables['SteamGameId'] = '949230'
    $proc = [System.Diagnostics.Process]::Start($psi)
    $ourPid = $proc.Id
    Write-NoBom $activeFile (@{ pid = $ourPid; runId = $RunId; start = $launchedAt.ToString('o'); runDir = $runDir } | ConvertTo-Json -Compress)
    Say "started Cities2.exe PID $ourPid $gameArgs"

    $bootLimit = if ($AllMods) { 600 } else { 240 }
    $staleLimit = if ($AllMods) { 240 } else { 120 }
    if ($TimeoutMin -le 0) { $TimeoutMin = switch ($Set) { 'bootstrap' { 15 } 'smoke' { 30 } 'queue' { 480 } default { 90 } } }
    if ($Queue -and $TimeoutMin -lt 480) { $TimeoutMin = 480 }
    $hb = Join-Path $runDir 'heartbeat.json'
    $lastBeat = $null; $lastBeatChange = Get-Date; $everBeat = $false; $status = 'running'
    while ($true) {
        Start-Sleep -Seconds 2
        $now = Get-Date
        $alive = [bool](Get-Process -Id $ourPid -ErrorAction SilentlyContinue)
        $beat = $null
        if (Test-Path $hb) {
            try { $beat = Get-Content $hb -Raw -ErrorAction Stop | ConvertFrom-Json } catch { $beat = $null }
            if ($beat -and ($beat.runId -ne $RunId -or ($beat.pid -and $beat.pid -ne $ourPid))) { $beat = $null }
        }
        if ($beat) {
            $everBeat = $true
            if ($beat.utc -ne $lastBeat) { $lastBeat = $beat.utc; $lastBeatChange = $now }
            if ($beat.phase -ne $lastPhase) {
                $lastPhase = $beat.phase
                $phaseTimes[$beat.phase] = [math]::Round(($now - $launchedAt).TotalSeconds, 1)
                Say ("phase {0} at {1:F0} s (game says {2} s since process start)" -f $beat.phase, ($now - $launchedAt).TotalSeconds, $beat.sinceStart)
            }
        }
        if (-not $alive) {
            if ($lastPhase -eq 'quit') { $status = 'quit'; Say 'game exited after the clean quit' }
            else { $status = 'gone'; Say "game process gone in phase '$lastPhase' without the suite's quit" }
            break
        }
        $elapsed = ($now - $launchedAt).TotalSeconds
        if (-not $everBeat -and $elapsed -gt $bootLimit) { $status = 'boot-timeout'; Say "no heartbeat after $bootLimit s"; break }
        if ($everBeat -and ($now - $lastBeatChange).TotalSeconds -gt $staleLimit) { $status = 'hung'; Say "heartbeat stale for $staleLimit s in phase '$lastPhase'"; break }
        if ($elapsed -gt $TimeoutMin * 60) { $status = 'timeout'; Say "overall timeout ($TimeoutMin min)"; break }
        if ($beat -and $beat.phase -eq 'quit') {
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
        if ($status -eq 'running') { $status = 'killed' }
    }
}
finally {
    # ───────────── collect logs, classify, put the settings back ─────────────
    $logs = Join-Path $runDir 'logs'
    New-Item -ItemType Directory -Force -Path $logs | Out-Null
    foreach ($f in @('Player.log')) { $p = Join-Path $ud $f; if (Test-Path $p) { Copy-Item $p $logs -Force } }
    Get-ChildItem (Join-Path $ud 'Logs') -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $launchedAt.AddSeconds(-5) } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $logs -Force }
    if (Test-Path $auditDir) {
        $moved = 0
        Get-ChildItem $auditDir -File | Where-Object { -not $auditBefore.ContainsKey($_.Name) } | ForEach-Object { New-Item -ItemType Directory -Force -Path (Join-Path $runDir 'audit') | Out-Null; Move-Item $_.FullName (Join-Path $runDir 'audit') -Force; $moved++ }
        if ($moved -gt 0) { Say "moved $moved build audit files the run wrote into the run folder" }
    }
    if ($status -eq 'gone') {
        $player = Join-Path $logs 'Player.log'
        $crashDir = Join-Path $env:TEMP 'Colossal Order\Cities Skylines II\Crashes'
        $newCrash = (Test-Path $crashDir) -and @(Get-ChildItem $crashDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $launchedAt }).Count -gt 0
        $text = if (Test-Path $player) { Get-Content $player -Raw } else { '' }
        if ($newCrash -or $text -match 'Crash!!!') { $status = 'crashed' }
        elseif ($text -match 'Game terminated successfully') { $status = 'closed-externally' }
        else { $status = 'unknown-exit' }
        Say "classified the exit: $status"
    }
    Restore-Settings "after run $RunId"
    if (Test-Path $activeFile) { Remove-Item $activeFile -Force }
}

$total = ((Get-Date) - $t0).TotalSeconds
$hostInfo = [ordered]@{ runId = $RunId; set = $Set; git = $git; dllHash = $dllHash; label = $label; status = $status; launchedAt = $launchedAt.ToString('o'); totalSec = [math]::Round($total, 1); phaseAtSec = $phaseTimes; pid = $ourPid; profile = $cfg.profile }
($hostInfo | ConvertTo-Json -Depth 4) | Set-Content -Path (Join-Path $runDir 'host.json') -Encoding UTF8
# the catalog of types, presets and options this build offers (for scenario writers)
$cat = Join-Path $runDir 'catalog.json'
if (Test-Path $cat) { Copy-Item $cat (Join-Path $repo 'suite\catalog.json') -Force }
$md = Join-Path $runDir 'summary.md'
$top = @("> **Host: $status** (label $label, profile $($cfg.profile), total $([math]::Round($total)) s; phases s after launch: $(($phaseTimes.GetEnumerator() | ForEach-Object { "$($_.Key) $($_.Value)" }) -join ', '))")
if ($status -ne 'quit') { $top += "> **The run did not end with the suite's own quit: results after phase '$lastPhase' are missing.**" }
$top += ''
$body = if (Test-Path $md) { Get-Content $md -Raw } else { "# Suite run $RunId`r`n(no summary written by the game)`r`n" }
Write-NoBom $md (($top -join "`r`n") + "`r`n" + $body)
Say "done: $status in $([math]::Round($total)) s -> $runDir"
Say "installed now: Mods/Placemaker/Placemaker.dll $label ($([math]::Round($dllInfo.Length / 1KB)) KB) - the user's normal game loads this build"
if ($Baseline) { & (Join-Path $PSScriptRoot 'Compare-Suite.ps1') -Old $Baseline -New $RunId -ResultsRoot $ResultsRoot }
if ($status -ne 'quit') { exit 1 }
exit 0
}
finally { Release-Lock }
