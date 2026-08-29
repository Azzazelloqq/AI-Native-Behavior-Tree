[CmdletBinding()]
param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe',
    [int]$WarmupSamples = 5,
    [int]$MeasuredSamples = 15,
    [string]$AgentCounts = '16,64,256,1024',
    [string]$OutputPath,
    [string]$IsolatedProjectPath,
    [int]$BuildTimeoutSeconds = 1800,
    [int]$PlayerTimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'

function Start-BoundedProcess {
    param([string]$FilePath, [string[]]$Arguments, [int]$TimeoutSeconds, [string]$Description)
    $quoted = $Arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }
    $process = Start-Process -FilePath $FilePath -ArgumentList ($quoted -join ' ') -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "$Description did not exit within $TimeoutSeconds seconds. PID=$($process.Id)."
    }
    return $process.ExitCode
}

if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity executable was not found: $UnityPath"
}

$aibtRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
$projectRoot = Split-Path -Parent (Split-Path -Parent $aibtRoot)
$runtimeSource = Join-Path $aibtRoot 'Runtime'
$authoringSource = Join-Path $aibtRoot 'Authoring'
$benchmarkSource = Join-Path $PSScriptRoot 'Unity'

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $OutputPath = Join-Path $PSScriptRoot "Results\windows-player-scheduling-$stamp.json"
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
    $leaf = 'aibt-p4-008-windows-player-' + [Guid]::NewGuid().ToString('N')
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
$isolatedHarness = Join-Path $isolatedAssets 'AIBTPlatformBenchmark'
$isolatedHarnessRuntime = Join-Path $isolatedHarness 'Runtime'
$buildRoot = Join-Path $isolatedRoot 'Build\Windows-x64'
$playerExecutable = Join-Path $buildRoot 'AIBTP4008PlatformBenchmark.exe'

New-Item -ItemType Directory -Path $isolatedAssets -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedPackages -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedSettings -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedAibtRoot -Force | Out-Null
New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedHarness -Force | Out-Null

Copy-Item -LiteralPath $runtimeSource -Destination (Join-Path $isolatedAibtRoot 'Runtime') -Recurse
Copy-Item -LiteralPath $authoringSource -Destination (Join-Path $isolatedAibtRoot 'Authoring') -Recurse
Copy-Item -Path (Join-Path $benchmarkSource '*') -Destination $isolatedHarness -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt') -Destination (Join-Path $isolatedSettings 'ProjectVersion.txt')

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
    '-executeMethod', 'AIBT.Benchmarks.Phase4.Platform.Windows.Editor.WindowsPlatformBenchmarkBuild.Build',
    '-aibtP4008PlayerOutput', $playerExecutable,
    '-aibtP4008BuildEvidence', $buildEvidencePath,
    '-logFile', $buildLog,
    '-quit'
)

Write-Host "Isolated project: $isolatedRoot"
Write-Host "Building Windows x64 IL2CPP Player: $playerExecutable"
$buildExitCode = Start-BoundedProcess -FilePath $UnityPath -Arguments $buildArguments -TimeoutSeconds $BuildTimeoutSeconds -Description 'Unity Windows Player build'
if ($buildExitCode -ne 0) { throw "Unity Windows Player build failed with exit code $buildExitCode. See $buildLog" }
if (-not (Test-Path -LiteralPath $playerExecutable -PathType Leaf)) { throw "Unity exited successfully but did not produce the Player: $playerExecutable" }
if (-not (Test-Path -LiteralPath $buildEvidencePath -PathType Leaf)) { throw "Unity exited successfully but did not produce build evidence: $buildEvidencePath" }

$buildEvidence = Get-Content -Raw -LiteralPath $buildEvidencePath | ConvertFrom-Json
if ($buildEvidence.result -ne 'Succeeded' -or $buildEvidence.target -ne 'StandaloneWindows64' -or
    $buildEvidence.scriptingBackend -ne 'IL2CPP' -or -not $buildEvidence.burstEnabled -or $buildEvidence.developmentBuild) {
    throw 'Build evidence does not prove a release IL2CPP x64 Burst-enabled Player.'
}

$playerArguments = @(
    '-batchmode', '-nographics', '-logFile', $playerLog,
    '-aibtBenchmarkOutput', $playerResultPath,
    '-aibtWarmupSamples', $WarmupSamples.ToString([Globalization.CultureInfo]::InvariantCulture),
    '-aibtMeasuredSamples', $MeasuredSamples.ToString([Globalization.CultureInfo]::InvariantCulture),
    '-aibtAgentCounts', $AgentCounts
)
Write-Host 'Launching Windows platform scheduling probe.'
$playerExitCode = Start-BoundedProcess -FilePath $playerExecutable -Arguments $playerArguments -TimeoutSeconds $PlayerTimeoutSeconds -Description 'Windows platform scheduling probe'
if ($playerExitCode -ne 0) { throw "Windows platform scheduling probe failed with exit code $playerExitCode. See $playerLog" }
if (-not (Test-Path -LiteralPath $playerResultPath -PathType Leaf)) { throw "The Player did not produce benchmark results: $playerResultPath" }

$playerLogText = Get-Content -Raw -LiteralPath $playerLog
if ($playerLogText -notmatch 'AIBT_P4_008_WINDOWS_PLAYER_OK\|') { throw 'The Player success marker is missing from the log.' }
if ($playerLogText -match 'AIBT_P4_008_WINDOWS_PLAYER_FAIL\|') { throw 'The Player logged a failure marker.' }

Copy-Item -LiteralPath $playerResultPath -Destination $OutputPath

Write-Host "AIBT P4-008 Windows platform benchmark completed. Evidence: $OutputPath"
Write-Host "Build log: $buildLog"
Write-Host "Player log: $playerLog"
Write-Host "Build evidence: $buildEvidencePath"
