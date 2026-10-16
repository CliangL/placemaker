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
    dispatcher.lock, status.json, history.json, dispatcher.log, launches.log, session.json, installed-before\
  A request carries a frozen snapshot of the requester's build; the dispatcher runs requests with the same build in one
  open game, back to back, and relaunches only to switch builds. See suite/README.md, "The game queue".
#>

$script:QRoot = if ($env:PLACEMAKER_GAME_QUEUE) { $env:PLACEMAKER_GAME_QUEUE } else { Join-Path $env:USERPROFILE '.placemaker\game-queue' }
$script:QFake = $env:PLACEMAKER_GAME_QUEUE_FAKE   # tests only: a fake game host script instead of Run-Suite
$script:Q = @{
    Root = $script:QRoot
    Requests = Join-Path $script:QRoot 'requests'
    Active = Join-Path $script:QRoot 'active'
    Done = Join-Path $script:QRoot 'done'
    Bundles = Join-Path $script:QRoot 'bundles'
    Logs = Join-Path $script:QRoot 'logs'
    Lock = Join-Path $script:QRoot 'dispatcher.lock'
    Status = Join-Path $script:QRoot 'status.json'
    History = Join-Path $script:QRoot 'history.json'
    Session = Join-Path $script:QRoot 'session.json'
    Log = Join-Path $script:QRoot 'dispatcher.log'
    Launches = Join-Path $script:QRoot 'launches.log'
    Installed = Join-Path $script:QRoot 'installed-before'
}
$script:QUserData = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
$script:QMainRuns = if ($env:PLACEMAKER_GAME_QUEUE_RUNS) { $env:PLACEMAKER_GAME_QUEUE_RUNS } else { 'C:\Users\jack\cs2-area-tool\.forensics\suite\runs' }   # Run-Suite's default ResultsRoot (one suite lock for every worktree)
$script:QDefaults = @{ boot = 60.0; quit = 15.0; scenario = 15.0; overhead = 4.0 }

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
# A file another process is writing (the game's logs): read with every share mode.
function Q-CopyShared([string]$src, [string]$dst) {
    if (-not (Test-Path -LiteralPath $src)) { return $false }
    try {
        $in = [IO.File]::Open($src, 'Open', 'Read', 'ReadWrite, Delete')
        try { $out = [IO.File]::Create($dst); try { $in.CopyTo($out) } finally { $out.Dispose() } } finally { $in.Dispose() }
        return $true
    } catch { return $false }
}

function Q-Ticks($p) { try { return $p.StartTime.ToUniversalTime().Ticks } catch { return 0 } }
function Q-ProcStart([int]$id) { $p = Get-Process -Id $id -ErrorAction SilentlyContinue; if ($p) { return (Q-Ticks $p) } else { return 0 } }
# Alive and the same process (a recycled PID has another start time).
function Q-Alive($id, $start = 0) {
    if (-not $id) { return $false }
    $p = Get-Process -Id ([int]$id) -ErrorAction SilentlyContinue
    if (-not $p) { return $false }
    if ($start -and [math]::Abs((Q-Ticks $p) - [int64]$start) -gt 20000000) { return $false }
    return $true
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

function Q-History {
    $h = Q-ReadJson $script:Q.History
    $r = @{ boot = @(); quit = @(); scenario = @(); overhead = @() }
    if ($h) { foreach ($k in @($r.Keys)) { if ($h.$k) { $r[$k] = @($h.$k) } } }
    return $r
}
function Q-HistoryAdd([string]$kind, [double]$sec) {
    if ($sec -le 0) { return }
    $h = Q-History
    $h[$kind] = @(@($h[$kind]) + [math]::Round($sec, 1) | Select-Object -Last 40)
    Q-WriteJson $script:Q.History $h
}
function Q-Rates {
    $h = Q-History
    return @{
        boot = Q-Median $h.boot $script:QDefaults.boot; quit = Q-Median $h.quit $script:QDefaults.quit
        scenario = Q-Median $h.scenario $script:QDefaults.scenario; overhead = Q-Median $h.overhead $script:QDefaults.overhead
    }
}
# First use: measured boots, quits and builds from the runs already on disk.
function Q-SeedHistory {
    if (Test-Path $script:Q.History) { return }
    $h = @{ boot = @(); quit = @(); scenario = @(); overhead = @() }
    if (Test-Path $script:QMainRuns) {
        foreach ($d in Get-ChildItem $script:QMainRuns -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 30) {
            $hj = Q-ReadJson (Join-Path $d.FullName 'host.json')
            if (-not $hj -or $hj.status -ne 'quit' -or -not $hj.phaseAtSec) { continue }
            if ($hj.phaseAtSec.scenarios) { $h.boot += [double]$hj.phaseAtSec.scenarios }
            if ($hj.phaseAtSec.quit -and $hj.totalSec) { $h.quit += [math]::Max(3, [double]$hj.totalSec - [double]$hj.phaseAtSec.quit) }
            $sj = Q-ReadJson (Join-Path $d.FullName 'summary.json')
            if ($sj) { foreach ($r in @($sj.scenarios)) { if ($r -and $r.sec) { $h.scenario += [double]$r.sec } } }
        }
    }
    foreach ($k in @($h.Keys)) { $h[$k] = @($h[$k] | Select-Object -Last 40) }
    Q-WriteJson $script:Q.History $h
}

# ───────────── the line: which request runs next, and when ─────────────

function Q-Queued {
    $r = @()
    foreach ($d in Get-ChildItem $script:Q.Requests -Directory -ErrorAction SilentlyContinue) {
        $j = Q-ReadJson (Join-Path $d.FullName 'request.json')
        if ($j) { $r += $j }
    }
    return @($r | Sort-Object { [int64]$_.seq })
}

# The policy: the user's playground first; else, while a game is open with build K, the oldest request with build K
# (that still fits that game's test grid), unless the head of the line has waited more than StarveSec; else the head.
function Q-SelectNext($queued, [string]$sessKey, [int]$served, [scriptblock]$fits, [double]$atSec = 0, [double]$starveSec = 600) {
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
                return $r
            }
        }
    }
    return $head
}

function Q-WorkSec($r, $rates) { return [double]$r.scenarios * $rates.scenario + $rates.overhead + 6.0 * [double]$r.land }

# The order the dispatcher will take and when each request should be done, from the measured history.
# $sess: @{ key; served; remainSec } for the open game (key $null when none, or when it is closing).
function Q-Plan($queued, $sess, $rates, [double]$starveSec = 600) {
    $pending = @($queued | Where-Object { $_ })
    $t = 0.0; $key = $null; $served = 0; $unknown = $false
    if ($sess) { $t = [double]$sess.remainSec; $key = $sess.key; $served = [int]$sess.served; if ($sess.unknown) { $unknown = $true } }
    $out = @()
    while ($pending.Count -gt 0) {
        $next = Q-SelectNext $pending $key $served $null $t $starveSec
        $relaunch = ($next.kind -eq 'play') -or (-not $key) -or ($next.key -ne $key) -or ($next.fresh -and $served -gt 0)
        $start = $t
        if ($relaunch) { if ($key) { $t += $rates.quit }; $t += $rates.boot; $served = 0 }
        if ($next.kind -eq 'play') { $key = $null }
        else { $t += (Q-WorkSec $next $rates); $key = $next.key; $served++ }
        $out += [pscustomobject]@{ id = $next.id; who = $next.who; kind = $next.kind; scenarios = $next.scenarios; relaunch = $relaunch; startInSec = [math]::Round($start); doneInSec = [math]::Round($t); unknown = $unknown; build = $next.build.hash }
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

# Scenarios, test blocks by kind, and land plots a set needs (expansions counted from the requester's catalog).
function Q-Needs($doc, [string]$repo) {
    $r = [ordered]@{ scenarios = 0; land = 0; blocks = [ordered]@{ Flat = 0; Zoned = 0; Slope = 0; Rough = 0; Survive = 0 } }
    if (-not $doc -or $doc.command) { return $r }
    $list = @(if ($doc.scenarios) { $doc.scenarios } else { $doc })
    $types = @()
    $cat = Join-Path $repo 'suite\catalog.json'
    if (Test-Path $cat) { try { $types = @((Get-Content $cat -Raw | ConvertFrom-Json).types) } catch { } }
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
        if ($sc.on) { continue }
        if ($sc.land) { $r.land += $n; continue }
        $kind = switch ("$($sc.site)".ToLowerInvariant()) { 'zoned' { 'Zoned' } 'slope' { 'Slope' } 'rough' { 'Rough' } 'survive' { 'Survive' } default { 'Flat' } }
        $r.blocks[$kind] += $n
    }
    return $r
}

# ───────────── the frozen build ─────────────

function Q-Say([string]$m) { Write-Host ('queue: ' + $m) }

function Q-HashText([string]$s) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($s))) -replace '-', '').ToLowerInvariant() } finally { $sha.Dispose() }
}

# Builds the requester's worktree without deploying (unless -NoBuild), then freezes what Deploy would copy (bin\Release
# and UI\build) plus the runner scripts the game session is run with (tools\suite, suite\schema.json, ...) into
# bundles\<hash>. The hash of those files is the build's identity.
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
        if (-not (Test-Path (Join-Path $bin 'Placemaker.dll'))) { return [pscustomobject]@{ code = 3; message = "no build in $bin (run without -NoBuild)"; tail = @() } }
        $files = [ordered]@{}
        foreach ($f in Get-ChildItem $bin -Recurse -File) { $files['mod\' + $f.FullName.Substring($bin.Length + 1)] = $f.FullName }
        if (Test-Path $ui) { foreach ($f in Get-ChildItem $ui -Recurse -File) { $files['mod\' + $f.FullName.Substring($ui.Length + 1)] = $f.FullName } }
        foreach ($f in Get-ChildItem (Join-Path $repo 'tools\suite') -File -Filter '*.ps1') { $files['host\tools\suite\' + $f.Name] = $f.FullName }
        # not suite\catalog.json: every run rewrites it (it names the assembly), which would make every request a new build
        foreach ($rel in 'suite\schema.json', 'suite\scenarios\smoke.json') { $p = Join-Path $repo $rel; if (Test-Path $p) { $files['host\' + $rel] = $p } }
        $lines = foreach ($k in ($files.Keys | Sort-Object)) { $k.ToLowerInvariant() + ':' + (Get-FileHash -LiteralPath $files[$k] -Algorithm SHA256).Hash }
        $hash = (Q-HashText ($lines -join "`n")).Substring(0, 12)
        $dllHash = (Get-FileHash (Join-Path $bin 'Placemaker.dll') -Algorithm SHA256).Hash.Substring(0, 8).ToLowerInvariant()
        $git = ''
        try {
            $git = "$(& git -C $repo rev-parse --short HEAD 2>$null)".Trim()
            if ("$(& git -C $repo status --porcelain --untracked-files=no 2>$null)".Trim()) { $git += '-dirty' }
        } catch { }
        if (-not $git) { $git = 'nogit' }
        $dir = Join-Path $script:Q.Bundles $hash
        $bytes = 0; foreach ($k in $files.Keys) { $bytes += (Get-Item -LiteralPath $files[$k]).Length }
        if (-not (Test-Path (Join-Path $dir 'bundle.json'))) {
            $tmp = "$dir.$PID.tmp"
            if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
            foreach ($k in $files.Keys) {
                $dst = Join-Path $tmp $k
                New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
                Copy-Item -LiteralPath $files[$k] -Destination $dst -Force
            }
            Q-WriteJson (Join-Path $tmp 'bundle.json') ([ordered]@{ hash = $hash; git = $git; dllHash = $dllHash; label = "$git+$dllHash"; repo = $repo; created = (Get-Date).ToString('o'); files = $files.Count; bytes = $bytes })
            try { Rename-Item -LiteralPath $tmp -NewName (Split-Path $dir -Leaf) -ErrorAction Stop } catch { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }   # another client froze the same build first
        }
        $b = Q-ReadJson (Join-Path $dir 'bundle.json')
        return [pscustomobject]@{ code = 0; hash = $hash; git = $git; dllHash = $dllHash; label = "$git+$dllHash"; bundle = $dir; mb = [math]::Round($bytes / 1MB, 1); sameAs = $(if ($b -and $b.repo -ne $repo) { $b.repo } else { '' }) }
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
    if (Test-Path (Join-Path $script:Q.Active "$id\request.json")) { return @{ state = 'active' } }
    if (Test-Path (Join-Path $script:Q.Requests "$id\request.json")) { return @{ state = 'queued' } }
    if (Test-Path $d) { return @{ state = 'finishing' } }
    return @{ state = 'gone' }
}

function Q-Cancel([string]$id, [string]$why) {
    $d = Join-Path $script:Q.Requests $id
    if (Test-Path $d) {
        try { Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction Stop; Q-Say "left the line ($why)"; return $true } catch { }
    }
    if (Test-Path (Join-Path $script:Q.Active $id)) { Q-Say "$why, but your request is already in the game; it finishes and its results land in the results folder" }
    return $false
}

# One progress line from status.json (what the dispatcher plans) for request $id.
function Q-ProgressLine($st, [string]$id, [datetime]$since) {
    if (-not $st) { return 'waiting for the dispatcher to start ...' }
    $plan = @($st.plan)
    $me = $null; $ahead = @()
    foreach ($p in $plan) { if ($p.id -eq $id) { $me = $p; break }; $ahead += $p }
    $cur = $st.current
    $now = ''
    if ($cur -and $cur.id -eq $id -and $cur.kind -eq 'play') {
        if ($st.game -and $st.game.readyAt) { return "the playground is open: the controls are yours; close the game when you are done ($(Q-Duration $cur.runningSec) since launch)" }
        return "the playground is starting for you (build $($cur.build), $(Q-Duration $cur.runningSec) in; about a minute)"
    }
    if ($cur -and $cur.id -eq $id) {
        $left = [double]$cur.etaSec
        $now = "running your {0} build(s) in the game ({1} in, build {2}){3}" -f $cur.scenarios, (Q-Duration $cur.runningSec), $cur.build, $(if ($left -gt 0) { "; ETA about " + (Q-Duration $left) } else { '' })
        return $now
    }
    $parts = @()
    if ($me) {
        $parts += "#$(@($ahead).Count + 1) in line"
        if ($ahead.Count -gt 0) {
            $names = @($ahead | Select-Object -First 4 | ForEach-Object { if ($_.kind -eq 'play') { "$($_.who) (the user's playground)" } else { "$($_.who) ($($_.scenarios) build(s)$(if ($_.relaunch) { ', relaunch' }))" } })
            if ($ahead.Count -gt 4) { $names += "$($ahead.Count - 4) more" }
            $parts += 'ahead: ' + ($names -join ', ')
        } else { $parts += 'next' }
    } elseif ($st.state -eq 'starting' -or $st.state -eq 'booting') { $parts += 'starting' }
    if ($cur) {
        if ($cur.kind -eq 'play') { $parts += "now: the user's playground ($($cur.who))" }
        else { $parts += "now: $($cur.who) $($cur.scenarios) build(s), $(Q-Duration $cur.runningSec) in" }
    }
    if ($st.game -and $st.game.build) { $parts += "game open with build $($st.game.build)$(if ($st.game.state -ne 'ready') { " ($($st.game.state))" })" }
    if ($st.note) { $parts += $st.note }
    if ($me) {
        if ($me.unknown -or $st.paused) { $parts += 'ETA unknown until the user''s game or playground ahead is closed' }
        else {
            $parts += $(if ($me.relaunch) { 'your build needs a launch' } else { 'your build is the open game''s' })
            $parts += ('ETA about {0} (around {1:HH:mm})' -f (Q-Duration $me.doneInSec), (Get-Date).AddSeconds([double]$me.doneInSec))
        }
    }
    if ($parts.Count -eq 0) { return 'in line; the dispatcher has not planned it yet' }
    return ($parts -join ' | ')
}

# Puts a request in line and waits for it; prints progress every ~15 s and on every change. Returns the done result,
# or a result with code 2 when the request left the line (Ctrl+C, its folder deleted).
function Q-SubmitAndWait([hashtable]$req, [string]$payloadText, [string]$scriptDir, [int]$printEverySec = 15) {
    Q-Init
    $id = $req.id
    $dir = Join-Path $script:Q.Requests $id
    $tmp = Join-Path $script:Q.Requests ".$id.tmp"
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    [IO.File]::WriteAllText((Join-Path $tmp 'payload.json'), $payloadText, (New-Object Text.UTF8Encoding $false))
    Q-WriteJson (Join-Path $tmp 'request.json') $req
    Rename-Item -LiteralPath $tmp -NewName $id
    Q-Say ("in line as {0} ({1}, {2} build(s), build {3} = {4})" -f $id, $req.who, $req.scenarios, $req.build.hash, $req.build.label)
    $parent = 0
    try { $parent = [int](Get-CimInstance Win32_Process -Filter "ProcessId=$PID").ParentProcessId } catch { }
    $parentStart = if ($parent) { Q-ProcStart $parent } else { 0 }
    $t0 = Get-Date
    $lastPrint = [datetime]::MinValue; $lastLine = ''; $lastState = ''; $lastStart = [datetime]::MinValue
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
            $st = Q-ReadJson $script:Q.Status
            if ($st -and $disp -and $st.dispatcherPid -ne $disp.pid) { $st = $null }
            $line = Q-ProgressLine $st $id $t0
            $state = "$($s.state)/$(if ($st) { $st.state })/$(if ($st -and $st.current) { $st.current.id })"
            if ($state -ne $lastState -or ((Get-Date) - $lastPrint).TotalSeconds -ge $printEverySec) {
                if ($line -ne $lastLine -or ((Get-Date) - $lastPrint).TotalSeconds -ge $printEverySec) { Q-Say $line; $lastLine = $line; $lastPrint = Get-Date }
                $lastState = $state
            }
            Start-Sleep -Seconds 2
        }
    } finally {
        if (-not $finished) { Q-Cancel $id 'stopped (Ctrl+C)' | Out-Null }
    }
}

# Run-Suite.ps1 through the queue (Show.ps1 and Play.ps1 call Run-Suite). Returns the exit code Run-Suite would have.
function Invoke-QueuedRun([string]$repo, [hashtable]$o) {
    $playground = [bool]$o.Playground
    $set = $o.Set
    $payload = '{}'
    $setName = $set
    if (-not $playground) {
        $file = if ($set -match '[\/.]' -and (Test-Path $set -PathType Leaf)) { (Resolve-Path $set).Path } else { Join-Path $repo "suite\scenarios\$set.json" }
        if (-not (Test-Path $file)) { Write-Host "STOP: no scenario set $file"; return 2 }
        $payload = Get-Content $file -Raw -Encoding UTF8
        try { $doc = $payload | ConvertFrom-Json } catch { Write-Host "STOP: $file is not JSON: $($_.Exception.Message)"; return 2 }
        if ($o.MaxScenarios -gt 0 -and $doc.scenarios -and @($doc.scenarios).Count -gt $o.MaxScenarios) {
            $doc.scenarios = @($doc.scenarios | Select-Object -First $o.MaxScenarios)
            $payload = $doc | ConvertTo-Json -Depth 30
        }
        $setName = [IO.Path]::GetFileNameWithoutExtension($file)
    }
    $runId = $o.RunId
    if (-not $runId) { $runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $setName }
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
        v = 1; id = $id; seq = (Get-Date).ToUniversalTime().Ticks; created = (Get-Date).ToUniversalTime().ToString('o'); who = $who
        kind = $(if ($playground) { 'play' } else { 'run' }); repo = $repo; set = $setName
        clientPid = $PID; clientStart = (Q-ProcStart $PID)
        build = [ordered]@{ hash = $snap.hash; label = $snap.label; git = $snap.git; dllHash = $snap.dllHash; bundle = $snap.bundle }
        session = $session; key = "$($snap.hash)|$(Q-HashText $sig)".Substring(0, 30)
        scenarios = [int]$needs.scenarios; land = [int]$needs.land; blocks = $needs.blocks
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
    $noBuild = -not $o.Build -and (Test-Path (Join-Path $repo 'bin\Release\Placemaker.dll'))
    if ($noBuild) { Q-Say "using the build in bin\Release as it is ($((Get-Item (Join-Path $repo 'bin\Release\Placemaker.dll')).LastWriteTime.ToString('yyyy-MM-dd HH:mm')); -Build rebuilds it first)" }
    $snap = Q-Snapshot $repo -NoBuild:$noBuild
    if ($snap.code -ne 0) { $snap.tail | ForEach-Object { Write-Host $_ }; Write-Host "STOP: $($snap.message)"; return @{ code = 2 } }
    $needs = Q-Needs $doc $repo
    $session = [ordered]@{ allMods = $false; keepGraphics = $false; noTerrain = $false; window = '1600x900'; pregrowSeconds = [double]75; opt = ''; grid = ''; userModSettings = $false; look = 'suite'; pace = 'human' }
    $sig = ($session.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ';'
    $id = Q-NewId $who
    $safe = ($base -replace '[^A-Za-z0-9._-]', '-')
    $runId = 'send-' + $safe + '-' + (Get-Date -Format 'MMdd-HHmmss')
    $req = [ordered]@{
        v = 1; id = $id; seq = (Get-Date).ToUniversalTime().Ticks; created = (Get-Date).ToUniversalTime().ToString('o'); who = $who
        kind = 'send'; repo = $repo; set = $base; clientPid = $PID; clientStart = (Q-ProcStart $PID)
        build = [ordered]@{ hash = $snap.hash; label = $snap.label; git = $snap.git; dllHash = $snap.dllHash; bundle = $snap.bundle }
        session = $session; key = "$($snap.hash)|$(Q-HashText $sig)".Substring(0, 30)
        scenarios = [int]$needs.scenarios; land = [int]$needs.land; blocks = $needs.blocks
        runId = $runId; resultsRoot = $o.ResultsRoot; resultsDir = (Join-Path $o.ResultsRoot $runId)
        timeoutMin = $(if ($o.TimeoutSec -gt 0) { [int][math]::Ceiling($o.TimeoutSec / 60) } else { 0 }); fresh = $false
    }
    Q-Say ("build {0} ({1}){2}" -f $snap.hash, $snap.label, $(if ($snap.sameAs) { "; the same build as $($snap.sameAs)" } else { '' }))
    $res = Q-SubmitAndWait $req $text $PSScriptRoot
    if (-not $res -or $res.status -eq 'cancelled' -or $res.status -eq 'dropped') { Write-Host "STOP: $(if ($res) { $res.message } else { 'no result' })"; return @{ code = 2 } }
    Q-Say "$($res.status): waited $(Q-Duration $res.waitedSec), ran $(Q-Duration $res.ranSec) in game $($res.session)$(if ($res.launched) { ' (launched for it)' } else { ' (already open)' })"
    $rf = Join-Path $res.resultsDir 'result.json'
    if (-not (Test-Path $rf)) { Write-Host "$($res.message)"; return @{ code = [int]$res.code; resultsDir = $res.resultsDir } }
    return @{ code = [int]$res.code; resultsDir = $res.resultsDir; result = $rf }
}

function Q-PrintRunResult($res, [datetime]$t0) {
    if (-not $res) { return 1 }
    if ($res.status -eq 'cancelled' -or $res.status -eq 'dropped') { Write-Host "STOP: $($res.message)"; return 2 }
    $how = "waited $(Q-Duration $res.waitedSec), ran $(Q-Duration $res.ranSec); game $($res.session), $(if ($res.launched) { 'launched for this request' } else { 'already open: no launch' })"
    if ($res.status -eq 'served') { Q-Say "done in $(Q-Duration ((Get-Date) - $t0).TotalSeconds) ($how)" }
    else { Q-Say "FAILED ($($res.status)): $($res.message) ($how)" }
    if ($res.resultsDir) { Write-Host "done: $($res.status) -> $($res.resultsDir)" }
    if ($res.installed) { Write-Host "installed now: $($res.installed)" }
    return [int]$res.code
}
