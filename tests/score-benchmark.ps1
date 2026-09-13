# Prints what a benchmark run actually did, so the table in action-benchmark.md can be filled
# in from the record rather than from memory. It reports; it does not score — the pass/fail
# columns are judgement calls about what the user asked for, which the record cannot know.
#
#   .\tests\score-benchmark.ps1 -Since "22:40"
#
# ponytail: groups actions into requests by a time gap, because the record carries no request id.
# Give it a request id in ActionRecordEntry if a run ever gets mis-grouped.

param(
    # Anything Get-Date parses: "22:40", "2026-09-13 22:40". Defaults to the last hour.
    [string]$Since = "",
    # A gap longer than this starts a new request. One hold's actions run back to back.
    [int]$GapSeconds = 3
)

$ErrorActionPreference = "Stop"
$from = if ($Since) { Get-Date $Since } else { (Get-Date).AddHours(-1) }
$recordPath = Join-Path $env:LOCALAPPDATA "Winly\actions.jsonl"
$logDir = Join-Path $env:LOCALAPPDATA "Winly\logs"

Write-Output "Since $($from.ToString('yyyy-MM-dd HH:mm:ss'))"

# ---- the action record: what was attempted and what became of it ----------------------------
if (-not (Test-Path $recordPath)) {
    Write-Output "No action record at $recordPath"
} else {
    $entries = Get-Content $recordPath |
        Where-Object { $_.Trim() } |
        ForEach-Object { $_ | ConvertFrom-Json } |
        # The cast already converts the record's UTC stamp to local, so compare against local.
        Where-Object { [datetime]$_.TimestampUtc -ge $from } |
        Sort-Object { [datetime]$_.TimestampUtc }

    Write-Output ""
    Write-Output "ACTIONS  ($($entries.Count) recorded)"
    $request = 0
    $previous = $null
    foreach ($entry in $entries) {
        $at = ([datetime]$entry.TimestampUtc).ToLocalTime()
        if ($null -eq $previous -or ($at - $previous).TotalSeconds -gt $GapSeconds) {
            $request++
            Write-Output ""
            Write-Output "  request $request  ($($at.ToString('HH:mm:ss')))"
        }
        $previous = $at
        $detail = if ($entry.Argument) { "$($entry.Target) [$($entry.Argument)]" } else { $entry.Target }
        $why = if ($entry.Reason) { " - $($entry.Reason)" } else { "" }
        Write-Output ("    {0,-10} {1,-34} {2}{3}" -f $entry.Verb, $detail, $entry.Status, $why)
    }

    Write-Output ""
    Write-Output "  $request requests, $($entries.Count) actions"
    $entries | Group-Object Status | ForEach-Object { Write-Output "    $($_.Name): $($_.Count)" }
    $overCap = $entries.Count - ($request * 10)
    if ($overCap -gt 0) { Write-Output "    WARNING: more actions than $request requests can hold at the cap of 10" }
}

# ---- the log: how long each turn took, by stage ---------------------------------------------
# Every log touched since the run started, not just the newest: a second instance that starts
# and exits writes the most recent file while the live one keeps logging to an older one.
$logs = Get-ChildItem $logDir -Filter "winly-*.log" -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -ge $from } | Sort-Object LastWriteTime
if (-not $logs) {
    Write-Output ""
    Write-Output "No log written since $($from.ToString('HH:mm:ss')) in $logDir"
    return
}

$pattern = 'activation\.latency ([\d.]+) ms.*?\(transcript ([\-\d.]+) ms, first token ([\-\d.]+) ms, speech ([\-\d.]+) ms\)'
$stamped = '^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})'
$turns = @()
foreach ($line in ($logs | Get-Content)) {
    $when = [regex]::Match($line, $stamped)
    $found = [regex]::Match($line, $pattern)
    if (-not ($when.Success -and $found.Success)) { continue }
    if ([datetime]$when.Groups[1].Value -lt $from) { continue }
    $turns += [pscustomobject]@{
        Total      = [double]$found.Groups[1].Value
        Transcript = [double]$found.Groups[2].Value
        FirstToken = [double]$found.Groups[3].Value
        Speech     = [double]$found.Groups[4].Value
    }
}

Write-Output ""
Write-Output "LATENCY  ($($turns.Count) turns, from $($logs.Name -join ', '))"
if ($turns.Count -eq 0) {
    Write-Output "  none in range - older turns log the total only, without the stage split"
    return
}

function Median([double[]]$values) {
    $sorted = $values | Sort-Object
    $middle = [int][math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2) { $sorted[$middle] } else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
}

foreach ($stage in "Total", "Transcript", "FirstToken", "Speech") {
    $values = $turns.$stage
    Write-Output ("  {0,-11} median {1,8:N0} ms   min {2,8:N0}   max {3,8:N0}" -f
        $stage, (Median $values), ($values | Measure-Object -Minimum).Minimum, ($values | Measure-Object -Maximum).Maximum)
}
$median = Median $turns.Total
Write-Output ""
Write-Output "  SC-001 wants a 4000 ms median: $([math]::Round($median)) ms - $(if ($median -le 4000) { 'meets it' } else { 'MISSES it' })"
