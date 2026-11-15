[CmdletBinding()]
param(
    [ValidateSet('Prepare', 'Build', 'Run', 'All')] [string]$Step = 'All',
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.7.0b1\Editor\Unity.exe',
    [string]$Backend = 'il2cpp',
    [string]$Stripping = 'High',
    [string]$PlayerDir,
    [string]$OutDir = '',
    [int]$Runs = 3,
    [string]$Only = '',
    [int]$MaxN = 0,
    [int]$MinN = 0,
    [switch]$Quick,
    [switch]$DeepProfile,
    [string[]]$ExtraPlayerArgs = @(),
    [int]$BuildTimeoutSeconds = 3600,
    [int]$PlayerTimeoutSeconds = 7200
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (-not $PlayerDir) { $PlayerDir = Join-Path $project "Build\Perf-$Backend" }
if (-not $OutDir) { $OutDir = Join-Path $project 'PerfResults\raw' }
$logs = Join-Path (Split-Path -Parent $OutDir) 'logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$stamp = [DateTime]::Now.ToString('yyyyMMdd-HHmmss')

function Invoke-Bounded([string]$File, [string[]]$Arguments, [int]$Timeout, [string]$What) {
    $quoted = $Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + $_ + '"' } else { $_ } }
    $p = Start-Process -FilePath $File -ArgumentList ($quoted -join ' ') -PassThru -WindowStyle Normal
    if (-not $p.WaitForExit($Timeout * 1000)) { Stop-Process -Id $p.Id -Force; throw "$What timed out" }
    return $p.ExitCode
}

if ($Step -in 'Prepare', 'All') {
    & python (Join-Path $PSScriptRoot 'gen_trees.py') (Join-Path $project 'Assets\AibtPerf\Trees') | Out-Null
    $log = Join-Path $logs "prepare-$stamp.log"
    $code = Invoke-Bounded $UnityPath @('-batchmode', '-nographics', '-projectPath', $project, '-executeMethod', 'AIBT.Perf.Editor.PerfBuild.Prepare', '-logFile', $log, '-quit') $BuildTimeoutSeconds 'Prepare'
    if ($code -ne 0) { throw "Prepare failed ($code), see $log" }
    Write-Host "Prepared. Log: $log"
}
if ($Step -in 'Build', 'All') {
    $log = Join-Path $logs "build-$Backend-$stamp.log"
    $exe = Join-Path $PlayerDir 'AibtPerf.exe'
    $code = Invoke-Bounded $UnityPath @('-batchmode', '-nographics', '-projectPath', $project, '-executeMethod', 'AIBT.Perf.Editor.PerfBuild.Build',
        '-perfPlayerOutput', $exe, '-perfBackend', $Backend, '-perfStripping', $Stripping, '-perfDevelopment', $(if ($DeepProfile) { 'true' } else { 'false' }), '-perfDeep', $(if ($DeepProfile) { 'true' } else { 'false' }), '-logFile', $log) $BuildTimeoutSeconds 'Build'
    if ($code -ne 0) { throw "Build failed ($code), see $log" }
    Get-Content (Join-Path $PlayerDir 'AibtPerf.build.txt')
}
if ($Step -in 'Run', 'All') {
    $exe = Join-Path $PlayerDir 'AibtPerf.exe'
    $out = Join-Path $OutDir "$Backend-$stamp"
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $log = Join-Path $out 'player.log'
    $arguments = @('-logFile', $log, '-screen-fullscreen', '0', '-screen-width', '640', '-screen-height', '360', '-perfOut', $out, '-perfRuns', "$Runs")
    if ($Only) { $arguments += @('-perfOnly', $Only) }
    if ($Quick) { $arguments += '-perfQuick' }
    if ($MaxN -gt 0) { $arguments += @('-perfMaxN', "$MaxN") }
    if ($MinN -gt 0) { $arguments += @('-perfMinN', "$MinN") }
    $arguments += $ExtraPlayerArgs
    $code = Invoke-Bounded $exe $arguments $PlayerTimeoutSeconds 'Player'
    Write-Host "Player exit $code. Output: $out"
    Select-String -Path $log -Pattern 'AIBT_PERF\|(START|DONE|FAULT)|Exception|error' | Select-Object -First 40 | ForEach-Object { $_.Line }
}
