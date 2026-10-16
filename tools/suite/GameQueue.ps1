<#
  The machine-wide game queue: shared by the clients (Run-Suite.ps1, Show.ps1, Send-Suite.ps1, Play.ps1, Queue.ps1)
  and the dispatcher (Game-Dispatcher.ps1). Dot-source it.

  Only one Cities2.exe can run, and every build installs into the same Mods\Placemaker folder, so every agent's game
  work goes through one line at %USERPROFILE%\.placemaker\game-queue (not under AppData\Local or Roaming: the agents'
  command sandbox gives every session its own private copy of those, so the line would not be shared):
    requests\<id>\request.json + payload.json   waiting in line (delete the folder to leave the line)
    active\<id>\                                being run now
    done\<id>\result.json                       finished: the client prints it and exits
    bundles\<hash>\                             frozen builds (mod\ = what Deploy copies, host\ = the runner scripts)
    user-build\, install.json, install.lock     the user's build and what Mods\Placemaker holds (ModsInstall.ps1)
    dispatcher.lock, status.json, history.json, dispatcher.log, launches.log, session.json
  A request carries a frozen snapshot of the requester's build; the dispatcher runs requests with the same build in one
  open game, back to back, and relaunches only to switch builds. See suite/README.md, "The game queue".
#>

# The queue's protocol version: a client with a newer one asks an older dispatcher to hand over when the game is free.
$script:QVersion = 2
$script:QRoot = if ($env:PLACEMAKER_GAME_QUEUE) { $env:PLACEMAKER_GAME_QUEUE } else { Join-Path $env:USERPROFILE '.placemaker\game-queue' }
# tests only: a fake game host script instead of Run-Suite, and only with a stand-in queue root (never on the real line)
$script:QFake = if ($env:PLACEMAKER_GAME_QUEUE) { $env:PLACEMAKER_GAME_QUEUE_FAKE } else { '' }
if ($env:PLACEMAKER_GAME_QUEUE_FAKE -and -not $env:PLACEMAKER_GAME_QUEUE) { Write-Host 'queue: PLACEMAKER_GAME_QUEUE_FAKE is ignored without PLACEMAKER_GAME_QUEUE (a test must use its own queue folder)' }
$script:Q = @{
    Root = $script:QRoot
    Requests = Join-Path $script:QRoot 'requests'
    Active = Join-Path $script:QRoot 'active'
    Done = Join-Path $script:QRoot 'done'
    Bundles = Join-Path $script:QRoot 'bundles'
    Logs = Join-Path $script:QRoot 'logs'
    Lock = Join-Path $script:QRoot 'dispatcher.lock'
    Retire = Join-Path $script:QRoot 'retire.json'
    Status = Join-Path $script:QRoot 'status.json'
    History = Join-Path $script:QRoot 'history.json'
    Session = Join-Path $script:QRoot 'session.json'
    Log = Join-Path $script:QRoot 'dispatcher.log'
    Launches = Join-Path $script:QRoot 'launches.log'
}
$script:QUserData = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
$script:QMainRuns = if ($env:PLACEMAKER_GAME_QUEUE_RUNS) { $env:PLACEMAKER_GAME_QUEUE_RUNS } else { 'C:\Users\jack\cs2-area-tool\.forensics\suite\runs' }   # Run-Suite's default ResultsRoot (one suite lock for every worktree)
# ETA model (seconds): a build = base + pictures x pic (+ land when it has its own land); a request adds overhead.
$script:QDefaults = @{ boot = 60.0; quit = 15.0; base = 8.0; pic = 4.5; land = 6.0; overhead = 3.0 }
# The runner scripts a game session is run with: only these (and the mod) make a build's identity.
$script:QHostHashed = @('host\tools\suite\run-suite.ps1', 'host\tools\suite\modsinstall.ps1', 'host\tools\suite\contact-suite.ps1', 'host\suite\schema.json', 'host\suite\scenarios\smoke.json')
. (Join-Path $PSScriptRoot 'ModsInstall.ps1')

function Q-Init { foreach ($k in 'Root', 'Requests', 'Active', 'Done', 'Bundles', 'Logs') { New-Item -ItemType Directory -Force -Path $script:Q[$k] | Out-Null } }

function Q-WriteText([string]$path, [string]$text) {
    $tmp = "$path.$PID.tmp"
    [IO.File]::WriteAllText($tmp, $text, (New-Object Text.UTF8Encoding $false))
    for ($i = 0; $i -lt 20; $i++) {
        try {
            if (Test-Path -LiteralPath $path) { [IO.File]::Replace($tmp, $path, [NullString]::Value) } else { [IO.File]::Move($tmp, $path) }
            return $true
        } catch { Start-Sleep -Milliseconds 50 }
    }
    Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    return $false
}
function Q-WriteJson([string]$path, $obj) { Q-WriteText $path ($obj | ConvertTo-Json -Depth 12) | Out-Null }
# Readers share delete access, so a writer can swap the file in while it is being read.
function Q-ReadText([string]$path) {
    $fs = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite, Delete')
    try { $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8); return $sr.ReadToEnd() } finally { $fs.Dispose() }
}
function Q-ReadJson([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    for ($i = 0; $i -lt 3; $i++) {
        try { return ((Q-ReadText $path) | ConvertFrom-Json) } catch { Start-Sleep -Milliseconds 60 }
    }
    return $null
}
# A time from the queue's files (ISO 8601 text; a DateTime already), as local time, whatever the machine's culture.
function Q-Date($v) {
    if ($v -is [datetime]) { return $v.ToLocalTime() }
    return [datetime]::Parse("$v", [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind).ToLocalTime()
}
# A file another process is writing (the game's logs): read with every share mode, from byte $offset on.
function Q-CopyShared([string]$src, [string]$dst, [int64]$offset = 0) {
    if (-not (Test-Path -LiteralPath $src)) { return $false }
    try {
        $in = [IO.File]::Open($src, 'Open', 'Read', 'ReadWrite, Delete')
        try {
            if ($offset -gt 0 -and $offset -le $in.Length) { [void]$in.Seek($offset, 'Begin') }
            $out = [IO.File]::Create($dst); try { $in.CopyTo($out) } finally { $out.Dispose() }
        } finally { $in.Dispose() }
        return $true
    } catch { return $false }
}
function Q-FileLength([string]$p) { if (Test-Path -LiteralPath $p) { try { return [int64](Get-Item -LiteralPath $p).Length } catch { } }; return [int64]0 }

# A process's start time (UTC ticks); -1 when it runs but its start time cannot be read (an elevated process seen from a
# normal one), 0 when it does not run.
function Q-Ticks($p) { try { return $p.StartTime.ToUniversalTime().Ticks } catch { return [int64]-1 } }
function Q-ProcStart([int]$id) { $p = Get-Process -Id $id -ErrorAction SilentlyContinue; if ($p) { return (Q-Ticks $p) } else { return 0 } }
# Alive and the same process (a recycled PID has another start time). A start time that cannot be read: alive.
function Q-Alive($id, $start = 0) {
    if (-not $id) { return $false }
    $p = Get-Process -Id ([int]$id) -ErrorAction SilentlyContinue
    if (-not $p) { return $false }
    if (-not $start -or [int64]$start -eq -1) { return $true }
    $t = Q-Ticks $p
    if ($t -eq -1) { return $true }
    return ([math]::Abs($t - [int64]$start) -le 20000000)
}

function Q-Duration([double]$sec) {
    if ($sec -lt 0) { $sec = 0 }
    $s = [int][math]::Round($sec)
    if ($s -lt 60) { return "$s s" }
    $m = [math]::Floor($s / 60); $r = $s % 60
    if ($m -lt 60) { if ($r -eq 0) { return "$m min" } else { return "$m min $r s" } }
    return ('{0} h {1} min' -f [math]::Floor($m / 60), ($m % 60))
}

function Q-Median($list, [double]$default) {
    $a = @($list | Where-Object { $_ -ne $null -and [double]$_ -gt 0 } | ForEach-Object { [double]$_ } | Sort-Object)
    if ($a.Count -eq 0) { return $default }
    return $a[[math]::Floor($a.Count / 2)]
}

# ───────────── history (for the ETA) ─────────────
# history.json: boot, quit and overhead times (s), and the last builds as rows { b: s without pictures, p: s per picture,
# n: pictures, l: 1 with its own land }, measured from the game's own timings (summary.json waitsMs camera + shotSettle).

function Q-History {
    $h = Q-ReadJson $script:Q.History
    $r = @{ boot = @(); quit = @(); overhead = @(); rows = @() }
    if ($h) { foreach ($k in @($r.Keys)) { if ($h.$k) { $r[$k] = @($h.$k) } } }
    return $r
}
function Q-SaveHistory($h) { foreach ($k in 'boot', 'quit', 'overhead') { $h[$k] = @(@($h[$k]) | Select-Object -Last 40) }; $h.rows = @(@($h.rows) | Select-Object -Last 120); Q-WriteJson $script:Q.History $h }
function Q-HistoryAdd([string]$kind, [double]$sec) {
    if ($sec -le 0) { return }
    $h = Q-History
    $h[$kind] = @(@($h[$kind]) + [math]::Round($sec, 1))
    Q-SaveHistory $h
}
# One finished build (a summary.json row) as a history row; $null when it cannot be split into pictures and the rest.
function Q-RowRecord($row, [bool]$land) {
    if (-not $row -or -not $row.sec -or [double]$row.sec -le 0 -or -not $row.waitsMs) { return $null }
    $n = if ($row.shots) { @($row.shots.PSObject.Properties).Count } else { 0 }
    $shotSec = ([double]$row.waitsMs.camera + [double]$row.waitsMs.shotSettle) / 1000.0
    $sec = [double]$row.sec
    $o = [ordered]@{ b = [math]::Round([math]::Max(0.5, $sec - $shotSec), 1); p = $null; n = $n; l = $(if ($land) { 1 } else { 0 }) }
    if ($n -gt 0) { $o.p = [math]::Round([math]::Max(0.2, $shotSec / $n), 2) }
    return $o
}
function Q-HistoryAddRows($rows, $landFlags) {
    $h = Q-History
    $k = 0
    foreach ($r in @($rows)) { $rec = Q-RowRecord $r ([bool](@($landFlags)[$k])); if ($rec) { $h.rows = @(@($h.rows) + [pscustomobject]$rec) }; $k++ }
    Q-SaveHistory $h
}
function Q-Rates {
    $h = Q-History
    $rows = @($h.rows | Where-Object { $_ })
    $base = Q-Median ($rows | ForEach-Object { $_.b }) $script:QDefaults.base
    $pic = Q-Median ($rows | Where-Object { $_.n -gt 0 -and $_.p } | ForEach-Object { $_.p }) $script:QDefaults.pic
    $land = $script:QDefaults.land
    $lr = @($rows | Where-Object { $_.l }); $fr = @($rows | Where-Object { -not $_.l })
    if ($lr.Count -ge 3 -and $fr.Count -ge 3) { $land = [math]::Min(120, [math]::Max(0, (Q-Median ($lr | ForEach-Object { $_.b }) 0) - (Q-Median ($fr | ForEach-Object { $_.b }) 0))) }
    return @{
        boot = Q-Median $h.boot $script:QDefaults.boot; quit = Q-Median $h.quit $script:QDefaults.quit
        base = $base; pic = $pic; land = $land; overhead = Q-Median $h.overhead $script:QDefaults.overhead
    }
}
# First use (or a history from before the per-picture model): measured builds from the runs already on disk.
function Q-SeedHistory {
    $h = Q-History
    if (@($h.rows).Count -gt 0) { return }
    if (Test-Path -LiteralPath $script:QMainRuns) {
        foreach ($d in Get-ChildItem -LiteralPath $script:QMainRuns -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 40) {
            $hj = Q-ReadJson (Join-Path $d.FullName 'host.json')
            if (@($h.boot).Count -lt 40 -and $hj -and $hj.status -eq 'quit' -and $hj.phaseAtSec) {
                if ($hj.phaseAtSec.scenarios) { $h.boot += [double]$hj.phaseAtSec.scenarios } elseif ($hj.phaseAtSec.queue) { $h.boot += [double]$hj.phaseAtSec.queue }
                if ($hj.phaseAtSec.quit -and $hj.totalSec) { $h.quit += [math]::Max(3, [double]$hj.totalSec - [double]$hj.phaseAtSec.quit) }
            }
            if ($d.Name -notlike 'queue-*') { continue }   # a queue game's summary holds every request's builds once
            $sj = Q-ReadJson (Join-Path $d.FullName 'summary.json')
            if ($sj) { foreach ($r in @($sj.scenarios)) { $rec = Q-RowRecord $r ("$($r.block)" -like 'P*'); if ($rec) { $h.rows += [pscustomobject]$rec } } }
        }
    }
    Q-SaveHistory $h
}

# Pictures a scenario takes: steps with a shot, 'shot' and 'terrain' steps (a scenario without steps: the default one).
function Q-ScenarioPics($sc) {
    $steps = @($sc.steps | Where-Object { $_ })
    if ($steps.Count -eq 0) { return 1 }
    $n = 0
    foreach ($st in $steps) { $d = "$($st.do)"; if ($d -eq 'shot' -or $d -eq 'terrain' -or "$($st.shot)") { $n++ } }
    return $n
}
function Q-RowSec([int]$pics, [bool]$land, $rates) { return $rates.base + $pics * $rates.pic + $(if ($land) { $rates.land } else { 0 }) }
# The work of a request (s): its builds, each with its pictures, plus the per-request overhead.
function Q-WorkSec($r, $rates) {
    $rp = @($r.rowPics)
    if ($rp.Count -gt 0) {
        $rl = @($r.rowLand); $t = 0.0
        for ($k = 0; $k -lt $rp.Count; $k++) { $t += Q-RowSec ([int]$rp[$k]) ([bool]$rl[$k]) $rates }
        return $t + $rates.overhead
    }
    return [double]$r.scenarios * (Q-RowSec 3 $false $rates) + [double]$r.land * $rates.land + $rates.overhead
}
# Time left for the request in the game: rows it finished (summary.json) and how fast they went.
# Returns @{ left; over (s past the estimate of the build running now); done; total }.
function Q-RunningLeft($r, [int]$rowsDone, [double]$sinceRowSec, [double]$runSec, $rates) {
    $rp = @($r.rowPics); $rl = @($r.rowLand)
    if ($rp.Count -eq 0) {
        $est = Q-WorkSec $r $rates
        $left = $est - $runSec; $over = 0.0
        if ($left -lt 0.1 * $est) { if ($left -lt 0) { $over = -$left }; $left = [math]::Max(3, 0.1 * $est) }
        return @{ left = $left; over = $over; done = $rowsDone; total = [int]$r.scenarios }
    }
    $est = @(for ($k = 0; $k -lt $rp.Count; $k++) { Q-RowSec ([int]$rp[$k]) ([bool]$rl[$k]) $rates })
    $k0 = [math]::Min($rowsDone, $est.Count)
    $doneEst = 0.0; for ($k = 0; $k -lt $k0; $k++) { $doneEst += $est[$k] }
    $speed = 1.0
    if ($k0 -gt 0 -and $doneEst -gt 0) { $speed = [math]::Min(3.0, [math]::Max(0.5, ($runSec - $sinceRowSec) / $doneEst)) }
    $left = 0.0; $over = 0.0
    if ($k0 -lt $est.Count) {
        $cur = $est[$k0] * $speed
        $curLeft = $cur - $sinceRowSec
        if ($curLeft -lt 0.1 * $cur) { if ($curLeft -lt 0) { $over = -$curLeft }; $curLeft = [math]::Max(2, 0.1 * $cur) }
        $left = $curLeft
        for ($k = $k0 + 1; $k -lt $est.Count; $k++) { $left += $est[$k] * $speed }
    } else { $left = 2 }
    return @{ left = $left; over = $over; done = $k0; total = $est.Count }
}

# ───────────── the line: which request runs next, and when ─────────────

function Q-Queued {
    $r = @()
    foreach ($d in Get-ChildItem -LiteralPath $script:Q.Requests -Directory -ErrorAction SilentlyContinue) {
        if ($d.Name.StartsWith('.')) { continue }   # being written, or being cancelled
        $j = Q-ReadJson (Join-Path $d.FullName 'request.json')
        if ($j -and $j.id -eq $d.Name) { $r += $j }
    }
    return @($r | Sort-Object { [int64]$_.seq })
}

# The policy: the user's playground first; else, while a game is open with build K, the oldest request with build K
# (that still fits that game's test grid), as long as the head of the line still starts within StarveSec of asking when
# that request is done; else the head. $atSec: how far in the future the choice is made (for the plan).
function Q-SelectNext($queued, [string]$sessKey, [int]$served, [scriptblock]$fits, [double]$atSec = 0, [double]$starveSec = 600, $rates = $null) {
    $queued = @($queued | Where-Object { $_ })
    if ($queued.Count -eq 0) { return $null }
    $play = $queued | Where-Object { $_.kind -eq 'play' } | Select-Object -First 1
    if ($play) { return $play }
    $head = $queued[0]
    if ($sessKey) {
        $waited = ((Get-Date).AddSeconds($atSec) - (Q-Date $head.created)).TotalSeconds
        if ($waited -lt $starveSec) {
            foreach ($r in $queued) {
                if ($r.key -ne $sessKey) { continue }
                if ($r.fresh -and $served -gt 0) { continue }
                if ($fits -and -not (& $fits $r)) { continue }
                if ($r.id -ne $head.id -and $rates -and ($waited + (Q-WorkSec $r $rates)) -gt $starveSec) { continue }   # would make the head wait too long
                return $r
            }
        }
    }
    return $head
}

# The order the dispatcher will take and when each request should be done, from the measured history.
# $sess: @{ key; served; remainSec } for the open game (key $null when none, or when it is closing).
function Q-Plan($queued, $sess, $rates, [double]$starveSec = 600) {
    $pending = @($queued | Where-Object { $_ })
    $t = 0.0; $key = $null; $served = 0; $unknown = $false; $launchFor = ''
    if ($sess) { $t = [double]$sess.remainSec; $key = $sess.key; $served = [int]$sess.served; if ($sess.unknown) { $unknown = $true } }
    $out = @()
    while ($pending.Count -gt 0) {
        $next = Q-SelectNext $pending $key $served $null $t $starveSec $rates
        $relaunch = ($next.kind -eq 'play') -or (-not $key) -or ($next.key -ne $key) -or ($next.fresh -and $served -gt 0)
        $start = $t
        if ($relaunch) { if ($key) { $t += $rates.quit }; $t += $rates.boot; $served = 0; $launchFor = "$($next.who)" }
        $shares = if ($relaunch) { '' } else { $launchFor }
        if ($next.kind -eq 'play') { $key = $null }
        else { $t += (Q-WorkSec $next $rates); $key = $next.key; $served++ }
        $pics = 0; foreach ($p in @($next.rowPics)) { $pics += [int]$p }
        $out += [pscustomobject]@{ id = $next.id; who = $next.who; kind = $next.kind; scenarios = $next.scenarios; pictures = $pics; relaunch = $relaunch; sharesWith = $shares; startInSec = [math]::Round($start); doneInSec = [math]::Round($t); unknown = $unknown; build = $next.build.hash }
        if ($next.kind -eq 'play') { $unknown = $true }   # the user plays as long as they like
        $pending = @($pending | Where-Object { $_.id -ne $next.id })
    }
    return $out
}

# ───────────── who asks, what it needs ─────────────

function Q-Who([string]$repo) {
    $b = ''
    try { $b = (& git -C $repo rev-parse --abbrev-ref HEAD 2>$null | Select-Object -First 1) } catch { }
    if ($b) { $b = "$b".Trim() }
    if (-not $b -or $b -eq 'HEAD') { $b = Split-Path $repo -Leaf }
    $b = ($b -split '/')[-1]
    return $b
}

# Scenarios, test blocks by kind, land plots and the pictures of each build a set needs (expansions counted from the
# requester's catalog).
function Q-Needs($doc, [string]$repo) {
    $r = [ordered]@{ scenarios = 0; land = 0; blocks = [ordered]@{ Flat = 0; Zoned = 0; Slope = 0; Rough = 0; Survive = 0 }; rowPics = @(); rowLand = @() }
    if (-not $doc -or $doc.command) { return $r }
    $list = @(if ($doc.scenarios) { $doc.scenarios } else { $doc })
    $types = @()
    $cat = Join-Path $repo 'suite\catalog.json'
    if (Test-Path -LiteralPath $cat) { try { $types = @((Get-Content -LiteralPath $cat -Raw -Encoding UTF8 | ConvertFrom-Json).types) } catch { } }
    foreach ($sc in $list) {
        if (-not $sc) { continue }
        $n = 1
        $exp = "$($sc.expand)".ToLowerInvariant()
        if ($exp -eq 'presets' -or $exp -eq 'types') {
            $ts = @($types | Where-Object { if ($sc.layer) { $_.layer -eq $sc.layer } elseif ($sc.group) { $_.group -eq $sc.group } else { $true } })
            if ($exp -eq 'presets') {
                $n = 0
                foreach ($t in $ts) { $p = @($t.presets | Where-Object { -not $sc.skip -or -not (@($sc.skip) -contains $_) }); $c = $p.Count; if ($sc.limit -gt 0) { $c = [math]::Min($c, [int]$sc.limit) }; $n += $c }
            } else { $n = $ts.Count; if ($sc.limit -gt 0) { $n = [math]::Min($n, [int]$sc.limit) } }
            if ($n -lt 1) { $n = 1 }
        }
        $r.scenarios += $n
        $pics = Q-ScenarioPics $sc
        for ($k = 0; $k -lt $n; $k++) { $r.rowPics += $pics; $r.rowLand += $(if ($sc.land -and -not $sc.on) { 1 } else { 0 }) }
        if ($sc.on) { continue }
        if ($sc.land) { $r.land += $n; continue }
        $kind = switch ("$($sc.site)".ToLowerInvariant()) { 'zoned' { 'Zoned' } 'slope' { 'Slope' } 'rough' { 'Rough' } 'survive' { 'Survive' } default { 'Flat' } }
        $r.blocks[$kind] += $n
    }
    return $r
}

# ───────────── the frozen build ─────────────

function Q-Say([string]$m) { Write-Host ('queue: ' + $m) }

function Q-HashText([string]$s) { return (MI-Hash $s) }

function Q-Touch([string]$dir) { try { (Get-Item -LiteralPath $dir -Force).LastWriteTime = Get-Date } catch { } }

# A frozen build that can be installed: bundle.json, mod\Placemaker.dll and a host that installs it itself.
function Q-BundleOk([string]$dir) {
    if (-not $dir) { return 'no frozen build' }
    if (-not (Test-Path -LiteralPath (Join-Path $dir 'bundle.json'))) { return "its frozen build is gone ($dir)" }
    if (-not (Test-Path -LiteralPath (Join-Path $dir 'mod\Placemaker.dll'))) { return "its frozen build has no Placemaker.dll ($dir)" }
    $rs = Join-Path $dir 'host\tools\suite\Run-Suite.ps1'
    if (-not (Test-Path -LiteralPath $rs) -or -not (Select-String -LiteralPath $rs -Pattern 'DeployFrom' -SimpleMatch -Quiet)) { return "its frozen build's Run-Suite.ps1 is older than this queue (no -DeployFrom): merge master into that worktree" }
    return ''
}

# Builds the requester's worktree without deploying (unless -NoBuild), then freezes what Deploy would copy (bin\Release
# and UI\build) plus the runner scripts the game session is run with (tools\suite, suite\schema.json, ...) into
# bundles\<hash>: copied first, then hashed, so the hash is exactly what was frozen. The hash is the build's identity.
function Q-Snapshot([string]$repo, [switch]$NoBuild) {
    Q-Init
    $mutex = [Threading.Mutex]::new($false, ('Local\PlacemakerBuild-' + (Q-HashText $repo.ToLowerInvariant()).Substring(0, 16)))
    $held = $false
    try {
        try { $held = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $held = $true }
        if (-not $held) { Q-Say "another command is building this worktree; waiting for it ..."; try { [void]$mutex.WaitOne() } catch [Threading.AbandonedMutexException] { }; $held = $true }
        $bin = Join-Path $repo 'bin\Release'
        $ui = Join-Path $repo 'UI\build'
        if (-not $NoBuild) {
            $t0 = Get-Date
            Q-Say "building $repo (not installed: the queue installs it when your turn comes) ..."
            $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
            $out = & dotnet build (Join-Path $repo 'Placemaker.csproj') -c Release -p:Deploy=false 2>&1 | ForEach-Object { "$_" }
            $code = $LASTEXITCODE
            $ErrorActionPreference = $prev
            if ($code -ne 0) { return [pscustomobject]@{ code = 3; message = 'build failed'; tail = @($out | Select-Object -Last 30) } }
            Q-Say ("build ok in {0}" -f (Q-Duration ((Get-Date) - $t0).TotalSeconds))
        }
        if (-not (Test-Path -LiteralPath (Join-Path $bin 'Placemaker.dll'))) { return [pscustomobject]@{ code = 3; message = "no build in $bin (run without -NoBuild)"; tail = @() } }
        $tmp = Join-Path $script:Q.Bundles ('.snap-' + $PID + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
        try {
            New-Item -ItemType Directory -Force -Path (Join-Path $tmp 'mod'), (Join-Path $tmp 'host\tools\suite'), (Join-Path $tmp 'host\suite\scenarios') | Out-Null
            foreach ($c in Get-ChildItem -LiteralPath $bin -Force) { Copy-Item -LiteralPath $c.FullName -Destination (Join-Path $tmp 'mod') -Recurse -Force -ErrorAction Stop }
            if (Test-Path -LiteralPath $ui) { foreach ($c in Get-ChildItem -LiteralPath $ui -Force) { Copy-Item -LiteralPath $c.FullName -Destination (Join-Path $tmp 'mod') -Recurse -Force -ErrorAction Stop } }
            foreach ($f in Get-ChildItem -LiteralPath (Join-Path $repo 'tools\suite') -File -Filter '*.ps1') { Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $tmp 'host\tools\suite') -Force -ErrorAction Stop }
            # not suite\catalog.json: every run rewrites it (it names the assembly), which would make every request a new build
            foreach ($rel in 'suite\schema.json', 'suite\scenarios\smoke.json') { $p = Join-Path $repo $rel; if (Test-Path -LiteralPath $p) { Copy-Item -LiteralPath $p -Destination (Join-Path $tmp "host\$rel") -Force -ErrorAction Stop } }
            # the identity: the mod as frozen, and the runner scripts the game session uses (not the client-only scripts)
            $root = (Get-Item -LiteralPath $tmp).FullName.TrimEnd('\')
            $items = @{}
            foreach ($f in Get-ChildItem -LiteralPath $tmp -Recurse -File -Force) { $items[$f.FullName.Substring($root.Length + 1).ToLowerInvariant()] = $f }
            $keys = [string[]]@($items.Keys | Where-Object { $_.StartsWith('mod\') -or $script:QHostHashed -contains $_ })
            [Array]::Sort($keys, [StringComparer]::Ordinal)
            $bytes = [int64]0
            $lines = foreach ($k in $keys) { $bytes += $items[$k].Length; $k + ':' + (Get-FileHash -LiteralPath $items[$k].FullName -Algorithm SHA256).Hash }
            $hash = (Q-HashText (@($lines) -join "`n")).Substring(0, 12)
            $dllHash = (Get-FileHash -LiteralPath (Join-Path $tmp 'mod\Placemaker.dll') -Algorithm SHA256).Hash.Substring(0, 8).ToLowerInvariant()
            $git = ''
            try {
                $git = "$(& git -C $repo rev-parse --short HEAD 2>$null)".Trim()
                if ("$(& git -C $repo status --porcelain --untracked-files=no 2>$null)".Trim()) { $git += '-dirty' }
            } catch { }
            if (-not $git) { $git = 'nogit' }
            $dir = Join-Path $script:Q.Bundles $hash
            if (Q-BundleOk $dir) {
                Q-WriteJson (Join-Path $tmp 'bundle.json') ([ordered]@{ hash = $hash; git = $git; dllHash = $dllHash; label = "$git+$dllHash"; repo = $repo; created = (Get-Date).ToString('o'); files = $keys.Count; bytes = $bytes })
                if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }   # an incomplete one
                try { [IO.Directory]::Move($tmp, $dir) } catch { }   # another client may have frozen the same build first
            }
            $bad = Q-BundleOk $dir
            if ($bad) { return [pscustomobject]@{ code = 3; message = "could not freeze the build into $dir ($bad)"; tail = @() } }
            Q-Touch $dir   # in use: the clean-up goes by last use
            $b = Q-ReadJson (Join-Path $dir 'bundle.json')
            return [pscustomobject]@{ code = 0; hash = $hash; git = $git; dllHash = $dllHash; label = "$git+$dllHash"; bundle = $dir; mb = [math]::Round($bytes / 1MB, 1); sameAs = $(if ($b -and $b.repo -ne $repo) { $b.repo } else { '' }) }
        } finally { if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue } }
    } finally { if ($held) { $mutex.ReleaseMutex() }; $mutex.Dispose() }
}

# ───────────── the dispatcher: alive? start it ─────────────

function Q-DispatcherInfo {
    $l = Q-ReadJson $script:Q.Lock
    if ($l -and (Q-Alive $l.pid $l.start)) { return $l }
    return $null
}

# Starts the dispatcher detached: Game-Dispatcher.ps1 -Detach starts the real one and exits at once, so the dispatcher
# is nobody's child and outlives the command that started it (an agent's background command is stopped with its tree).
function Q-StartDispatcher([string]$scriptDir) {
    $script = Join-Path $scriptDir 'Game-Dispatcher.ps1'
    $p = Start-Process -FilePath 'powershell.exe' -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$script`" -Detach" -WindowStyle Hidden -WorkingDirectory $env:USERPROFILE -PassThru
    $p.WaitForExit(15000) | Out-Null
    for ($i = 0; $i -lt 20; $i++) { $d = Q-DispatcherInfo; if ($d) { return [int]$d.pid }; Start-Sleep -Milliseconds 250 }
    return 0
}

# ───────────── the client: ask, wait, print ─────────────

function Q-NewId([string]$who) {
    $safe = ($who -replace '[^A-Za-z0-9._-]', '-')
    if ($safe.Length -gt 40) { $safe = $safe.Substring(0, 40) }
    return ('{0}-{1}-{2}' -f (Get-Date -Format 'yyyyMMdd-HHmmss-fff'), $safe, ([guid]::NewGuid().ToString('N').Substring(0, 4)))
}

# The request's state: queued | active | done (with the result) | gone (left the line).
function Q-RequestState([string]$id) {
    $d = Join-Path $script:Q.Done $id
    $res = Q-ReadJson (Join-Path $d 'result.json')
    if ($res) { return @{ state = 'done'; result = $res } }
    if (Test-Path -LiteralPath (Join-Path $script:Q.Active "$id\request.json")) { return @{ state = 'active' } }
    if (Test-Path -LiteralPath (Join-Path $script:Q.Requests "$id\request.json")) { return @{ state = 'queued' } }
    if (Test-Path -LiteralPath $d) { return @{ state = 'finishing' } }
    return @{ state = 'gone' }
}

# Leaves the line: the request folder is renamed away in one step (so the dispatcher either has taken it already, or can
# no longer), then deleted.
function Q-Cancel([string]$id, [string]$why) {
    $d = Join-Path $script:Q.Requests $id
    if (Test-Path -LiteralPath $d) {
        $gone = Join-Path $script:Q.Requests (".$id.cancel-$PID")
        try {
            [IO.Directory]::Move($d, $gone)
            Remove-Item -LiteralPath $gone -Recurse -Force -ErrorAction SilentlyContinue
            Q-Say "left the line ($why)"
            return $true
        } catch { }
    }
    if (Test-Path -LiteralPath (Join-Path $script:Q.Active $id)) { Q-Say "$why, but your request is already in the game; it finishes and its results land in the results folder" }
    return $false
}

# One progress line from status.json (what the dispatcher plans) for request $id. Returns @{ line; key } (the key changes
# when something worth printing at once changes: state, place in line, who is ahead, what runs).
function Q-Progress($st, [string]$id) {
    if (-not $st) { return @{ line = 'waiting for the dispatcher to start ...'; key = 'none' } }
    $plan = @($st.plan)
    $me = $null; $ahead = @()
    foreach ($p in $plan) { if ($p.id -eq $id) { $me = $p; break }; $ahead += $p }
    $cur = $st.current
    $key = "$($st.state)|$(if ($cur) { $cur.id })|$(if ($me) { @($ahead).Count })|$((@($ahead) | ForEach-Object { $_.id }) -join ',')|$($st.note)|$(if ($st.game) { "$($st.game.id)/$($st.game.state)" })"
    if ($cur -and $cur.id -eq $id -and $cur.kind -eq 'play') {
        if ($st.game -and $st.game.readyAt) { return @{ line = "the playground is open: the controls are yours; close the game when you are done ($(Q-Duration $cur.runningSec) since launch)"; key = "$key|ready" } }
        return @{ line = "the playground is starting for you (build $($cur.build), $(Q-Duration $cur.runningSec) in; about a minute)"; key = $key }
    }
    if ($cur -and $cur.id -eq $id) {
        $prog = if ($cur.total) { "$($cur.done) of $($cur.total) build(s) done" } else { "$($cur.scenarios) build(s)" }
        $eta = if ([double]$cur.overSec -gt 0) { "the build running now is $(Q-Duration $cur.overSec) over its estimate" } elseif ([double]$cur.etaSec -gt 0) { 'ETA about ' + (Q-Duration $cur.etaSec) } else { '' }
        return @{ line = ("running yours in the game: {0}, {1} in (build {2}){3}" -f $prog, (Q-Duration $cur.runningSec), $cur.build, $(if ($eta) { "; $eta" } else { '' })); key = "$key|$($cur.done)" }
    }
    $parts = @()
    if ($me) {
        $parts += "#$(@($ahead).Count + 1) in line"
        if ($ahead.Count -gt 0) {
            $names = @($ahead | Select-Object -First 4 | ForEach-Object { if ($_.kind -eq 'play') { "$($_.who) (the user's playground)" } else { "$($_.who) ($($_.scenarios) build(s)$(if ($_.pictures) { ", $($_.pictures) pictures" })$(if ($_.relaunch) { ', relaunch' }))" } })
            if ($ahead.Count -gt 4) { $names += "$($ahead.Count - 4) more" }
            $parts += 'ahead: ' + ($names -join ', ')
        } else { $parts += 'next' }
    } elseif ($st.state -eq 'starting' -or $st.state -eq 'booting') { $parts += 'starting' }
    if ($cur) {
        if ($cur.kind -eq 'play') { $parts += "now: the user's playground ($($cur.who))" }
        else { $parts += "now: $($cur.who) $(if ($cur.total) { "$($cur.done) of $($cur.total) build(s) done" } else { "$($cur.scenarios) build(s)" }), $(Q-Duration $cur.runningSec) in$(if ([double]$cur.overSec -gt 0) { ", $(Q-Duration $cur.overSec) over its estimate" })" }
    }
    if ($st.game -and $st.game.build) { $parts += "game open with build $($st.game.build)$(if ($st.game.state -ne 'ready') { " ($($st.game.state))" })" }
    if ($st.note) { $parts += $st.note }
    if ($me) {
        if ($me.unknown -or $st.paused) { $parts += 'ETA unknown until the user''s game or playground ahead is closed' }
        else {
            $parts += $(if ($me.relaunch) { 'your build needs a launch' } elseif ($me.sharesWith) { "your build shares the launch for $($me.sharesWith)" } else { 'your build is the open game''s' })
            $parts += ('ETA about {0} (around {1:HH:mm})' -f (Q-Duration $me.doneInSec), (Get-Date).AddSeconds([double]$me.doneInSec))
        }
    }
    if ($parts.Count -eq 0) { return @{ line = 'in line; the dispatcher has not planned it yet'; key = $key } }
    return @{ line = ($parts -join ' | '); key = $key }
}

# Puts a request in line and waits for it; prints progress every ~15 s and at once on every change of state, place in
# line, who is ahead or what runs. Returns the done result, or a result with code 2 when the request left the line
# (Ctrl+C, its folder deleted), or code 1 when the queue is stuck (the dispatcher stopped updating status.json, or the
# request sits in active\ without running).
function Q-SubmitAndWait([hashtable]$req, [string]$payloadText, [string]$scriptDir, [int]$printEverySec = 15) {
    Q-Init
    $id = $req.id
    $tmp = Join-Path $script:Q.Requests ".$id.tmp"
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    [IO.File]::WriteAllText((Join-Path $tmp 'payload.json'), $payloadText, (New-Object Text.UTF8Encoding $false))
    Q-WriteJson (Join-Path $tmp 'request.json') $req
    [IO.Directory]::Move($tmp, (Join-Path $script:Q.Requests $id))
    Q-Say ("in line as {0} ({1}, {2} build(s), build {3} = {4})" -f $id, $req.who, $req.scenarios, $req.build.hash, $req.build.label)
    $parent = 0
    try { $parent = [int](Get-CimInstance Win32_Process -Filter "ProcessId=$PID").ParentProcessId } catch { }
    $parentStart = if ($parent) { Q-ProcStart $parent } else { 0 }
    $lastPrint = [datetime]::MinValue; $lastLine = ''; $lastKey = ''; $lastStart = [datetime]::MinValue
    $orphanSince = $null; $retired = $false
    $finished = $false
    try {
        while ($true) {
            $s = Q-RequestState $id
            if ($s.state -eq 'done') { $finished = $true; return $s.result }
            if ($s.state -eq 'gone') {
                Start-Sleep -Milliseconds 700
                $s = Q-RequestState $id
                if ($s.state -eq 'done') { $finished = $true; return $s.result }
                if ($s.state -eq 'gone') { $finished = $true; Q-Say 'your request was taken out of the line (its folder was deleted)'; return [pscustomobject]@{ code = 2; status = 'cancelled'; message = 'removed from the line' } }
            }
            if ($parent -and $parentStart -and -not (Q-Alive $parent $parentStart)) { Q-Cancel $id 'the command that asked is gone' | Out-Null; $finished = $true; return [pscustomobject]@{ code = 2; status = 'cancelled'; message = 'the asking command is gone' } }
            $disp = Q-DispatcherInfo
            if (-not $disp -and ((Get-Date) - $lastStart).TotalSeconds -gt 20) {
                Start-Sleep -Milliseconds (Get-Random -Minimum 0 -Maximum 800)   # several clients: the dispatcher's own lock keeps one
                if (-not (Q-DispatcherInfo)) {
                    $dp = Q-StartDispatcher $scriptDir
                    Q-Say "no dispatcher was running: started one (PID $dp)"
                }
                $lastStart = Get-Date
            }
            if ($disp -and -not $retired -and [int]$disp.v -lt $script:QVersion) {
                Q-WriteJson $script:Q.Retire ([ordered]@{ v = $script:QVersion; by = $req.who; pid = $PID; at = (Get-Date).ToString('o') })
                Q-Say "the dispatcher running (PID $($disp.pid)) is an older version ($([int]$disp.v)); asked it to hand over when the game is free"
                $retired = $true
            }
            $st = Q-ReadJson $script:Q.Status
            if ($st -and $disp -and $st.dispatcherPid -ne $disp.pid) { $st = $null }
            # stuck? (the dispatcher is alive but has stopped writing status.json, or our request sits in active\ unrun)
            if ($st -and $disp) {
                $age = ((Get-Date) - (Q-Date $st.updated)).TotalSeconds
                if ($age -gt 180) {
                    $finished = $true
                    Q-Say "STUCK: the dispatcher (PID $($disp.pid)) has not updated status.json for $(Q-Duration $age); giving up (see $($script:Q.Log))"
                    return [pscustomobject]@{ code = 1; status = 'stuck'; message = "the game queue's dispatcher stopped answering ($(Q-Duration $age))" }
                }
                if ($s.state -eq 'active' -and $age -lt 15 -and -not ($st.current -and $st.current.id -eq $id)) {
                    if (-not $orphanSince) { $orphanSince = Get-Date }
                    elseif (((Get-Date) - $orphanSince).TotalSeconds -gt 60) {
                        $finished = $true
                        Q-Say "STUCK: your request has been in $($script:Q.Active) for over a minute but the dispatcher is not running it; giving up (see $($script:Q.Log))"
                        return [pscustomobject]@{ code = 1; status = 'stuck'; message = 'the request was taken from the line but never run' }
                    }
                } else { $orphanSince = $null }
            }
            $p = Q-Progress $st $id
            $key = "$($s.state)|$($p.key)"
            if ($key -ne $lastKey -or ((Get-Date) - $lastPrint).TotalSeconds -ge $printEverySec) {
                if ($p.line -ne $lastLine -or ((Get-Date) - $lastPrint).TotalSeconds -ge $printEverySec) { Q-Say $p.line; $lastLine = $p.line; $lastPrint = Get-Date }
                $lastKey = $key
            }
            Start-Sleep -Seconds 2
        }
    } finally {
        if (-not $finished) { Q-Cancel $id 'stopped (Ctrl+C)' | Out-Null }
    }
}

# A results folder name nobody else uses: two agents asking for the same thing in the same second get two folders.
function Q-UniqueRunId([string]$runId, [string]$resultsRoot) {
    $r = $runId
    while (Test-Path -LiteralPath (Join-Path $resultsRoot $r)) { $r = "$runId-$([guid]::NewGuid().ToString('N').Substring(0, 4))" }
    return $r
}

# Run-Suite.ps1 through the queue (Show.ps1 and Play.ps1 call Run-Suite). Returns the exit code Run-Suite would have.
function Invoke-QueuedRun([string]$repo, [hashtable]$o) {
    $playground = [bool]$o.Playground
    $set = $o.Set
    $payload = '{}'
    $setName = $set
    if (-not $playground) {
        $file = if ($set -match '[\/.]' -and (Test-Path -LiteralPath $set -PathType Leaf)) { (Resolve-Path -LiteralPath $set).Path } else { Join-Path $repo "suite\scenarios\$set.json" }
        if (-not (Test-Path -LiteralPath $file)) { Write-Host "STOP: no scenario set $file"; return 2 }
        $payload = Get-Content -LiteralPath $file -Raw -Encoding UTF8
        try { $doc = $payload | ConvertFrom-Json } catch { Write-Host "STOP: $file is not JSON: $($_.Exception.Message)"; return 2 }
        if ($o.MaxScenarios -gt 0 -and $doc.scenarios -and @($doc.scenarios).Count -gt $o.MaxScenarios) {
            $doc.scenarios = @($doc.scenarios | Select-Object -First $o.MaxScenarios)
            $payload = $doc | ConvertTo-Json -Depth 30
        }
        $setName = [IO.Path]::GetFileNameWithoutExtension($file)
    }
    $runId = $o.RunId
    if (-not $runId) { $runId = Q-UniqueRunId ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $setName) $o.ResultsRoot }
    $who = if ($o.Who) { $o.Who } else { Q-Who $repo }
    $snap = Q-Snapshot $repo -NoBuild:([bool]$o.NoBuild)
    if ($snap.code -ne 0) { $snap.tail | ForEach-Object { Write-Host $_ }; Write-Host "STOP: $($snap.message)"; return 3 }
    $needs = if ($playground) { Q-Needs $null $repo } else { Q-Needs ($payload | ConvertFrom-Json) $repo }
    $session = [ordered]@{
        allMods = [bool]$o.AllMods; keepGraphics = [bool]$o.KeepGraphics; noTerrain = [bool]$o.NoTerrain; window = "$($o.Window)"
        pregrowSeconds = [double]$o.PregrowSeconds; opt = "$($o.Opt)"; grid = "$($o.Grid)"; userModSettings = [bool]$o.UserModSettings; look = "$($o.Look)"; pace = "$($o.Pace)"
    }
    $sig = ($session.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ';'
    $id = Q-NewId $who
    $req = [ordered]@{
        v = $script:QVersion; id = $id; seq = (Get-Date).ToUniversalTime().Ticks; created = (Get-Date).ToUniversalTime().ToString('o'); who = $who
        kind = $(if ($playground) { 'play' } else { 'run' }); repo = $repo; set = $setName
        clientPid = $PID; clientStart = (Q-ProcStart $PID)
        build = [ordered]@{ hash = $snap.hash; label = $snap.label; git = $snap.git; dllHash = $snap.dllHash; bundle = $snap.bundle }
        session = $session; key = "$($snap.hash)|$(Q-HashText $sig)".Substring(0, 30)
        scenarios = [int]$needs.scenarios; land = [int]$needs.land; blocks = $needs.blocks; rowPics = @($needs.rowPics); rowLand = @($needs.rowLand)
        runId = $runId; resultsRoot = $o.ResultsRoot; resultsDir = (Join-Path $o.ResultsRoot $runId)
        timeoutMin = [int]$o.TimeoutMin; fresh = $false
    }
    Q-Say ("build {0} ({1}, {2} MB){3}" -f $snap.hash, $snap.label, $snap.mb, $(if ($snap.sameAs) { "; the same build as $($snap.sameAs)" } else { '' }))
    $t0 = Get-Date
    $res = Q-SubmitAndWait $req $payload $PSScriptRoot
    return (Q-PrintRunResult $res $t0)
}

# Send-Suite.ps1 through the queue: the file runs in the queue's game with the requester's current build (Run-Suite's
# default session options, so it shares the game a Show.ps1 of the same build opened). Returns @{ code; resultsDir; result }.
function Invoke-QueuedSend([string]$repo, [string]$text, [string]$base, [hashtable]$o) {
    $doc = $text | ConvertFrom-Json
    $who = if ($o.Who) { $o.Who } else { Q-Who $repo }
    $noBuild = -not $o.Build -and (Test-Path -LiteralPath (Join-Path $repo 'bin\Release\Placemaker.dll'))
    if ($noBuild) { Q-Say "using the build in bin\Release as it is ($((Get-Item -LiteralPath (Join-Path $repo 'bin\Release\Placemaker.dll')).LastWriteTime.ToString('yyyy-MM-dd HH:mm')); -Build rebuilds it first)" }
    $snap = Q-Snapshot $repo -NoBuild:$noBuild
    if ($snap.code -ne 0) { $snap.tail | ForEach-Object { Write-Host $_ }; Write-Host "STOP: $($snap.message)"; return @{ code = 2 } }
    $needs = Q-Needs $doc $repo
    $session = [ordered]@{ allMods = $false; keepGraphics = $false; noTerrain = $false; window = '1600x900'; pregrowSeconds = [double]75; opt = ''; grid = ''; userModSettings = $false; look = 'suite'; pace = 'human' }
    $sig = ($session.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ';'
    $id = Q-NewId $who
    $safe = ($base -replace '[^A-Za-z0-9._-]', '-')
    $runId = Q-UniqueRunId ('send-' + $safe + '-' + (Get-Date -Format 'MMdd-HHmmss')) $o.ResultsRoot
    $req = [ordered]@{
        v = $script:QVersion; id = $id; seq = (Get-Date).ToUniversalTime().Ticks; created = (Get-Date).ToUniversalTime().ToString('o'); who = $who
        kind = 'send'; repo = $repo; set = $base; clientPid = $PID; clientStart = (Q-ProcStart $PID)
        build = [ordered]@{ hash = $snap.hash; label = $snap.label; git = $snap.git; dllHash = $snap.dllHash; bundle = $snap.bundle }
        session = $session; key = "$($snap.hash)|$(Q-HashText $sig)".Substring(0, 30)
        scenarios = [int]$needs.scenarios; land = [int]$needs.land; blocks = $needs.blocks; rowPics = @($needs.rowPics); rowLand = @($needs.rowLand)
        runId = $runId; resultsRoot = $o.ResultsRoot; resultsDir = (Join-Path $o.ResultsRoot $runId)
        timeoutMin = $(if ($o.TimeoutSec -gt 0) { [int][math]::Ceiling($o.TimeoutSec / 60) } else { 0 }); fresh = $false
    }
    Q-Say ("build {0} ({1}){2}" -f $snap.hash, $snap.label, $(if ($snap.sameAs) { "; the same build as $($snap.sameAs)" } else { '' }))
    $res = Q-SubmitAndWait $req $text $PSScriptRoot
    if (-not $res -or $res.status -eq 'cancelled' -or $res.status -eq 'dropped') { Write-Host "STOP: $(if ($res) { $res.message } else { 'no result' })"; return @{ code = 2 } }
    if ($res.status -eq 'stuck') { Write-Host "STOP: $($res.message)"; return @{ code = 1 } }
    Q-Say "$($res.status): waited $(Q-Duration $res.waitedSec), ran $(Q-Duration $res.ranSec) in game $($res.session)$(if ($res.launched) { ' (launched for it)' } else { ' (already open)' })"
    $rf = Join-Path $res.resultsDir 'result.json'
    if (-not (Test-Path -LiteralPath $rf)) { Write-Host "$($res.message)"; return @{ code = [int]$res.code; resultsDir = $res.resultsDir } }
    return @{ code = [int]$res.code; resultsDir = $res.resultsDir; result = $rf }
}

function Q-PrintRunResult($res, [datetime]$t0) {
    if (-not $res) { return 1 }
    if ($res.status -eq 'cancelled' -or $res.status -eq 'dropped') { Write-Host "STOP: $($res.message)"; return 2 }
    if ($res.status -eq 'stuck') { Write-Host "STOP: $($res.message)"; return 1 }
    $how = "waited $(Q-Duration $res.waitedSec), ran $(Q-Duration $res.ranSec); game $($res.session), $(if ($res.launched) { 'launched for this request' } else { 'already open: no launch' })"
    if ($res.status -eq 'served') { Q-Say "done in $(Q-Duration ((Get-Date) - $t0).TotalSeconds) ($how)" }
    else { Q-Say "FAILED ($($res.status)): $($res.message) ($how)" }
    if ($res.resultsDir) { Write-Host "done: $($res.status) -> $($res.resultsDir)" }
    if ($res.installed) { Write-Host "installed now: $($res.installed)" }
    return [int]$res.code
}
