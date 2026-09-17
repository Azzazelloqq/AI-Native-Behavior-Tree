[CmdletBinding()]
param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe',
    [string]$OutputPath,
    [string]$IsolatedProjectPath,
    [int]$BuildTimeoutSeconds = 1800,
    [int]$PlayerTimeoutSeconds = 14400,
    [int]$PlayerCellLimit = 0
)

$ErrorActionPreference = 'Stop'

function Start-BoundedProcess {
    param([string]$FilePath, [string[]]$Arguments, [int]$TimeoutSeconds, [string]$Description, [switch]$Hidden)
    $quoted = $Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }
    $windowStyle = if ($Hidden) { 'Hidden' } else { 'Normal' }
    $process = Start-Process -FilePath $FilePath -ArgumentList ($quoted -join ' ') -PassThru -WindowStyle $windowStyle
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "$Description did not exit within $TimeoutSeconds seconds. PID=$($process.Id)."
    }
    return $process.ExitCode
}

if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity executable was not found: $UnityPath"
}
if ($PlayerCellLimit -lt 0 -or $PlayerCellLimit -gt 60) {
    throw 'PlayerCellLimit must be zero (the canonical 60-cell matrix) or an integer from 1 to 60 for a diagnostic smoke run.'
}

# This script lives at Assets/AIBT/Benchmarks~/Phase7/SwarmArena -> Assets/AIBT is three levels up.
$aibtRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$runtimeSource = Join-Path $aibtRoot 'Runtime'
$authoringSource = Join-Path $aibtRoot 'Authoring'
$analyzersSource = Join-Path $aibtRoot 'Analyzers'
$sampleSource = Join-Path $aibtRoot 'Samples~\SwarmArena'
$harnessSource = Join-Path $PSScriptRoot 'Unity'

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $OutputPath = Join-Path $PSScriptRoot "Results\swarm-arena-player-$stamp.json"
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$resultDirectory = Split-Path -Parent $OutputPath
$resultStem = [IO.Path]::GetFileNameWithoutExtension($OutputPath)
$buildLog = Join-Path $resultDirectory ($resultStem + '-build.log')
$playerLog = Join-Path $resultDirectory ($resultStem + '-player.log')
$buildEvidencePath = Join-Path $resultDirectory ($resultStem + '-build.raw.json')
$playerResultPath = Join-Path $resultDirectory ($resultStem + '-player.raw.json')

foreach ($target in @($OutputPath, $buildLog, $playerLog, $buildEvidencePath, $playerResultPath)) {
    if (Test-Path -LiteralPath $target) { throw "Evidence output already exists; choose another OutputPath: $target" }
}

if ([string]::IsNullOrWhiteSpace($IsolatedProjectPath)) {
    $leaf = 'aibt-p7-035-swarm-arena-player-' + [Guid]::NewGuid().ToString('N')
    $IsolatedProjectPath = Join-Path ([IO.Path]::GetTempPath()) $leaf
}
$isolatedRoot = [IO.Path]::GetFullPath($IsolatedProjectPath)
if (Test-Path -LiteralPath $isolatedRoot) {
    $existing = @(Get-ChildItem -LiteralPath $isolatedRoot -Force)
    if ($existing.Count -ne 0) { throw "IsolatedProjectPath must not exist or must be empty: $isolatedRoot" }
}

$isolatedAssets = Join-Path $isolatedRoot 'Assets'
$isolatedPackages = Join-Path $isolatedRoot 'Packages'
$isolatedSettings = Join-Path $isolatedRoot 'ProjectSettings'
$isolatedAibtRoot = Join-Path $isolatedAssets 'AIBT'
$isolatedSampleRoot = Join-Path $isolatedAibtRoot 'Samples\SwarmArena'
$isolatedHarness = Join-Path $isolatedAssets 'AIBTSwarmArenaBenchmark'
$buildRoot = Join-Path $isolatedRoot 'Build\Windows-x64'
$playerExecutable = Join-Path $buildRoot 'AIBTP7035SwarmArenaBenchmark.exe'

New-Item -ItemType Directory -Path $isolatedAssets -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedPackages -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedSettings -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedAibtRoot -Force | Out-Null
New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedHarness -Force | Out-Null

Copy-Item -LiteralPath $runtimeSource -Destination (Join-Path $isolatedAibtRoot 'Runtime') -Recurse
Copy-Item -LiteralPath $authoringSource -Destination (Join-Path $isolatedAibtRoot 'Authoring') -Recurse
Copy-Item -LiteralPath $analyzersSource -Destination (Join-Path $isolatedAibtRoot 'Analyzers') -Recurse
New-Item -ItemType Directory -Path (Split-Path -Parent $isolatedSampleRoot) -Force | Out-Null
Copy-Item -LiteralPath $sampleSource -Destination $isolatedSampleRoot -Recurse
# The sample's Editor-only trace menu requires the full AIBT.Editor assembly, which is not part of
# this release-Player harness. It has no runtime workload role, so exclude it from the isolated project.
$isolatedSampleEditor = Join-Path $isolatedSampleRoot 'Editor'
if (Test-Path -LiteralPath $isolatedSampleEditor -PathType Container) {
    Remove-Item -LiteralPath $isolatedSampleEditor -Recurse -Force
}
Copy-Item -Path (Join-Path $harnessSource '*') -Destination $isolatedHarness -Recurse
$hostProjectRoot = Split-Path -Parent (Split-Path -Parent $aibtRoot)
Copy-Item -LiteralPath (Join-Path $hostProjectRoot 'ProjectSettings\ProjectVersion.txt') -Destination (Join-Path $isolatedSettings 'ProjectVersion.txt')

$manifest = @'
{
  "dependencies": {
    "com.unity.burst": "1.8.30",
    "com.unity.collections": "6.5.0",
    "com.unity.nuget.newtonsoft-json": "3.2.2",
    "com.unity.modules.jsonserialize": "1.0.0"
  }
}
'@
[IO.File]::WriteAllText((Join-Path $isolatedPackages 'manifest.json'), $manifest, [Text.UTF8Encoding]::new($false))

$buildArguments = @(
    '-batchmode', '-nographics', '-burst-enable-compilation',
    '-projectPath', $isolatedRoot,
    '-executeMethod', 'AIBT.Benchmarks.Phase7.SwarmArena.Editor.SwarmArenaBenchmarkBuild.Build',
    '-aibtP7035PlayerOutput', $playerExecutable,
    '-aibtP7035BuildEvidence', $buildEvidencePath,
    '-logFile', $buildLog,
    '-quit'
)

Write-Host "Isolated project: $isolatedRoot"
Write-Host "Building Windows x64 IL2CPP Player: $playerExecutable"
$buildExitCode = Start-BoundedProcess -FilePath $UnityPath -Arguments $buildArguments -TimeoutSeconds $BuildTimeoutSeconds -Description 'Unity Windows Player build' -Hidden
if ($buildExitCode -ne 0) { throw "Unity Windows Player build failed with exit code $buildExitCode. See $buildLog" }
if (-not (Test-Path -LiteralPath $playerExecutable -PathType Leaf)) { throw "Unity exited successfully but did not produce the Player: $playerExecutable" }
if (-not (Test-Path -LiteralPath $buildEvidencePath -PathType Leaf)) { throw "Unity exited successfully but did not produce build evidence: $buildEvidencePath" }

$buildEvidence = Get-Content -Raw -LiteralPath $buildEvidencePath | ConvertFrom-Json
if ($buildEvidence.result -ne 'Succeeded' -or $buildEvidence.target -ne 'StandaloneWindows64' -or
    $buildEvidence.scriptingBackend -ne 'IL2CPP' -or -not $buildEvidence.burstEnabled -or $buildEvidence.developmentBuild) {
    throw 'Build evidence does not prove a release IL2CPP x64 Burst-enabled Player.'
}

# Intentionally NOT -batchmode/-nographics: the rendering-enabled cells need a real windowed
# graphics device, per this card's own requirement for a normal rendered run alongside the
# CPU-isolation (rendering-disabled) run.
$playerArguments = @(
    '-logFile', $playerLog,
    '-aibtP7035Output', $playerResultPath
)
if ($PlayerCellLimit -gt 0) {
    $playerArguments += @('-aibtP7035CellLimit', $PlayerCellLimit.ToString([Globalization.CultureInfo]::InvariantCulture))
}
Write-Host 'Launching Swarm Arena Player benchmark probe (windowed, real matrix, long-running).'
$playerExitCode = Start-BoundedProcess -FilePath $playerExecutable -Arguments $playerArguments -TimeoutSeconds $PlayerTimeoutSeconds -Description 'Swarm Arena Player benchmark probe'
if ($playerExitCode -ne 0) { throw "Swarm Arena Player benchmark probe failed with exit code $playerExitCode. See $playerLog" }
if (-not (Test-Path -LiteralPath $playerResultPath -PathType Leaf)) { throw "The Player did not produce benchmark results: $playerResultPath" }

$playerLogText = Get-Content -Raw -LiteralPath $playerLog
if ($playerLogText -notmatch 'AIBT_P7_035_SWARM_ARENA_OK\|') { throw 'The Player success marker is missing from the log.' }
if ($playerLogText -match 'AIBT_P7_035_SWARM_ARENA_FAIL\|') { throw 'The Player logged a failure marker.' }

Copy-Item -LiteralPath $playerResultPath -Destination $OutputPath

Write-Host "AIBT P7-035 Swarm Arena Player benchmark completed. Evidence: $OutputPath"
Write-Host "Build log: $buildLog"
Write-Host "Player log: $playerLog"
Write-Host "Build evidence: $buildEvidencePath"
