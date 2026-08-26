[CmdletBinding()]
param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe',
    [string]$OutputDirectory,
    [string]$IsolatedProjectPath,
    [int]$BuildTimeoutSeconds = 1800
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
$driverSource = Join-Path $aibtRoot 'Tests\Runtime\Benchmarking\SchedulingPolicyDriver.cs'
$scenariosSource = Join-Path $aibtRoot 'Benchmarks~\Phase4\Scheduling\Unity\SchedulingScenarios.cs'
$benchmarkSource = Join-Path $PSScriptRoot 'Unity'

if (-not (Test-Path -LiteralPath $driverSource -PathType Leaf)) { throw "SchedulingPolicyDriver.cs was not found: $driverSource" }
if (-not (Test-Path -LiteralPath $scenariosSource -PathType Leaf)) { throw "SchedulingScenarios.cs was not found: $scenariosSource" }

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $OutputDirectory = Join-Path $PSScriptRoot "Results\web-build-$stamp"
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw "Output directory already exists; choose another: $OutputDirectory" }
$buildLog = $OutputDirectory.TrimEnd('\') + '-build.log'
$buildEvidencePath = $OutputDirectory.TrimEnd('\') + '-build.raw.json'
if (Test-Path -LiteralPath $buildLog) { throw "Build log already exists: $buildLog" }
if (Test-Path -LiteralPath $buildEvidencePath) { throw "Build evidence already exists: $buildEvidencePath" }

if ([string]::IsNullOrWhiteSpace($IsolatedProjectPath)) {
    $leaf = 'aibt-p4-008-web-player-' + [Guid]::NewGuid().ToString('N')
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

New-Item -ItemType Directory -Path $isolatedAssets -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedPackages -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedSettings -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedAibtRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $OutputDirectory) -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedHarness -Force | Out-Null

Copy-Item -LiteralPath $runtimeSource -Destination (Join-Path $isolatedAibtRoot 'Runtime') -Recurse
Copy-Item -LiteralPath $authoringSource -Destination (Join-Path $isolatedAibtRoot 'Authoring') -Recurse
Copy-Item -Path (Join-Path $benchmarkSource '*') -Destination $isolatedHarness -Recurse
Copy-Item -LiteralPath $driverSource -Destination (Join-Path $isolatedHarness 'Runtime\SchedulingPolicyDriver.cs')
Copy-Item -LiteralPath $scenariosSource -Destination (Join-Path $isolatedHarness 'Runtime\SchedulingScenarios.cs')
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
    '-executeMethod', 'AIBT.Benchmarks.Phase4.Platform.Web.Editor.WebPlatformBenchmarkBuild.Build',
    '-aibtP4008PlayerOutput', $OutputDirectory,
    '-aibtP4008BuildEvidence', $buildEvidencePath,
    '-logFile', $buildLog,
    '-quit'
)

Write-Host "Isolated project: $isolatedRoot"
Write-Host "Building WebGL Player: $OutputDirectory"
$buildExitCode = Start-BoundedProcess -FilePath $UnityPath -Arguments $buildArguments -TimeoutSeconds $BuildTimeoutSeconds -Description 'Unity WebGL build'
if ($buildExitCode -ne 0) { throw "Unity WebGL build failed with exit code $buildExitCode. See $buildLog" }
if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory 'index.html') -PathType Leaf)) { throw "Unity exited successfully but did not produce a WebGL build: $OutputDirectory" }
if (-not (Test-Path -LiteralPath $buildEvidencePath -PathType Leaf)) { throw "Unity exited successfully but did not produce build evidence: $buildEvidencePath" }

$buildEvidence = Get-Content -Raw -LiteralPath $buildEvidencePath | ConvertFrom-Json
if ($buildEvidence.result -ne 'Succeeded' -or $buildEvidence.target -ne 'WebGL' -or $buildEvidence.threadsSupport) {
    throw 'Build evidence does not prove a successful single-thread WebGL Player.'
}

Write-Host "AIBT P4-008 WebGL build completed: $OutputDirectory"
Write-Host "Build log: $buildLog"
Write-Host "Build evidence: $buildEvidencePath"
Write-Host 'Serve this directory over HTTP and load it in a browser to run the probe (WebGL requires HTTP, not file://).'
