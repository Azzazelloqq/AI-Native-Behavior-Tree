[CmdletBinding()]
param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe',
    [int]$WarmupSamples = 5,
    [int]$MeasuredSamples = 15,
    [string]$AgentCounts = '16,64,256,1024',
    [string]$OutputPath,
    [string]$IsolatedProjectPath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity executable was not found: $UnityPath"
}

if ($WarmupSamples -lt 1) { throw 'WarmupSamples must be at least 1.' }
if ($MeasuredSamples -lt 1) { throw 'MeasuredSamples must be at least 1.' }

$aibtRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$projectRoot = Split-Path -Parent (Split-Path -Parent $aibtRoot)
$runtimeSource = Join-Path $aibtRoot 'Runtime'
$authoringSource = Join-Path $aibtRoot 'Authoring'
$driverSource = Join-Path $aibtRoot 'Tests\Runtime\Benchmarking\SchedulingPolicyDriver.cs'
$scenariosSource = Join-Path $aibtRoot 'Benchmarks~\Phase4\Scheduling\Unity\SchedulingScenarios.cs'
$benchmarkSource = Join-Path $PSScriptRoot 'Unity'

if (-not (Test-Path -LiteralPath $driverSource -PathType Leaf)) {
    throw "SchedulingPolicyDriver.cs was not found: $driverSource"
}
if (-not (Test-Path -LiteralPath $scenariosSource -PathType Leaf)) {
    throw "SchedulingScenarios.cs was not found: $scenariosSource"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $OutputPath = Join-Path $PSScriptRoot "Results\auto-comparison-windows-editor-$stamp.json"
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$logPath = [IO.Path]::ChangeExtension($OutputPath, '.log')

if (Test-Path -LiteralPath $OutputPath) {
    throw "Output already exists; choose a new path: $OutputPath"
}
if (Test-Path -LiteralPath $logPath) {
    throw "Log already exists; choose a new output path: $logPath"
}

if ([string]::IsNullOrWhiteSpace($IsolatedProjectPath)) {
    $leaf = 'aibt-p4-006-auto-comparison-' + [Guid]::NewGuid().ToString('N')
    $IsolatedProjectPath = Join-Path ([IO.Path]::GetTempPath()) $leaf
}
$IsolatedProjectPath = [IO.Path]::GetFullPath($IsolatedProjectPath)
if (Test-Path -LiteralPath $IsolatedProjectPath) {
    $existing = @(Get-ChildItem -LiteralPath $IsolatedProjectPath -Force)
    if ($existing.Count -ne 0) {
        throw "IsolatedProjectPath must not exist or must be empty: $IsolatedProjectPath"
    }
}

$assetsRoot = Join-Path $IsolatedProjectPath 'Assets'
$isolatedAibtRoot = Join-Path $assetsRoot 'AIBT'
$packagesRoot = Join-Path $IsolatedProjectPath 'Packages'
$settingsRoot = Join-Path $IsolatedProjectPath 'ProjectSettings'
$outputDirectory = Split-Path -Parent $OutputPath
$benchmarkDestination = Join-Path $assetsRoot 'AIBTAutoComparisonBenchmark'

New-Item -ItemType Directory -Path $isolatedAibtRoot -Force | Out-Null
New-Item -ItemType Directory -Path $packagesRoot -Force | Out-Null
New-Item -ItemType Directory -Path $settingsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $benchmarkDestination -Force | Out-Null

Copy-Item -LiteralPath $runtimeSource -Destination (Join-Path $isolatedAibtRoot 'Runtime') -Recurse
Copy-Item -LiteralPath $authoringSource -Destination (Join-Path $isolatedAibtRoot 'Authoring') -Recurse
Copy-Item -LiteralPath $driverSource -Destination (Join-Path $benchmarkDestination 'SchedulingPolicyDriver.cs')
Copy-Item -LiteralPath $scenariosSource -Destination (Join-Path $benchmarkDestination 'SchedulingScenarios.cs')
Copy-Item -Path (Join-Path $benchmarkSource '*') -Destination $benchmarkDestination -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt') `
    -Destination (Join-Path $settingsRoot 'ProjectVersion.txt')

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
[IO.File]::WriteAllText(
    (Join-Path $packagesRoot 'manifest.json'),
    $manifest,
    [Text.UTF8Encoding]::new($false))

$unityArguments = @(
    '-batchmode',
    '-nographics',
    '-burst-enable-compilation',
    '-projectPath', $IsolatedProjectPath,
    '-executeMethod', 'AIBT.Benchmarks.Phase4.AutoComparison.AutoComparisonRunner.Run',
    '-aibtBenchmarkOutput', $OutputPath,
    '-aibtWarmupSamples', $WarmupSamples.ToString([Globalization.CultureInfo]::InvariantCulture),
    '-aibtMeasuredSamples', $MeasuredSamples.ToString([Globalization.CultureInfo]::InvariantCulture),
    '-aibtAgentCounts', $AgentCounts,
    '-logFile', $logPath,
    '-quit'
)

Write-Host "Isolated project: $IsolatedProjectPath"
Write-Host "Result: $OutputPath"
Write-Host "Log: $logPath"

$quotedArguments = $unityArguments | ForEach-Object {
    if ($_ -match '[\s"]') {
        '"' + ($_ -replace '"', '\"') + '"'
    } else {
        $_
    }
}
$unityProcess = Start-Process `
    -FilePath $UnityPath `
    -ArgumentList ($quotedArguments -join ' ') `
    -WindowStyle Hidden `
    -Wait `
    -PassThru
$unityExitCode = $unityProcess.ExitCode
if ($unityExitCode -ne 0) {
    throw "Unity benchmark failed with exit code $unityExitCode. See $logPath"
}
if (-not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
    throw "Unity exited successfully but did not write the benchmark result: $OutputPath"
}

Write-Host 'Benchmark completed successfully.'
