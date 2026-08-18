param(
    [string]$UnityEditorPath = $env:UNITY_EDITOR_PATH,
    [switch]$SkipReleaseBuild
)

$ErrorActionPreference = 'Stop'
$spikeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$harnessRoot = Join-Path $spikeRoot 'Harness'
$artifactsRoot = Join-Path $spikeRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
$env:UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE = '2'

if ([string]::IsNullOrWhiteSpace($UnityEditorPath)) {
    $UnityEditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe'
}

$resolvedUnity = (Resolve-Path -LiteralPath $UnityEditorPath).Path
$unityVersion = (Get-Item -LiteralPath $resolvedUnity).VersionInfo.ProductVersion
if ($unityVersion -notlike '6000.5.8f1*') { throw "Expected Unity 6000.5.8f1, found $unityVersion" }

$testResults = Join-Path $artifactsRoot 'unity-editmode.xml'
$testLog = Join-Path $artifactsRoot 'unity-editmode.log'
Remove-Item -LiteralPath $testResults, $testLog -Force -ErrorAction SilentlyContinue
$testArguments = @(
    '-batchmode', '-nographics', '-projectPath', ('"' + $harnessRoot + '"'),
    '-runTests', '-testPlatform', 'EditMode', '-testResults', ('"' + $testResults + '"'),
    '-enableCodeCoverage', 'false', '-logFile', ('"' + $testLog + '"')
)
$testProcess = Start-Process -FilePath $resolvedUnity -ArgumentList $testArguments -PassThru -WindowStyle Hidden
if (-not $testProcess.WaitForExit(300000)) {
    Stop-Process -Id $testProcess.Id -Force -ErrorAction SilentlyContinue
    throw "Unity focused tests did not exit within 300s. PID=$($testProcess.Id), Log=$testLog"
}
if ($testProcess.ExitCode -ne 0) { throw "Unity tests failed with exit code $($testProcess.ExitCode). See $testLog" }
if (-not (Test-Path -LiteralPath $testResults)) { throw "Unity did not produce $testResults" }

[xml]$results = Get-Content -Raw -LiteralPath $testResults
$total = [int]$results.'test-run'.total
$failed = [int]$results.'test-run'.failed
$skipped = [int]$results.'test-run'.skipped
if ($total -le 0) { throw 'Unity discovered no focused native ownership tests.' }
if ($failed -ne 0 -or $skipped -ne 0) { throw "Focused tests failed=$failed skipped=$skipped. See $testResults" }

$resolvedPackagesPath = Join-Path $harnessRoot 'Packages\packages-lock.json'
$resolvedPackages = Get-Content -Raw -LiteralPath $resolvedPackagesPath | ConvertFrom-Json
$burstVersion = $resolvedPackages.dependencies.'com.unity.burst'.version
if ($burstVersion -ne '1.8.29') { throw "Expected resolved Burst 1.8.29, found $burstVersion" }

$testLogText = Get-Content -Raw -LiteralPath $testLog
$failurePattern = '(?im)\bBC\d{4}\b|Burst compilation failed|Burst compiler failed|falling back to managed|A Native Collection has not been disposed|Found [1-9]\d* leak'
$releaseWarningPattern = '(?im)\bwarning\s+(?:CS|BC)\d+|\bwarning:|Script attached[^\r\n]*is missing|no valid script is attached'
if ($testLogText -match $failurePattern) { throw "Test log contains Burst fallback/error or native leak markers. See $testLog" }
if ($testLogText -notmatch 'Native leak detection mode: EnabledWithStackTrace') { throw 'Unity did not enable native leak detection with stack traces.' }

if (-not $SkipReleaseBuild) {
    $releaseLog = Join-Path $artifactsRoot 'unity-release-build.log'
    $playerRoot = Join-Path $artifactsRoot 'release-player'
    $resolvedArtifactsRoot = [IO.Path]::GetFullPath($artifactsRoot).TrimEnd('\')
    $resolvedPlayerRoot = [IO.Path]::GetFullPath($playerRoot)
    if (-not $resolvedPlayerRoot.StartsWith($resolvedArtifactsRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean release output outside artifacts: $resolvedPlayerRoot"
    }
    Remove-Item -LiteralPath $releaseLog -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $playerRoot) { Remove-Item -LiteralPath $playerRoot -Recurse -Force }
    $env:AIBT_NATIVE_OWNERSHIP_PLAYER_OUTPUT = $playerRoot
    $releaseArguments = @(
        '-batchmode', '-nographics', '-projectPath', ('"' + $harnessRoot + '"'),
        '-executeMethod', 'AIBT.NativeOwnership.Spike.Editor.ReleaseBuildGate.Build',
        '-quit', '-logFile', ('"' + $releaseLog + '"')
    )
    $releaseProcess = Start-Process -FilePath $resolvedUnity -ArgumentList $releaseArguments -PassThru -WindowStyle Hidden
    if (-not $releaseProcess.WaitForExit(600000)) {
        Stop-Process -Id $releaseProcess.Id -Force -ErrorAction SilentlyContinue
        throw "Unity release build did not exit within 600s. PID=$($releaseProcess.Id), Log=$releaseLog"
    }
    if ($releaseProcess.ExitCode -ne 0) { throw "Unity release build failed with exit code $($releaseProcess.ExitCode). See $releaseLog" }
    if (-not (Test-Path -LiteralPath (Join-Path $playerRoot 'NativeOwnershipProbe.exe'))) { throw 'Release player executable was not produced.' }
    $releaseLogText = Get-Content -Raw -LiteralPath $releaseLog
    if ($releaseLogText -notmatch 'AIBT_NATIVE_OWNERSHIP_RELEASE_BUILD_OK') { throw 'Release build success marker is missing.' }
    if ($releaseLogText -match $failurePattern) { throw "Release log contains Burst fallback/error or native leak markers. See $releaseLog" }
    if ($releaseLogText -match $releaseWarningPattern) { throw "Release log contains compiler or missing-script warnings. See $releaseLog" }

    $playerExecutable = Join-Path $playerRoot 'NativeOwnershipProbe.exe'
    $playerLog = Join-Path $artifactsRoot 'release-player.log'
    Remove-Item -LiteralPath $playerLog -Force -ErrorAction SilentlyContinue
    $playerArguments = @('-batchmode', '-nographics', '-logFile', ('"' + $playerLog + '"'))
    $playerProcess = Start-Process -FilePath $playerExecutable -ArgumentList $playerArguments -PassThru -WindowStyle Hidden
    if (-not $playerProcess.WaitForExit(60000)) {
        Stop-Process -Id $playerProcess.Id -Force -ErrorAction SilentlyContinue
        throw "Release Player ownership probe did not exit within 60s. PID=$($playerProcess.Id), Log=$playerLog"
    }
    if ($playerProcess.ExitCode -ne 0) { throw "Release Player ownership probe failed with exit code $($playerProcess.ExitCode). See $playerLog" }
    if (-not (Test-Path -LiteralPath $playerLog)) { throw 'Release Player ownership probe produced no log.' }
    $playerLogText = Get-Content -Raw -LiteralPath $playerLog
    if ($playerLogText -notmatch 'AIBT_NATIVE_OWNERSHIP_PLAYER_OK') { throw 'Release Player ownership success marker is missing.' }
    if ($playerLogText -match $failurePattern -or $playerLogText -match $releaseWarningPattern) {
        throw "Release Player log contains warning, fallback, Burst error, or native leak markers. See $playerLog"
    }
}

Write-Host "Native ownership verification passed. Unity=$unityVersion Burst=$burstVersion Tests=$total ReleaseBuild=$(-not $SkipReleaseBuild)"
