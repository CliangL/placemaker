<#
.SYNOPSIS
  Sends scenarios to a running suite session (Run-Suite.ps1 -Queue) and waits for the result.

.EXAMPLE
  powershell -File tools/suite/Send-Suite.ps1 -File suite/scenarios/examples/parking-entrance.json
  powershell -File tools/suite/Send-Suite.ps1 -Json '{"id":"zoom","on":"parking-entrance","steps":[{"do":"camera","target":"entrance","view":"closeup","shot":"entrance"}]}'
  powershell -File tools/suite/Send-Suite.ps1 -Quit

.DESCRIPTION
  Drops the scenario (a single scenario, or {"scenarios": [...]}, or a command) into <run>/inbox under a time-stamped
  name, waits for <name>.result.json and prints it: per scenario status, failed checks, time, waits and PNG paths.
  -RunId defaults to the newest run whose heartbeat says it is in the queue.
#>
param(
    [string]$File = '',
    [string]$Json = '',
    [switch]$Quit,
    [string]$RunId = '',
    [int]$TimeoutSec = 900,
    [switch]$NoWait,
    [string]$ResultsRoot = 'C:\Users\jack\cs2-area-tool\.forensics\suite\runs'
)
$ErrorActionPreference = 'Stop'
if (-not $RunId) {
    $live = Get-ChildItem $ResultsRoot -Directory | Sort-Object LastWriteTime -Descending | Where-Object {
        $hb = Join-Path $_.FullName 'heartbeat.json'
        if (-not (Test-Path $hb)) { return $false }
        try { $b = Get-Content $hb -Raw | ConvertFrom-Json } catch { return $false }
        return ($b.phase -like 'queue*') -and (((Get-Date).ToUniversalTime() - [datetime]::Parse($b.utc).ToUniversalTime()).TotalSeconds -lt 30)
    } | Select-Object -First 1
    if (-not $live) { Write-Host 'no suite session is waiting in the queue (start one with Run-Suite.ps1 -Queue)'; exit 2 }
    $RunId = $live.Name
}
$inbox = Join-Path (Join-Path $ResultsRoot $RunId) 'inbox'
if (-not (Test-Path $inbox)) { Write-Host "run $RunId has no inbox"; exit 2 }
if ($Quit) { $text = '{"command":"quit"}'; $base = 'quit' }
elseif ($File) { $text = Get-Content $File -Raw; $base = [IO.Path]::GetFileNameWithoutExtension($File) }
elseif ($Json) { $text = $Json; $base = 'inline' }
else { Write-Host 'give -File, -Json or -Quit'; exit 2 }
$null = $text | ConvertFrom-Json   # fail here on bad JSON, not in the game
$name = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + $base
$tmp = Join-Path $inbox "$name.tmp"
[IO.File]::WriteAllText($tmp, $text, (New-Object Text.UTF8Encoding $false))
Move-Item $tmp (Join-Path $inbox "$name.json")
Write-Host "sent $name to run $RunId"
if ($NoWait -or $Quit) { exit 0 }
$result = Join-Path $inbox "$name.result.json"
$t0 = Get-Date
while (-not (Test-Path $result)) {
    if (((Get-Date) - $t0).TotalSeconds -gt $TimeoutSec) { Write-Host "no result after $TimeoutSec s"; exit 1 }
    Start-Sleep -Milliseconds 500
}
Start-Sleep -Milliseconds 200
$r = Get-Content $result -Raw | ConvertFrom-Json
if ($r.error) { Write-Host "ERROR: $($r.error)" }
foreach ($s in $r.scenarios) {
    $failed = if ($s.failed) { (@($s.failed) -join ', ') } else { '' }
    Write-Host ("{0}: {1} in {2} s  failed: {3}" -f $s.id, $s.status, $s.sec, $failed)
    if ($s.waitsMs) { Write-Host ("  waits ms: " + (($s.waitsMs.PSObject.Properties | ForEach-Object { "$($_.Name) $($_.Value)" }) -join ', ')) }
    if ($s.shots) { foreach ($p in $s.shots.PSObject.Properties) { Write-Host ("  shot {0}: {1}" -f $p.Name, (Join-Path (Join-Path $ResultsRoot $RunId) $p.Value)) } }
    Write-Host "  result: $($s.result)"
}
exit 0
