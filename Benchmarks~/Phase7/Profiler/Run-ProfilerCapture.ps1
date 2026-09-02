[CmdletBinding()]
param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe',
    [string]$IsolatedProjectPath,
    [int]$BuildTimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity executable was not found: $UnityPath"
}

$aibtRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$runtimeSource = Join-Path $aibtRoot 'Runtime'
$authoringSource = Join-Path $aibtRoot 'Authoring'
$benchmarkSource = Join-Path $PSScriptRoot 'Unity'
$projectRoot = Split-Path -Parent (Split-Path -Parent $aibtRoot)

if ([string]::IsNullOrWhiteSpace($IsolatedProjectPath)) {
    $leaf = 'aibt-p7-003-profiler-windows-' + [Guid]::NewGuid().ToString('N')
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
$isolatedHarness = Join-Path $isolatedAssets 'AIBTProfilerCapture'
$buildRoot = Join-Path $isolatedRoot 'Build\Windows-x64'
$playerExecutable = Join-Path $buildRoot 'AIBTP7003ProfilerCapture.exe'

$resultDirectory = Join-Path $PSScriptRoot 'Results'
New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$buildLog = Join-Path $resultDirectory "profiler-capture-$stamp-build.log"
$playerLog = Join-Path $resultDirectory "profiler-capture-$stamp-player.log"
$buildEvidencePath = Join-Path $resultDirectory "profiler-capture-$stamp-build.raw.json"

New-Item -ItemType Directory -Path $isolatedAssets -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedPackages -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedSettings -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedAibtRoot -Force | Out-Null
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
    '-executeMethod', 'AIBT.Benchmarks.Phase7.Profiler.Editor.ProfilerCaptureBuild.Build',
    '-aibtP7003PlayerOutput', $playerExecutable,
    '-aibtP7003BuildEvidence', $buildEvidencePath,
    '-logFile', $buildLog,
    '-quit'
)

Write-Host "Isolated project: $isolatedRoot"
Write-Host "Building Windows x64 Development+ConnectWithProfiler Player: $playerExecutable"
$quotedBuild = $buildArguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }
$buildProcess = Start-Process -FilePath $UnityPath -ArgumentList ($quotedBuild -join ' ') -PassThru -WindowStyle Hidden
if (-not $buildProcess.WaitForExit($BuildTimeoutSeconds * 1000)) {
    Stop-Process -Id $buildProcess.Id -Force -ErrorAction SilentlyContinue
    throw "Unity Windows Player build did not exit within $BuildTimeoutSeconds seconds."
}
if ($buildProcess.ExitCode -ne 0) { throw "Unity Windows Player build failed with exit code $($buildProcess.ExitCode). See $buildLog" }
if (-not (Test-Path -LiteralPath $playerExecutable -PathType Leaf)) { throw "Unity exited successfully but did not produce the Player: $playerExecutable" }
if (-not (Test-Path -LiteralPath $buildEvidencePath -PathType Leaf)) { throw "Unity exited successfully but did not produce build evidence: $buildEvidencePath" }

$buildEvidence = Get-Content -Raw -LiteralPath $buildEvidencePath | ConvertFrom-Json
if ($buildEvidence.result -ne 'Succeeded' -or $buildEvidence.target -ne 'StandaloneWindows64' -or
    $buildEvidence.scriptingBackend -ne 'IL2CPP' -or -not $buildEvidence.burstEnabled -or
    -not $buildEvidence.developmentBuild -or -not $buildEvidence.connectWithProfiler) {
    throw 'Build evidence does not prove a Development, ConnectWithProfiler, Burst-enabled Windows x64 Player.'
}
Write-Host "Build evidence: $buildEvidencePath"

Write-Host "Launching the Player (stays running ~45s, connectable via the Editor's Profiler window)."
$playerProcess = Start-Process -FilePath $playerExecutable -ArgumentList @('-logFile', ('"' + $playerLog + '"')) -PassThru
Write-Host "Player PID: $($playerProcess.Id)"
Write-Host "Player log: $playerLog"
Write-Host $playerProcess.Id
