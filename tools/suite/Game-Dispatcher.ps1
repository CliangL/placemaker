<#
.SYNOPSIS
  The game queue's dispatcher: one per machine, started on demand by a client (Run-Suite, Show, Send-Suite, Play),
  gone again when the line has been empty for a while. Nobody runs it by hand.

.DESCRIPTION
  Works the line in %USERPROFILE%\.placemaker\game-queue (see GameQueue.ps1):
  * Single instance: dispatcher.lock, held open for its whole life (its PID and start time inside; a lock nobody holds is stale and taken over).
  * Batching: while a game is open with build K it runs the oldest request with build K next (fed into the open game's
    inbox, no relaunch), unless the head of the line has waited more than -StarveMinutes; another build waits for the
    current request, then the game quits cleanly, the new bundle is installed into Mods\Placemaker and the game relaunched.
    Land made to order is laid out in the open game when its request arrives (SuiteRunner.Land.cs). A request that does
    not fit the open game's test grid, or got 'no-block' there, gets a fresh launch.
  * The user's playground (Play.ps1) goes next after the current request; the queue pauses until the user closes it.
  * Never starts a game while a Cities2.exe it did not start is running (the user playing); never kills one.
  * Crash or hang: Run-Suite collects the logs; the request is marked failed (a crash during boot is retried once).
  * Results: each request's own folder (summary.json, scenarios, shots, logs, contact.html) in the requester's ResultsRoot.
  * After -IdleMinutes with an empty line the game quits; then the build that was installed before the queue started
    is put back into Mods\Placemaker (unless someone installed another one meanwhile), the lock released, and it exits.
#>
param(
    [double]$IdleMinutes = 10,
    [double]$StarveMinutes = 10,
    [int]$ExitGraceSec = 20,
    # started by a client: start the real dispatcher (hidden) and exit, so it is nobody's child
    [switch]$Detach
)
$ErrorActionPreference = 'Stop'
if ($Detach) {
    $pass = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-IdleMinutes', $IdleMinutes, '-StarveMinutes', $StarveMinutes, '-ExitGraceSec', $ExitGraceSec)
    if ($env:PLACEMAKER_GAME_QUEUE_IDLE) { $pass[6] = $env:PLACEMAKER_GAME_QUEUE_IDLE }   # tests
    Start-Process -FilePath 'powershell.exe' -ArgumentList ($pass -join ' ') -WindowStyle Hidden -WorkingDirectory $env:USERPROFILE | Out-Null
    exit 0
}
. (Join-Path $PSScriptRoot 'GameQueue.ps1')
Q-Init
$script:mods = Join-Path $script:QUserData 'Mods\Placemaker'
$script:suiteLock = Join-Path (Split-Path $script:QMainRuns) 'lock'

function D-Log([string]$m) {
    $line = '{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}' -f (Get-Date), $PID, $m
    try { Add-Content -Path $script:Q.Log -Value $line -Encoding UTF8 } catch { }
}

# ───────────── single instance: dispatcher.lock held open for the dispatcher's life ─────────────
# Nobody else can open it for writing while this process lives; when it dies (killed or not) the handle goes with it,
# and the PID inside is only information (a lock file whose PID is gone is stale and taken over).
try { $script:lockStream = [IO.File]::Open($script:Q.Lock, 'OpenOrCreate', 'ReadWrite', 'Read') } catch { exit 0 }
$oldText = ''
try { $buf = New-Object byte[] ([int]$script:lockStream.Length); [void]$script:lockStream.Read($buf, 0, $buf.Length); $oldText = [Text.Encoding]::UTF8.GetString($buf) } catch { }
$old = $null; try { if ($oldText.Trim()) { $old = $oldText | ConvertFrom-Json } } catch { }
if ($old -and $old.pid -and $old.pid -ne $PID) { D-Log "took over a stale lock (dispatcher PID $($old.pid) from $($old.at) is gone)" }
$lockBytes = [Text.Encoding]::UTF8.GetBytes((([ordered]@{ pid = $PID; start = (Q-ProcStart $PID); at = (Get-Date).ToString('o'); script = $PSCommandPath }) | ConvertTo-Json -Compress))
$script:lockStream.SetLength(0); $script:lockStream.Write($lockBytes, 0, $lockBytes.Length); $script:lockStream.Flush()
D-Log "dispatcher up (PID $PID, $PSCommandPath)$(if ($script:QFake) { ", FAKE host $script:QFake" })"
Q-SeedHistory

$script:sess = $null          # the game this dispatcher runs (persisted in session.json)
$script:active = $null        # @{ req; dir; feed } the request in the game now
$script:launches = 0
$script:idleSince = Get-Date
$script:noWorkSince = Get-Date
$script:note = ''
$script:paused = $false
$script:bootFailures = @{}
$script:orphanQuit = @{}
$script:lastGc = [datetime]::MinValue
$script:hostBackoff = [datetime]::MinValue

function D-SaveSession { if ($script:sess) { Q-WriteJson $script:Q.Session $script:sess } elseif (Test-Path $script:Q.Session) { Remove-Item $script:Q.Session -Force } }

# ───────────── other games on the machine ─────────────

# Cities2.exe processes this dispatcher did not start, classified. Never touched (an earlier dispatcher's game excepted).
function D-Foreign {
    if ($script:QFake) {
        $f = Join-Path $script:Q.Root 'fake-user-game'
        if (Test-Path $f) { return @([pscustomobject]@{ pid = 0; kind = 'user'; runId = '' }) }
        return @()
    }
    $r = @()
    foreach ($p in @(Get-Process -Name 'Cities2' -ErrorAction SilentlyContinue)) {
        $cmd = ''
        try { $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)").CommandLine } catch { }
        $tok = if ($cmd -match '-placemakerSuite=(\S+)') { $Matches[1].Trim('"') } else { '' }
        if ($script:sess -and $tok -eq $script:sess.id) { continue }
        $kind = if (-not $tok) { 'user' } elseif ($tok -like 'queue-*') { 'orphan' } elseif ($tok -like 'play-*') { 'playground' } else { 'runner' }
        $r += [pscustomobject]@{ pid = $p.Id; kind = $kind; runId = $tok }
    }
    return $r
}

# A Run-Suite outside the queue holding the per-repo suite lock (a -Direct run).
function D-SuiteLockHolder {
    if ($script:QFake) { return $null }
    $l = Q-ReadJson $script:suiteLock
    if (-not $l -or -not $l.pid) { return $null }
    if ($script:sess -and $l.pid -eq $script:sess.hostPid) { return $null }
    if (Q-Alive $l.pid) { return $l }
    return $null
}

function D-WaitNote($foreign, $holder) {
    $f = @($foreign) | Select-Object -First 1
    if ($f) {
        switch ($f.kind) {
            'user' { return "waiting: the user's own game is open (Cities2.exe PID $($f.pid)); the queue goes on when it closes" }
            'playground' { return "waiting: the user's playground is open (run $($f.runId)); the queue goes on when they close the game" }
            'orphan' { return "waiting: an earlier queue game (run $($f.runId)) is closing" }
            default { return "waiting: a game runner started outside the queue has the game (run $($f.runId), PID $($f.pid))" }
        }
    }
    if ($holder) { return "waiting: a game runner started outside the queue holds the suite lock (run $($holder.runId), PID $($holder.pid))" }
    return ''
}

# A game an earlier (dead) dispatcher left: ask it to quit through its inbox, kill it only if it will not.
function D-HandleOrphans($foreign) {
    foreach ($f in @($foreign | Where-Object { $_.kind -eq 'orphan' })) {
        if (-not $script:orphanQuit.ContainsKey($f.runId)) {
            $inbox = Join-Path (Join-Path $script:QMainRuns $f.runId) 'inbox'
            if (Test-Path $inbox) { Q-WriteText (Join-Path $inbox ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-quit.json')) '{"command":"quit"}' | Out-Null }
            $script:orphanQuit[$f.runId] = Get-Date
            D-Log "an earlier queue game is still open (run $($f.runId), PID $($f.pid)): asked it to quit"
        } elseif (((Get-Date) - $script:orphanQuit[$f.runId]).TotalSeconds -gt 240) {
            D-Log "the earlier queue game (run $($f.runId), PID $($f.pid)) did not quit in 240 s: stopping it (a queue game, not the user's)"
            Stop-Process -Id $f.pid -Force -ErrorAction SilentlyContinue
            $script:orphanQuit[$f.runId] = Get-Date
        }
    }
}

# ───────────── installing a build ─────────────

function D-DllHash([string]$dir) { $d = Join-Path $dir 'Placemaker.dll'; if (Test-Path $d) { return (Get-FileHash $d -Algorithm SHA256).Hash.Substring(0, 8).ToLowerInvariant() } else { return '' } }

# Before the queue's first install: the build installed now is kept, to be put back at shutdown.
function D-SnapshotInstalled {
    $info = Join-Path $script:Q.Installed 'info.json'
    if (Test-Path $info) { return }
    $tmp = "$($script:Q.Installed).tmp"
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    $had = Test-Path (Join-Path $script:mods 'Placemaker.dll')
    if ($had) { New-Item -ItemType Directory -Force -Path (Join-Path $tmp 'mod') | Out-Null; Copy-Item -Path (Join-Path $script:mods '*') -Destination (Join-Path $tmp 'mod') -Recurse -Force }
    $dll = if ($had) { Get-Item (Join-Path $script:mods 'Placemaker.dll') } else { $null }
    Q-WriteJson (Join-Path $tmp 'info.json') ([ordered]@{ had = $had; dllHash = (D-DllHash $script:mods); dllTime = $(if ($dll) { $dll.LastWriteTime.ToString('o') } else { '' }); at = (Get-Date).ToString('o'); lastDeployed = '' })
    if (Test-Path $script:Q.Installed) { Remove-Item $script:Q.Installed -Recurse -Force }
    Rename-Item $tmp (Split-Path $script:Q.Installed -Leaf)
    D-Log "kept the installed build to put back at shutdown: $(if ($had) { "Placemaker.dll $(D-DllHash $script:mods) of $($dll.LastWriteTime)" } else { 'nothing was installed' })"
}

function D-Deploy($req) {
    if ($script:QFake) { return $true }
    D-SnapshotInstalled
    $src = Join-Path $req.build.bundle 'mod'
    for ($i = 0; $i -lt 10; $i++) {
        try {
            if (Test-Path $script:mods) { Remove-Item $script:mods -Recurse -Force -ErrorAction Stop }
            New-Item -ItemType Directory -Force -Path $script:mods | Out-Null
            Copy-Item -Path (Join-Path $src '*') -Destination $script:mods -Recurse -Force -ErrorAction Stop
            $info = Q-ReadJson (Join-Path $script:Q.Installed 'info.json')
            if ($info) { $info.lastDeployed = (D-DllHash $script:mods); Q-WriteJson (Join-Path $script:Q.Installed 'info.json') $info }
            D-Log "installed build $($req.build.hash) ($($req.build.label), from $($req.who)) into Mods\Placemaker"
            return $true
        } catch { D-Log "install attempt $($i + 1) failed: $($_.Exception.Message)"; Start-Sleep -Seconds 3 }
    }
    return $false
}

function D-RestoreInstalled {
    if ($script:QFake) { return '' }
    $info = Q-ReadJson (Join-Path $script:Q.Installed 'info.json')
    if (-not $info) { return '' }
    $now = D-DllHash $script:mods
    $msg = ''
    if ($info.lastDeployed -and $now -ne $info.lastDeployed) {
        $msg = "Mods\Placemaker changed since the queue's last install (now $now): left as it is"
    } else {
        try {
            if (Test-Path $script:mods) { Remove-Item $script:mods -Recurse -Force -ErrorAction Stop }
            if ($info.had) { Copy-Item -Path (Join-Path $script:Q.Installed 'mod') -Destination $script:mods -Recurse -Force -ErrorAction Stop }
            $msg = if ($info.had) { "put back the build installed before the queue started (Placemaker.dll $($info.dllHash) of $($info.dllTime))" } else { 'nothing was installed before the queue started: Mods\Placemaker removed' }
        } catch { $msg = "could not put back the earlier build: $($_.Exception.Message) (kept in $($script:Q.Installed))"; D-Log $msg; return $msg }
    }
    Remove-Item $script:Q.Installed -Recurse -Force -ErrorAction SilentlyContinue
    D-Log $msg
    return $msg
}

# ───────────── games: launch, watch, quit ─────────────

function D-Grid($req, $queued) {
    if ($req.session.grid) { return $req.session.grid }
    $flat = 0; $zoned = 0; $slope = 0
    foreach ($r in @($queued | Where-Object { $_.key -eq $req.key })) { $flat += [int]$r.blocks.Flat; $zoned += [int]$r.blocks.Zoned; $slope += [int]$r.blocks.Slope }
    $nx = [math]::Max(16, [math]::Max([math]::Ceiling(($flat + 2) / 2), [math]::Max($zoned, [math]::Ceiling($slope / 2))))
    return "$([math]::Min(30, $nx))x7"
}

function D-Quote([string]$a) { if ($a -match '[\s;"]') { return '"' + ($a -replace '"', '\"') + '"' } else { return $a } }

function D-StartHost($req, [string]$runId, [string[]]$extra) {
    $script = if ($script:QFake) { $script:QFake } else { Join-Path $req.build.bundle 'host\tools\suite\Run-Suite.ps1' }
    $s = $req.session
    $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script, '-Direct', '-NoBuild', '-RunId', $runId, '-Label', $req.build.git)
    if ($s.allMods) { $a += '-AllMods' }
    if ($s.keepGraphics) { $a += '-KeepGraphics' }
    if ($s.noTerrain) { $a += '-NoTerrain' }
    if ($s.userModSettings) { $a += '-UserModSettings' }
    if ($s.window) { $a += @('-Window', $s.window) }
    if ($s.look) { $a += @('-Look', $s.look) }
    if ($s.pace) { $a += @('-Pace', $s.pace) }
    if ($s.opt) { $a += @('-Opt', $s.opt) }
    if ($s.pregrowSeconds -gt 0) { $a += @('-PregrowSeconds', [string]$s.pregrowSeconds) }
    $a += $extra
    $argLine = ($a | ForEach-Object { D-Quote $_ }) -join ' '
    $out = Join-Path $script:Q.Logs "$runId.host.txt"
    $p = Start-Process -FilePath 'powershell.exe' -ArgumentList $argLine -NoNewWindow -PassThru -RedirectStandardOutput $out -RedirectStandardError "$out.err"
    D-Log "started the game host for run $runId (PID $($p.Id)): $argLine"
    return $p
}

function D-StartSession($req, $queued) {
    if (-not (D-Deploy $req)) { return $false }
    $id = 'queue-' + (Get-Date -Format 'MMdd-HHmmss')
    $grid = D-Grid $req $queued
    $p = D-StartHost $req $id @('-Set', 'queue', '-Queue', '-Grid', $grid, '-ResultsRoot', $script:QMainRuns)
    $script:launches++
    $script:sess = [ordered]@{
        id = $id; kind = 'queue'; key = $req.key; build = $req.build.hash; label = $req.build.label; bundle = $req.build.bundle; who = $req.who
        dir = (Join-Path $script:QMainRuns $id); hostPid = $p.Id; hostStart = (Q-Ticks $p); startedAt = (Get-Date).ToString('o'); readyAt = ''; quitAt = ''; quitWhy = ''
        served = 0; grid = $grid; capacity = $null; firstRequest = $req.id; requestId = ''
    }
    D-SaveSession
    Add-Content -Path $script:Q.Launches -Value ('{0:yyyy-MM-dd HH:mm:ss} launch {1} build {2} ({3}) for {4} ({5}), grid {6}' -f (Get-Date), $id, $req.build.hash, $req.build.label, $req.id, $req.who, $grid)
    D-Log "launching game $id for build $($req.build.hash) ($($req.who)), grid $grid"
    return $true
}

function D-StartPlay($req) {
    if (-not (D-Deploy $req)) { return $false }
    $a = @('-Playground', '-ResultsRoot', $req.resultsRoot)
    if ($req.session.grid) { $a += @('-Grid', $req.session.grid) }
    $p = D-StartHost $req $req.runId $a
    $script:launches++
    $script:sess = [ordered]@{
        id = $req.runId; kind = 'play'; key = ''; build = $req.build.hash; label = $req.build.label; bundle = $req.build.bundle; who = $req.who
        dir = $req.resultsDir; hostPid = $p.Id; hostStart = (Q-Ticks $p); startedAt = (Get-Date).ToString('o'); readyAt = ''; quitAt = ''; quitWhy = ''
        served = 0; grid = $req.session.grid; capacity = $null; firstRequest = $req.id; requestId = $req.id
    }
    D-SaveSession
    Add-Content -Path $script:Q.Launches -Value ('{0:yyyy-MM-dd HH:mm:ss} launch {1} PLAYGROUND build {2} ({3}) for {4} ({5})' -f (Get-Date), $req.runId, $req.build.hash, $req.build.label, $req.id, $req.who)
    D-Log "the user's playground $($req.runId) for $($req.who): build $($req.build.hash)"
    return $true
}

function D-Beat {
    if (-not $script:sess) { return $null }
    $b = Q-ReadJson (Join-Path $script:sess.dir 'heartbeat.json')
    if ($b -and $b.runId -ne $script:sess.id) { return $null }
    return $b
}

function D-QuitSession([string]$why) {
    if (-not $script:sess -or $script:sess.quitAt) { return }
    $inbox = Join-Path $script:sess.dir 'inbox'
    New-Item -ItemType Directory -Force -Path $inbox | Out-Null
    Q-WriteText (Join-Path $inbox ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-quit.json')) '{"command":"quit"}' | Out-Null
    $script:sess.quitAt = (Get-Date).ToString('o'); $script:sess.quitWhy = $why
    D-SaveSession
    D-Log "closing game $($script:sess.id): $why"
}

function D-GamePid {
    $b = D-Beat
    if ($b -and $b.pid) { return [int]$b.pid }
    return 0
}

# ───────────── requests: take, feed, deliver ─────────────

function D-Take($req) {
    $from = Join-Path $script:Q.Requests $req.id
    $to = Join-Path $script:Q.Active $req.id
    try { Move-Item -LiteralPath $from -Destination $to -ErrorAction Stop } catch { return $null }
    if (-not (Test-Path (Join-Path $to 'request.json'))) { Remove-Item $to -Recurse -Force -ErrorAction SilentlyContinue; return $null }
    return @{ req = (Q-ReadJson (Join-Path $to 'request.json')); dir = $to; feed = $null; takenAt = (Get-Date) }
}

function D-SummaryRows([string]$dir) {
    $s = Q-ReadJson (Join-Path $dir 'summary.json')
    if (-not $s) { return @() }
    return @($s.scenarios | Where-Object { $_ })
}

# Feeds the active request into the open game's inbox.
function D-Feed {
    $a = $script:active
    $req = $a.req
    $inbox = Join-Path $script:sess.dir 'inbox'
    New-Item -ItemType Directory -Force -Path $inbox | Out-Null
    $name = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + ($req.who -replace '[^A-Za-z0-9._-]', '-')
    $payload = [IO.File]::ReadAllText((Join-Path $a.dir 'payload.json'))
    $rowsBefore = @(D-SummaryRows $script:sess.dir).Count
    $timeout = if ($req.timeoutMin -gt 0) { $req.timeoutMin * 60 } else { 300 + 150 * [int]$req.scenarios + 120 * [int]$req.land }
    $a.feed = [ordered]@{ inbox = $name; session = $script:sess.id; sessionDir = $script:sess.dir; fedAt = (Get-Date).ToString('o'); rowsBefore = $rowsBefore; timeoutSec = $timeout; launched = ($script:sess.served -eq 0); killed = '' }
    Q-WriteJson (Join-Path $a.dir 'feed.json') $a.feed
    Q-WriteText (Join-Path $inbox "$name.json") $payload | Out-Null
    D-Log "fed $($req.id) ($($req.who), $($req.scenarios) build(s)) into game $($script:sess.id) as $name"
}

function D-H([string]$s) { return [Net.WebUtility]::HtmlEncode($s) }

# The request's own results folder, in the requester's ResultsRoot: what a direct run would have left there.
function D-Publish($a, $result, [string]$status, [string]$message) {
    $req = $a.req; $feed = $a.feed
    $dir = $req.resultsDir
    if (Test-Path $dir) { $old = "$dir.old-$(Get-Date -Format 'yyyyMMdd-HHmmss')"; try { Move-Item $dir $old -ErrorAction Stop } catch { } }
    foreach ($sub in '', 'shots', 'scenarios', 'logs') { New-Item -ItemType Directory -Force -Path (Join-Path $dir $sub) | Out-Null }
    $sd = $feed.sessionDir
    $sum = Q-ReadJson (Join-Path $sd 'summary.json')
    $all = @(if ($sum) { $sum.scenarios | Where-Object { $_ } })   # @() around the if: one row must stay an array (Windows PowerShell has no .Count on a lone object)
    $count = if ($result -and $result.scenarios) { @($result.scenarios).Count } else { [math]::Max(0, $all.Count - [int]$feed.rowsBefore) }
    $rows = @()
    $scFiles = @{}
    for ($k = 0; $k -lt $count; $k++) {
        $i = [int]$feed.rowsBefore + $k
        if ($i -ge $all.Count) { break }
        $row = $all[$i]
        $rows += $row
        $f = Get-ChildItem (Join-Path $sd 'scenarios') -Filter ('{0:D2}-*.json' -f ($i + 1)) -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($f) { Copy-Item $f.FullName (Join-Path $dir "scenarios\$($f.Name)") -Force; $scFiles["$($row.id)"] = Join-Path $dir "scenarios\$($f.Name)" }
        if ($row.shots) {
            foreach ($p in $row.shots.PSObject.Properties) {
                $rel = "$($p.Value)"
                if ($rel -like 'ERROR*' -or -not $rel) { continue }
                $src = Join-Path $sd $rel
                $dst = Join-Path $dir $rel
                New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
                if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination $dst -Force }
            }
        }
    }
    # logs: the session's copies once the game is gone, else the live files (the whole session so far)
    $hostGone = -not (Q-Alive $script:sess.hostPid $script:sess.hostStart)
    foreach ($n in 'Placemaker.log', 'Player.log') {
        $sessCopy = Join-Path $sd "logs\$n"
        $live = if ($n -eq 'Player.log') { Join-Path $script:QUserData 'Player.log' } else { Join-Path $script:QUserData "Logs\$n" }
        if ($hostGone -and (Test-Path $sessCopy)) { Copy-Item $sessCopy (Join-Path $dir "logs\$n") -Force } else { Q-CopyShared $live (Join-Path $dir "logs\$n") | Out-Null }
    }
    Q-CopyShared (Join-Path $sd 'suite.log') (Join-Path $dir 'suite.log') | Out-Null
    $cat = Join-Path $sd 'catalog.json'
    if (Test-Path $cat) {
        Copy-Item $cat (Join-Path $dir 'catalog.json') -Force
        $repoCat = Join-Path $req.repo 'suite\catalog.json'
        if (Test-Path (Split-Path $repoCat)) { Copy-Item $cat $repoCat -Force -ErrorAction SilentlyContinue }
    }
    $fedAt = (Q-Date $feed.fedAt)
    foreach ($ad in (Join-Path $script:QUserData 'ModsData\Placemaker\audit'), (Join-Path $sd 'audit')) {
        if (-not (Test-Path $ad)) { continue }
        foreach ($f in Get-ChildItem $ad -File | Where-Object { $_.LastWriteTime -ge $fedAt.AddSeconds(-2) }) {
            New-Item -ItemType Directory -Force -Path (Join-Path $dir 'audit') | Out-Null
            Copy-Item $f.FullName (Join-Path $dir "audit\$($f.Name)") -Force
        }
    }
    $now = Get-Date
    $waited = ($fedAt - (Q-Date $req.created)).TotalSeconds
    $ran = ($now - $fedAt).TotalSeconds
    $byStatus = ($rows | Group-Object { "$($_.status)" } | ForEach-Object { "$($_.Count) $($_.Name)" }) -join ', '
    $shotCount = 0; foreach ($r in $rows) { if ($r.shots) { $shotCount += @($r.shots.PSObject.Properties).Count } }
    $headline = "$(if ($status -eq 'served') { 'finished' } else { "$($status.ToUpperInvariant()): $message" }). $($rows.Count) scenarios ($byStatus). Shots: $shotCount. Run in the queue's game $($feed.session)."
    $queueInfo = [ordered]@{ request = $req.id; who = $req.who; status = $status; message = $message; session = $feed.session; sessionDir = $sd; launched = [bool]$feed.launched; enqueued = $req.created; fedAt = $feed.fedAt; finishedAt = $now.ToString('o'); waitedSec = [math]::Round($waited); ranSec = [math]::Round($ran); build = $req.build }
    $out = [ordered]@{ runId = $req.runId; set = $req.set; git = $req.build.label; queue = $queueInfo; headline = $headline; phase = $(if ($status -eq 'served') { 'done' } else { 'aborted' }) }
    if ($sum) { foreach ($k in 'grid', 'capacity', 'plots', 'landLaidOutLater', 'look', 'placemakerSettings', 'profile', 'fidelity', 'map', 'codeMods', 'dllHash', 'assembly', 'wrongBuild', 'gitMismatch') { if ($sum.$k) { $out[$k] = $sum.$k } } }
    $out['scenarios'] = $rows
    $out['scenarioCount'] = $rows.Count
    Q-WriteJson (Join-Path $dir 'summary.json') $out
    # summary.md and contact.html
    $md = @("> **Host: $status** (label $($req.build.label), queue game $($feed.session)$(if ($feed.launched) { ', launched for this request' } else { ', already open' }); waited $(Q-Duration $waited), ran $(Q-Duration $ran))$(if ($message) { " - $message" })", '', "# Game run $($req.runId) ($($req.set))", '', "**$headline**", '',
        'Read the logs: `logs/Placemaker.log` (the queue game''s whole log so far: find your scenarios by id), `logs/Player.log`, `suite.log`. Pictures in order: `contact.html`. Each scenario''s steps: `scenarios/NN-<id>.json`.', '',
        '| # | scenario | block | status | s | shots |', '|---|---|---|---|---|---|')
    $html = New-Object Text.StringBuilder
    [void]$html.Append('<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Run ' + (D-H $req.runId) + '</title><style>:root{--bg:#f6f6f4;--fg:#1d1d1b;--dim:#6b6b66;--card:#fff;--line:#dcdcd6;--fail:#b3261e}@media (prefers-color-scheme:dark){:root{--bg:#161615;--fg:#ececea;--dim:#a3a39d;--card:#21211f;--line:#3a3a37;--fail:#ff8a80}}body{background:var(--bg);color:var(--fg);font:14px/1.4 system-ui,sans-serif;margin:0 16px 40px}h1{font-size:20px}h2{font-size:16px;margin:28px 0 6px}.row{display:flex;flex-wrap:wrap;gap:10px}.shot{background:var(--card);border:1px solid var(--line);border-radius:6px;padding:6px;width:min(400px,100%);box-sizing:border-box}.shot img{width:100%;height:auto;display:block}.cap{font-size:12px;color:var(--dim);margin-top:4px}.bad{color:var(--fail);font-weight:600}</style></head><body>')
    [void]$html.Append('<h1>Run ' + (D-H $req.runId) + '</h1><p>' + (D-H $headline) + '</p>')
    $n = 0
    foreach ($r in $rows) {
        $n++
        $md += "| $n | $($r.id) | $($r.block) $($r.kind) | $($r.status)$(if ($r.failure) { ': ' + $r.failure }) | $($r.sec) | $(if ($r.shots) { @($r.shots.PSObject.Properties).Count } else { 0 }) |"
        [void]$html.Append('<h2>' + $n + '. ' + (D-H $r.id) + '</h2><div class="cap">' + (D-H "$($r.block) $($r.kind), status $($r.status), $($r.sec) s") + '</div>')
        $sf = $scFiles["$($r.id)"]
        if ($sf) {
            $sc = Q-ReadJson $sf
            foreach ($x in @($sc.steps | Where-Object { $_ -and $_.error })) { [void]$html.Append('<div class="bad">step ' + $x.i + ' ' + (D-H $x.do) + ': ' + (D-H "$($x.error)") + '</div>') }
        }
        [void]$html.Append('<div class="row">')
        if ($r.shots) { foreach ($p in $r.shots.PSObject.Properties) { $v = "$($p.Value)"; if ($v -like 'ERROR*') { [void]$html.Append('<div class="shot bad">' + (D-H "$($p.Name): $v") + '</div>') } else { [void]$html.Append('<div class="shot"><a href="' + (D-H $v) + '"><img loading="lazy" src="' + (D-H $v) + '" alt="' + (D-H $p.Name) + '"></a><div class="cap">' + (D-H $p.Name) + '</div></div>') } } }
        [void]$html.Append('</div>')
    }
    [void]$html.Append('</body></html>')
    [IO.File]::WriteAllText((Join-Path $dir 'summary.md'), ($md -join "`r`n") + "`r`n", (New-Object Text.UTF8Encoding $false))
    [IO.File]::WriteAllText((Join-Path $dir 'contact.html'), $html.ToString(), (New-Object Text.UTF8Encoding $false))
    if ($result) {
        $r2 = $result | ConvertTo-Json -Depth 12 | ConvertFrom-Json
        foreach ($s in @($r2.scenarios | Where-Object { $_ })) { if ($scFiles["$($s.id)"]) { $s.resultFile = $scFiles["$($s.id)"] } }
        if ($r2.PSObject.Properties['contact']) { $r2.contact = Join-Path $dir 'contact.html' }
        Q-WriteJson (Join-Path $dir 'result.json') $r2
    }
    Q-WriteJson (Join-Path $dir 'host.json') ([ordered]@{ runId = $req.runId; set = $req.set; git = $req.build.git; dllHash = $req.build.dllHash; label = $req.build.label; status = $status; queue = $queueInfo })
    return @{ waited = $waited; ran = $ran; rows = $rows }
}

function D-InstalledLine {
    $info = Q-ReadJson (Join-Path $script:Q.Installed 'info.json')
    $before = if ($info) { if ($info.had) { "Placemaker.dll $($info.dllHash) of $($info.dllTime)" } else { 'nothing' } } else { 'the build installed before' }
    return "Mods\Placemaker has the queue's current build while its game is open; when the queue shuts down it puts back what was installed before it started ($before)"
}

# Finishes the active request: its result for the client, its folder in done\.
function D-Finish([string]$status, [int]$code, [string]$message, $result) {
    $a = $script:active
    $req = $a.req
    $pub = @{ waited = 0; ran = 0; rows = @() }
    if ($a.feed -and $req.kind -ne 'play') { try { $pub = D-Publish $a $result $status $message } catch { D-Log "publishing $($req.id) failed: $($_.Exception.Message)" } }
    if ($req.kind -eq 'play') { $pub.waited = ((Q-Date $script:sess.startedAt) - (Q-Date $req.created)).TotalSeconds; $pub.ran = ((Get-Date) - (Q-Date $script:sess.startedAt)).TotalSeconds }
    $res = [ordered]@{
        id = $req.id; who = $req.who; kind = $req.kind; status = $status; code = $code; message = $message
        resultsDir = $req.resultsDir; runId = $req.runId; session = $(if ($a.feed) { $a.feed.session } else { $script:sess.id }); launched = $(if ($a.feed) { [bool]$a.feed.launched } else { $true })
        waitedSec = [math]::Round($pub.waited); ranSec = [math]::Round($pub.ran); finishedAt = (Get-Date).ToString('o'); installed = (D-InstalledLine)
        build = $req.build
    }
    $doneDir = Join-Path $script:Q.Done $req.id
    try { Move-Item -LiteralPath $a.dir -Destination $doneDir -ErrorAction Stop } catch { New-Item -ItemType Directory -Force -Path $doneDir | Out-Null }
    Q-WriteJson (Join-Path $doneDir 'result.json') $res
    D-Log "finished $($req.id) ($($req.who)): $status$(if ($message) { " - $message" }) (waited $(Q-Duration $pub.waited), ran $(Q-Duration $pub.ran))"
    # history for the ETA
    if ($status -eq 'served' -and $pub.rows) {
        $sum = 0.0
        foreach ($r in $pub.rows) { if ($r.sec) { Q-HistoryAdd 'scenario' ([double]$r.sec); $sum += [double]$r.sec } }
        Q-HistoryAdd 'overhead' ([math]::Max(1, $pub.ran - $sum))
    }
    $script:active = $null
    if ($script:sess -and $script:sess.kind -eq 'queue') { $script:sess.served = [int]$script:sess.served + 1; D-SaveSession }
    $script:idleSince = Get-Date
}

# Back in line (at its old place: its seq is kept).
function D-Requeue([string]$why, [switch]$Fresh) {
    $a = $script:active
    $req = $a.req
    if ($Fresh) { $req.fresh = $true; Q-WriteJson (Join-Path $a.dir 'request.json') $req }
    Remove-Item (Join-Path $a.dir 'feed.json') -Force -ErrorAction SilentlyContinue
    try { Move-Item -LiteralPath $a.dir -Destination (Join-Path $script:Q.Requests $req.id) -ErrorAction Stop } catch { D-Log "could not put $($req.id) back in line: $($_.Exception.Message)" }
    D-Log "put $($req.id) ($($req.who)) back in line: $why"
    $script:active = $null
}

# ───────────── capacity of the open game's test grid ─────────────

function D-Used {
    $u = @{ Flat = @{}; Zoned = @{}; Slope = @{}; Rough = @{}; Survive = @{} }
    if (-not $script:sess) { return $u }
    foreach ($r in D-SummaryRows $script:sess.dir) { if ($r.block -and $r.kind -and $u.ContainsKey("$($r.kind)")) { $u["$($r.kind)"][$r.block] = $true } }
    return $u
}
function D-Fits($r) {
    if (-not $script:sess -or -not $script:sess.capacity) { return $true }
    $u = D-Used
    foreach ($k in 'Flat', 'Zoned', 'Slope', 'Rough', 'Survive') {
        $need = [int]$r.blocks.$k
        if ($need -le 0) { continue }
        $cap = [int]$script:sess.capacity.($k.ToLowerInvariant())
        if ($need -gt $cap - $u[$k].Count) { return $false }
    }
    return $true
}

# ───────────── status for the clients ─────────────

function D-Status([string]$state, $queued) {
    $rates = Q-Rates
    $now = Get-Date
    $cur = $null
    $remain = 0.0
    if ($script:active -and $script:active.feed) {
        $req = $script:active.req
        $runSec = ($now - (Q-Date $script:active.feed.fedAt)).TotalSeconds
        $est = Q-WorkSec $req $rates
        $remain = [math]::Max(5, $est - $runSec)
        $cur = [ordered]@{ id = $req.id; who = $req.who; kind = $req.kind; scenarios = $req.scenarios; runningSec = [math]::Round($runSec); etaSec = [math]::Round($remain); build = $req.build.hash }
    } elseif ($script:sess -and $script:sess.kind -eq 'play') {
        $cur = [ordered]@{ id = $script:sess.requestId; who = $script:sess.who; kind = 'play'; scenarios = 0; runningSec = [math]::Round(($now - (Q-Date $script:sess.startedAt)).TotalSeconds); etaSec = 0; build = $script:sess.build }
    }
    $planSess = $null
    $gameState = ''
    if ($script:sess) {
        if ($script:sess.kind -eq 'play') { $planSess = @{ key = $null; served = 0; remainSec = 0; unknown = $true }; $gameState = 'playground' }
        elseif ($script:sess.quitAt) { $gameState = 'closing'; $planSess = @{ key = $null; served = 0; remainSec = [math]::Max(3, $rates.quit - ($now - (Q-Date $script:sess.quitAt)).TotalSeconds) } }
        elseif (-not $script:sess.readyAt) { $gameState = 'starting'; $planSess = @{ key = $script:sess.key; served = $script:sess.served; remainSec = [math]::Max(5, $rates.boot - ($now - (Q-Date $script:sess.startedAt)).TotalSeconds) + $remain } }
        else { $gameState = 'ready'; $planSess = @{ key = $script:sess.key; served = $script:sess.served; remainSec = $remain } }
    }
    if ($script:paused) { if ($planSess) { $planSess.unknown = $true } else { $planSess = @{ key = $null; served = 0; remainSec = 0; unknown = $true } } }
    $plan = @(Q-Plan $queued $planSess $rates ($StarveMinutes * 60))
    $st = [ordered]@{
        updated = $now.ToString('o'); dispatcherPid = $PID; state = $state; note = $script:note; paused = $script:paused
        game = $(if ($script:sess) { [ordered]@{ id = $script:sess.id; kind = $script:sess.kind; build = $script:sess.build; label = $script:sess.label; who = $script:sess.who; state = $gameState; served = $script:sess.served; startedAt = $script:sess.startedAt; readyAt = $script:sess.readyAt; grid = $script:sess.grid } } else { $null })
        current = $cur; queued = @($queued).Count; plan = $plan; launches = $script:launches
        rates = [ordered]@{ bootSec = [math]::Round($rates.boot); quitSec = [math]::Round($rates.quit); perBuildSec = [math]::Round($rates.scenario); overheadSec = [math]::Round($rates.overhead) }
    }
    Q-WriteJson $script:Q.Status $st
}

# ───────────── housekeeping ─────────────

function D-Gc {
    if (((Get-Date) - $script:lastGc).TotalMinutes -lt 10) { return }
    $script:lastGc = Get-Date
    foreach ($d in Get-ChildItem $script:Q.Done -Directory -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -lt (Get-Date).AddHours(-24) }) { Remove-Item $d.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    $keep = @{}
    foreach ($root in $script:Q.Requests, $script:Q.Active) { foreach ($j in Get-ChildItem $root -Recurse -Filter 'request.json' -ErrorAction SilentlyContinue) { $r = Q-ReadJson $j.FullName; if ($r) { $keep[$r.build.hash] = $true } } }
    if ($script:sess) { $keep[$script:sess.build] = $true }
    foreach ($b in Get-ChildItem $script:Q.Bundles -Directory -ErrorAction SilentlyContinue) {
        if ($keep.ContainsKey($b.Name)) { continue }
        if ($b.LastWriteTime -lt (Get-Date).AddHours(-2)) { Remove-Item $b.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    }
    foreach ($f in Get-ChildItem $script:Q.Logs -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-3) }) { Remove-Item $f.FullName -Force -ErrorAction SilentlyContinue }
}

# Requests whose client is gone leave the line.
function D-DropDead($queued) {
    $alive = @()
    foreach ($r in @($queued)) {
        if ($r.clientPid -and -not (Q-Alive $r.clientPid $r.clientStart)) {
            $d = Join-Path $script:Q.Requests $r.id
            $doneDir = Join-Path $script:Q.Done $r.id
            try {
                Move-Item -LiteralPath $d -Destination $doneDir -ErrorAction Stop
                Q-WriteJson (Join-Path $doneDir 'result.json') ([ordered]@{ id = $r.id; who = $r.who; status = 'dropped'; code = 2; message = 'the command that asked is gone (Ctrl+C or killed): taken out of the line' })
                D-Log "dropped $($r.id) ($($r.who)): its client PID $($r.clientPid) is gone"
            } catch { }
            continue
        }
        $alive += $r
    }
    return $alive
}

# ───────────── adopt what a dead dispatcher left ─────────────

$prev = Q-ReadJson $script:Q.Session
if ($prev) {
    if (Q-Alive $prev.hostPid $prev.hostStart) {
        $script:sess = [ordered]@{}
        foreach ($p in $prev.PSObject.Properties) { $script:sess[$p.Name] = $p.Value }
        D-Log "adopted game $($prev.id) ($($prev.kind), build $($prev.build)) from a dispatcher that is gone"
    } else {
        D-Log "the game $($prev.id) of a gone dispatcher has ended"
        Remove-Item $script:Q.Session -Force
    }
}
foreach ($d in Get-ChildItem $script:Q.Active -Directory -ErrorAction SilentlyContinue) {
    $req = Q-ReadJson (Join-Path $d.FullName 'request.json')
    if (-not $req) { Remove-Item $d.FullName -Recurse -Force -ErrorAction SilentlyContinue; continue }
    $feed = Q-ReadJson (Join-Path $d.FullName 'feed.json')
    $a = @{ req = $req; dir = $d.FullName; feed = $feed; takenAt = (Get-Date) }
    if ($script:sess -and (($feed -and $feed.session -eq $script:sess.id) -or ($script:sess.kind -eq 'play' -and $script:sess.requestId -eq $req.id))) {
        if ($feed) { $a.feed = [ordered]@{}; foreach ($p in $feed.PSObject.Properties) { $a.feed[$p.Name] = $p.Value } }
        $script:active = $a
        D-Log "adopted the request in the game: $($req.id) ($($req.who))"
    } else {
        $script:active = $a
        $inboxResult = if ($feed) { Join-Path $feed.sessionDir "inbox\$($feed.inbox).result.json" } else { '' }
        if ($inboxResult -and (Test-Path $inboxResult)) {
            $tmpSess = $script:sess
            $script:sess = [ordered]@{ hostPid = 0; hostStart = 0; kind = 'queue'; id = $feed.session; dir = $feed.sessionDir; served = 1 }
            D-Finish 'served' 0 '' (Q-ReadJson $inboxResult)
            $script:sess = $tmpSess
        } else { D-Requeue 'its game ended with the dispatcher that ran it' }
    }
}

# ───────────── the loop ─────────────

$state = 'idle'
try {
    $errors = 0
    while ($true) {
      try {
        $now = Get-Date
        $queued = D-DropDead (Q-Queued)
        $foreign = @(D-Foreign)
        D-HandleOrphans $foreign
        $holder = D-SuiteLockHolder
        $script:note = ''
        $script:paused = $false

        if ($script:sess) {
            $hostAlive = Q-Alive $script:sess.hostPid $script:sess.hostStart
            if ($script:sess.kind -eq 'play') {
                $state = 'playground'
                $script:note = "the user's playground is open ($($script:sess.who), run $($script:sess.id)); the queue goes on when they close the game"
                $script:paused = $true
                $beat = D-Beat
                if (-not $script:sess.readyAt -and $beat -and "$($beat.phase)" -eq 'playground') {
                    $script:sess.readyAt = $now.ToString('o'); D-SaveSession
                    D-Log ("the playground {0} is ready ({1:F0} s): the controls are the user's; the queue waits until the game is closed" -f $script:sess.id, ($now - (Q-Date $script:sess.startedAt)).TotalSeconds)
                }
                if (-not $hostAlive) {
                    D-Log "the user's playground $($script:sess.id) closed"
                    if ($script:active) { D-Finish 'served' 0 '' $null }
                    $script:sess = $null; D-SaveSession
                    $script:idleSince = Get-Date
                }
            } else {
                $beat = D-Beat
                if (-not $script:sess.readyAt -and $beat -and "$($beat.phase)" -like 'queue*') {
                    $script:sess.readyAt = $now.ToString('o')
                    $boot = ($now - (Q-Date $script:sess.startedAt)).TotalSeconds
                    Q-HistoryAdd 'boot' $boot
                    $sum = Q-ReadJson (Join-Path $script:sess.dir 'summary.json')
                    if ($sum -and $sum.capacity) { $script:sess.capacity = $sum.capacity }
                    D-SaveSession
                    D-Log ("game {0} ready in {1:F0} s (grid {2})" -f $script:sess.id, $boot, $script:sess.grid)
                    $script:idleSince = $now
                }
                if (-not $hostAlive) {
                    # the game and its host are gone: a clean quit, a crash or a hang (Run-Suite collected the logs)
                    $hj = Q-ReadJson (Join-Path $script:sess.dir 'host.json')
                    $how = if ($hj) { "$($hj.status)" } else { 'unknown' }
                    if ($script:sess.quitAt) { Q-HistoryAdd 'quit' ($now - (Q-Date $script:sess.quitAt)).TotalSeconds }
                    D-Log "game $($script:sess.id) ended: $how$(if ($script:sess.quitWhy) { " (closed: $($script:sess.quitWhy))" })"
                    if ($script:active) {
                        $f = $script:active.feed
                        $resFile = if ($f) { Join-Path $script:sess.dir "inbox\$($f.inbox).result.json" } else { '' }
                        if ($resFile -and (Test-Path $resFile)) { D-Finish 'served' 0 '' (Q-ReadJson $resFile) }
                        elseif ($f -and $f.killed) { D-Finish 'timeout' $(if ($script:active.req.kind -eq 'send') { 1 } else { 1 }) "no result after $($f.timeoutSec) s: the game was stopped (status $how); the logs are in the results folder" $null }
                        elseif ($f) { D-Finish 'failed' $(if ($script:active.req.kind -eq 'send') { 4 } else { 1 }) "the game went away while running it (status $how); the logs are in the results folder" $null }
                        else { D-Requeue "the game ended before it was fed (status $how)" }
                    } elseif (-not $script:sess.readyAt -and -not $script:sess.quitAt -and -not (Test-Path (Join-Path $script:sess.dir 'host.json'))) {
                        # the host stopped before it launched the game (the suite lock, a Cities2.exe it found): not the build's fault
                        $why = @(Get-Content (Join-Path $script:Q.Logs "$($script:sess.id).host.txt") -ErrorAction SilentlyContinue | Where-Object { $_ -match 'STOP' } | Select-Object -Last 1)
                        D-Log "the game host for $($script:sess.id) stopped before launching the game: $($why -join ' ') - trying again in 30 s"
                        $script:hostBackoff = $now.AddSeconds(30)
                    } elseif (-not $script:sess.readyAt -and -not $script:sess.quitAt) {
                        $k = $script:sess.key
                        $script:bootFailures[$k] = 1 + [int]$script:bootFailures[$k]
                        D-Log "game $($script:sess.id) did not boot (status $how; attempt $($script:bootFailures[$k]))"
                        if ($script:bootFailures[$k] -ge 2) {
                            foreach ($r in @($queued | Where-Object { $_.key -eq $k })) {
                                $a = D-Take $r
                                if ($a) { $script:active = $a; $a.feed = [ordered]@{ inbox = ''; session = $script:sess.id; sessionDir = $script:sess.dir; fedAt = (Get-Date).ToString('o'); rowsBefore = 0; launched = $true }; D-Finish 'failed' 1 "the game did not boot twice for this build (status $how): see $($script:sess.dir)\host.log" $null }
                            }
                            $script:bootFailures.Remove($k)
                        }
                    }
                    $script:sess = $null; D-SaveSession
                    $script:idleSince = Get-Date
                    $state = 'idle'
                }
                elseif ($script:active -and $script:active.feed) {
                    $state = 'running'
                    $f = $script:active.feed
                    $resFile = Join-Path $script:sess.dir "inbox\$($f.inbox).result.json"
                    if (Test-Path $resFile) {
                        Start-Sleep -Milliseconds 300
                        $res = Q-ReadJson $resFile
                        $rows = @($res.scenarios | Where-Object { $_ })
                        $noBlock = @($rows | Where-Object { $_.status -eq 'no-block' })
                        if ($noBlock.Count -gt 0 -and -not $f.launched -and -not $script:active.req.fresh) {
                            D-Requeue "$($noBlock.Count) scenario(s) found no free test block (or no room for their land) in game $($script:sess.id): a fresh launch for it" -Fresh
                        } else {
                            $code = if ($res.error) { 1 } else { 0 }
                            D-Finish 'served' $code $(if ($res.error) { "$($res.error)" } else { '' }) $res
                        }
                    } elseif (-not $f.killed -and ($now - (Q-Date $f.fedAt)).TotalSeconds -gt [double]$f.timeoutSec) {
                        $gp = D-GamePid
                        D-Log "$($script:active.req.id): no result after $($f.timeoutSec) s: stopping game $($script:sess.id) (PID $gp, started by the queue)"
                        $f.killed = $now.ToString('o')
                        Q-WriteJson (Join-Path $script:active.dir 'feed.json') $f
                        if ($gp) { Stop-Process -Id $gp -Force -ErrorAction SilentlyContinue }
                    }
                }
                elseif ($script:sess.quitAt) {
                    $state = 'closing'
                    if (($now - (Q-Date $script:sess.quitAt)).TotalSeconds -gt 240) {
                        $gp = D-GamePid
                        D-Log "game $($script:sess.id) did not close in 240 s: stopping it (PID $gp, started by the queue)"
                        if ($gp) { Stop-Process -Id $gp -Force -ErrorAction SilentlyContinue }
                        $script:sess.quitAt = $now.ToString('o'); D-SaveSession
                    }
                }
                elseif (-not $script:sess.readyAt) { $state = 'starting' }
                else {
                    # ready and idle: the next request for this game, or close it for another build
                    $state = 'ready'
                    $fits = { param($r) ($script:sess.served -eq 0) -or (D-Fits $r) }   # a fresh game takes its first request whatever it needs
                    $next = Q-SelectNext $queued $script:sess.key $script:sess.served $fits 0 ($StarveMinutes * 60)
                    if ($next) {
                        $same = $next.kind -ne 'play' -and $next.key -eq $script:sess.key -and -not ($next.fresh -and $script:sess.served -gt 0)
                        if ($same -and ($script:sess.served -eq 0 -or (D-Fits $next))) {
                            $a = D-Take $next
                            if ($a) { $script:active = $a; D-Feed; $state = 'running' }
                        } else {
                            $why = if ($next.kind -eq 'play') { "the user's playground ($($next.who)) is next" } elseif ($next.key -ne $script:sess.key) { "next is build $($next.build.hash) ($($next.who))" } else { "$($next.who) needs a fresh test grid" }
                            D-QuitSession $why
                        }
                    } elseif (($now - $script:idleSince).TotalMinutes -ge $IdleMinutes) {
                        D-QuitSession "nothing in line for $IdleMinutes min"
                    }
                }
            }
        }
        else {
            # no game of ours
            $next = Q-SelectNext $queued $null 0 $null 0 ($StarveMinutes * 60)
            $wait = D-WaitNote $foreign $holder
            if ($next -and $wait) {
                $state = 'waiting'; $script:note = $wait; $script:paused = $true
            } elseif ($next -and $script:hostBackoff -gt $now) {
                $state = 'waiting'; $script:note = 'waiting a moment before starting the game again'
            } elseif ($next -and $next.kind -eq 'play') {
                $a = D-Take $next
                if ($a) {
                    $script:active = $a
                    if (D-StartPlay $a.req) { $state = 'playground' } else { D-Finish 'failed' 1 'could not install the build (Mods\Placemaker is locked?)' $null }
                }
            } elseif ($next) {
                # a game for the head's build; its requests stay in line and are fed once it is ready
                if (D-StartSession $next $queued) { $state = 'starting' } else { D-Log 'could not install the build: retrying in a moment'; $script:hostBackoff = $now.AddSeconds(10) }
            } else {
                $state = 'idle'
                if ($wait) { $script:note = $wait }
                if (($now - $script:idleSince).TotalSeconds -ge $ExitGraceSec -and @(Get-ChildItem $script:Q.Active -Directory -ErrorAction SilentlyContinue).Count -eq 0) {
                    D-Log 'the line is empty and no game is open: shutting down'
                    break
                }
            }
        }
        if ($queued.Count -gt 0 -or $script:active) { $script:noWorkSince = $now }
        D-Status $state $queued
        D-Gc
        Start-Sleep -Milliseconds 1500
        $errors = 0
      } catch {
        $errors++
        D-Log "dispatcher error ($errors in a row): $($_.Exception.Message) at $($_.InvocationInfo.ScriptLineNumber): $($_.InvocationInfo.Line.Trim()) | $($_.ScriptStackTrace -replace "`r?`n", " < ")"
        if ($errors -ge 20) { throw }
        Start-Sleep -Seconds 3
      }
    }
}
catch { D-Log "dispatcher error: $($_.Exception.Message) at $($_.InvocationInfo.ScriptLineNumber): $($_.InvocationInfo.Line.Trim()) | $($_.ScriptStackTrace -replace "`r?`n", " < ")"; throw }
finally {
    if (-not $script:sess) { D-RestoreInstalled | Out-Null }
    try { $st = Q-ReadJson $script:Q.Status; if ($st) { $st.state = 'stopped'; $st.updated = (Get-Date).ToString('o'); Q-WriteJson $script:Q.Status $st } } catch { }
    D-Log 'dispatcher down'
    try { $script:lockStream.Dispose() } catch { }
    Remove-Item $script:Q.Lock -Force -ErrorAction SilentlyContinue   # fails harmlessly when the next dispatcher already holds it
}
