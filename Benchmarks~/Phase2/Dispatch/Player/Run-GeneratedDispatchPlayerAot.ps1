[CmdletBinding()]
param(
    [string] $UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe',
    [string] $OutputPath,
    [string] $IsolatedProjectPath,
    [int] $BuildTimeoutSeconds = 1800,
    [int] $PlayerTimeoutSeconds = 120,
    [switch] $ControlledInvalid
)

$ErrorActionPreference = 'Stop'

function Quote-ProcessArgument {
    param([string] $Value)

    if ($Value -match '[\s"]') {
        return '"' + ($Value -replace '"', '\"') + '"'
    }

    return $Value
}

function Start-BoundedProcess {
    param(
        [string] $FilePath,
        [string[]] $Arguments,
        [int] $TimeoutSeconds,
        [string] $Description
    )

    $argumentLine = ($Arguments | ForEach-Object { Quote-ProcessArgument $_ }) -join ' '
    $process = Start-Process `
        -FilePath $FilePath `
        -ArgumentList $argumentLine `
        -PassThru `
        -WindowStyle Hidden
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "$Description did not exit within $TimeoutSeconds seconds. PID=$($process.Id)."
    }

    return $process.ExitCode
}

function Assert-CleanLog {
    param(
        [string] $Path,
        [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description did not produce a log: $Path"
    }

    $text = Get-Content -Raw -LiteralPath $Path
    $patterns = [ordered]@{
        'C# compiler error' = '(?im)\berror\s+CS\d{4}\b|\bCS\d{4}\s*:\s*error\b'
        'C# compiler warning' = '(?im)\bwarning\s+CS\d{4}\b|\bCS\d{4}\s*:\s*warning\b'
        'Burst diagnostic' = '(?im)\bBC\d{4}\b'
        'Burst/AOT failure' = '(?im)^(?=[^\r\n]*(?:Burst|AOT))(?=[^\r\n]*(?:\berror\b|\bfailed\b|\bfailure\b|\bexception\b))[^\r\n]*$'
        'Burst/AOT warning' = '(?im)^(?=[^\r\n]*(?:Burst|AOT))(?=[^\r\n]*warning)[^\r\n]*$'
        'IL post-processing failure' = '(?im)^(?=[^\r\n]*ILPP)(?=[^\r\n]*(?:\berror\b|\bfailed\b|\bfailure\b|\bexception\b|\bwarning\b))[^\r\n]*$'
        'Managed fallback' = '(?im)fall(?:ing)?\s+back\s+to\s+managed|managed\s+(?:execution\s+)?fallback|MANAGED_FALLBACK_EXECUTED'
        'Missing script' = '(?im)Script attached[^\r\n]*is missing|no valid script is attached'
        'Native leak' = '(?im)A Native Collection has not been disposed|Found [1-9]\d* leak'
        'Probe failure marker' = '(?im)AIBT_P2_012_PLAYER_AOT_FAIL\|'
    }

    foreach ($entry in $patterns.GetEnumerator()) {
        $match = [regex]::Match($text, $entry.Value)
        if ($match.Success) {
            $lineStart = $text.LastIndexOf("`n", [Math]::Max(0, $match.Index - 1)) + 1
            $lineEnd = $text.IndexOf("`n", $match.Index)
            if ($lineEnd -lt 0) { $lineEnd = $text.Length }
            $line = $text.Substring($lineStart, $lineEnd - $lineStart).Trim()
            throw "$Description contains $($entry.Key): $line"
        }
    }
}

function Assert-PositiveBuildProof {
    param([string] $Path)

    $text = Get-Content -Raw -LiteralPath $Path
    $required = [ordered]@{
        'generated canary assembly compilation' = '(?im)Csc[^\r\n]*AIBT\.NativeBurstDispatch\.Tests\.dll'
        'generated canary assembly IL post-processing' = '(?im)ILPostProcess[^\r\n]*AIBT\.NativeBurstDispatch\.Tests\.dll'
        'Burst IL post-processor execution' = '(?im)running zzzUnity\.Burst\.CodeGen\.BurstILPostProcessor'
        'Burst AOT compiler completion' = '(?im)bcl\.exe exited after \d+ ms\.'
        'release build marker' = '(?im)AIBT_P2_012_PLAYER_AOT_BUILD_OK\|'
    }

    foreach ($entry in $required.GetEnumerator()) {
        if ($text -notmatch $entry.Value) {
            throw "Unity build log is missing positive proof of $($entry.Key)."
        }
    }
}

function Get-FileSetFingerprint {
    param([string] $Root)

    $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $prefix = $resolvedRoot + '\'
    $lines = @(
        Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File |
            Sort-Object FullName |
            ForEach-Object {
                $relative = $_.FullName.Substring($prefix.Length).Replace('\', '/')
                $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                $relative + "`t" + $hash
            }
    )
    $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Get-Percentile {
    param(
        [double[]] $Values,
        [double] $Percentile
    )

    if (-not $Values -or $Values.Count -eq 0) { throw 'Percentile input is empty.' }
    $ordered = @($Values | Sort-Object)
    $rank = [Math]::Ceiling(($Percentile / 100.0) * $ordered.Count) - 1
    $index = [Math]::Max(0, [Math]::Min($ordered.Count - 1, [int]$rank))
    return [double]$ordered[$index]
}

function Get-TextSha256 {
    param([string] $Text)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)))).Replace('-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

$unityExecutable = [IO.Path]::GetFullPath($UnityPath)
if (-not (Test-Path -LiteralPath $unityExecutable -PathType Leaf)) {
    throw "Unity executable was not found: $unityExecutable"
}

$unityVersion = (Get-Item -LiteralPath $unityExecutable).VersionInfo.ProductVersion
if ($unityVersion -notlike '6000.5.8f1*') {
    throw "Expected Unity 6000.5.8f1, found $unityVersion."
}

$playerRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dispatchRoot = Split-Path -Parent $playerRoot
$aibtRoot = [IO.Path]::GetFullPath((Join-Path $playerRoot '..\..\..\..'))
$unityHarnessSource = Join-Path $playerRoot 'Unity'
$windowsBaselineSource = Join-Path $playerRoot '..\..\Windows\Unity\Runtime\GeneratedDispatchWindowsBaselineProbe.cs'
$sourceProjectRoot = Split-Path -Parent (Split-Path -Parent $aibtRoot)
$projectVersionSource = Join-Path $sourceProjectRoot 'ProjectSettings\ProjectVersion.txt'
$analyzerPath = Join-Path $aibtRoot 'Analyzers\AIBT.CodeGen.dll'
$analyzerMetaPath = $analyzerPath + '.meta'
$playerAsmdefPath = Join-Path $unityHarnessSource 'Runtime\AIBT.NativeBurstDispatch.Tests.asmdef'
$nodeAsmdefPath = Join-Path $unityHarnessSource 'Runtime\Nodes\AIBT.GeneratedDispatchPlayerAot.Nodes.asmdef'
$expectedAnalyzerSha256 = 'a7e6765b530b112591d0a2302271b13bd2675f4f1246d07a3cf7730d72c96dbc'

if ($ControlledInvalid) {
    $invalidEvidence = Join-Path ([IO.Path]::GetTempPath()) ('aibt-p2-022-controlled-invalid-' + [Guid]::NewGuid().ToString('N') + '.json')
    try {
        [IO.File]::WriteAllText(
            $invalidEvidence,
            '{"schema":"aibt-p2-022-windows-baseline-raw-v1","passed":true,"samples":[]}',
            [Text.UTF8Encoding]::new($false))
        $verifier = Join-Path $aibtRoot 'Tools~\Verification\P2\Windows\Verify-WindowsBaselineEvidence.ps1'
        $rejected = $false
        try { & $verifier -EvidencePath $invalidEvidence | Out-Null }
        catch { $rejected = $true }
        if (-not $rejected) { throw 'Controlled-invalid evidence was accepted.' }
    }
    finally {
        Remove-Item -LiteralPath $invalidEvidence -Force -ErrorAction SilentlyContinue
    }
    throw 'AIBT_P2_022_CONTROLLED_INVALID_REJECTED: malformed baseline evidence was rejected by the production JSON Schema verifier.'
}

if (-not (Test-Path -LiteralPath (Join-Path $aibtRoot 'package.json') -PathType Leaf)) {
    throw "The AIBT package root is invalid: $aibtRoot"
}
if (-not (Test-Path -LiteralPath $analyzerPath -PathType Leaf)) {
    throw "The checked source generator was not found: $analyzerPath"
}
$sourceAnalyzerHash = (Get-FileHash -LiteralPath $analyzerPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($sourceAnalyzerHash -ne $expectedAnalyzerSha256) {
    throw "The checked source generator drifted from the frozen P2 snapshot. Expected=$expectedAnalyzerSha256 Actual=$sourceAnalyzerHash"
}
$analyzerMeta = Get-Content -Raw -LiteralPath $analyzerMetaPath
if ($analyzerMeta -notmatch 'guid:\s*31a3f09584684895a0a72916d3ad4de0' -or
    $analyzerMeta -notmatch '(?m)^- RoslynAnalyzer\s*$') {
    throw 'The AIBT source-generator analyzer GUID does not match the Player asmdef.'
}
$playerAsmdef = Get-Content -Raw -LiteralPath $playerAsmdefPath | ConvertFrom-Json
$nodeAsmdef = Get-Content -Raw -LiteralPath $nodeAsmdefPath | ConvertFrom-Json
if ($playerAsmdef.name -ne 'AIBT.NativeBurstDispatch.Tests' -or
    $playerAsmdef.analyzers -notcontains 'GUID:31a3f09584684895a0a72916d3ad4de0' -or
    $playerAsmdef.references -notcontains 'AIBT.GeneratedDispatchPlayerAot.Nodes' -or
    $nodeAsmdef.name -ne 'AIBT.GeneratedDispatchPlayerAot.Nodes' -or
    $nodeAsmdef.analyzers -notcontains 'GUID:31a3f09584684895a0a72916d3ad4de0') {
    throw 'The Player node/catalog asmdef topology does not reference the checked source generator, friend assembly, and explicit node assembly.'
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $OutputPath = Join-Path $dispatchRoot "Results\windows-player-generated-dispatch-aot-$stamp.json"
}
$finalEvidencePath = [IO.Path]::GetFullPath($OutputPath)
$resultDirectory = Split-Path -Parent $finalEvidencePath
$resultStem = [IO.Path]::GetFileNameWithoutExtension($finalEvidencePath)
$buildLog = Join-Path $resultDirectory ($resultStem + '-build.log')
$playerLog = Join-Path $resultDirectory ($resultStem + '-player.log')
$buildEvidencePath = Join-Path $resultDirectory ($resultStem + '-build.raw.json')
$playerEvidencePath = Join-Path $resultDirectory ($resultStem + '-player.raw.json')
$windowsBaselineEvidencePath = Join-Path $resultDirectory ($resultStem + '-windows-baseline.raw.json')

$evidenceTargets = @(
    $finalEvidencePath,
    $buildLog,
    $playerLog,
    $buildEvidencePath,
    $playerEvidencePath,
    $windowsBaselineEvidencePath
)
foreach ($target in $evidenceTargets) {
    if (Test-Path -LiteralPath $target) {
        throw "Evidence output already exists; choose another OutputPath: $target"
    }
}

if ([string]::IsNullOrWhiteSpace($IsolatedProjectPath)) {
    $leaf = 'aibt-p2-012-player-aot-' + [Guid]::NewGuid().ToString('N')
    $IsolatedProjectPath = Join-Path ([IO.Path]::GetTempPath()) $leaf
}
$isolatedRoot = [IO.Path]::GetFullPath($IsolatedProjectPath)
if (Test-Path -LiteralPath $isolatedRoot) {
    $existing = @(Get-ChildItem -LiteralPath $isolatedRoot -Force)
    if ($existing.Count -ne 0) {
        throw "IsolatedProjectPath must not exist or must be empty: $isolatedRoot"
    }
}

$isolatedAssets = Join-Path $isolatedRoot 'Assets'
$isolatedPackages = Join-Path $isolatedRoot 'Packages'
$isolatedSettings = Join-Path $isolatedRoot 'ProjectSettings'
$isolatedAibtRoot = Join-Path $isolatedAssets 'AIBT'
$isolatedHarness = Join-Path $isolatedAssets 'AIBTP2GeneratedDispatchPlayerAot'
$buildRoot = Join-Path $isolatedRoot 'Build\Windows-x64'
$playerExecutable = Join-Path $buildRoot 'AIBTP2012GeneratedDispatch.exe'

New-Item -ItemType Directory -Path $isolatedAssets -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedPackages -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedSettings -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedAibtRoot -Force | Out-Null
New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
Copy-Item -LiteralPath $unityHarnessSource -Destination $isolatedHarness -Recurse
$sourceHarnessHash = Get-FileSetFingerprint $unityHarnessSource
$isolatedBaseHarnessHash = Get-FileSetFingerprint $isolatedHarness
if ($sourceHarnessHash -ne $isolatedBaseHarnessHash) {
    throw 'The isolated Player harness does not match its benchmark source snapshot.'
}
if (-not (Test-Path -LiteralPath $windowsBaselineSource -PathType Leaf)) {
    throw "The P2-022 Windows baseline probe was not found: $windowsBaselineSource"
}
$isolatedWindowsBaseline = Join-Path $isolatedHarness 'Runtime\GeneratedDispatchWindowsBaselineProbe.cs'
Copy-Item -LiteralPath $windowsBaselineSource -Destination $isolatedWindowsBaseline
Copy-Item -LiteralPath (Join-Path $aibtRoot 'Runtime') -Destination (Join-Path $isolatedAibtRoot 'Runtime') -Recurse
Copy-Item -LiteralPath (Join-Path $aibtRoot 'Analyzers') -Destination (Join-Path $isolatedAibtRoot 'Analyzers') -Recurse
Copy-Item -LiteralPath $projectVersionSource -Destination (Join-Path $isolatedSettings 'ProjectVersion.txt')

$manifest = [ordered]@{
    dependencies = [ordered]@{
        'com.unity.burst' = '1.8.29'
        'com.unity.collections' = '6.5.0'
        'com.unity.nuget.newtonsoft-json' = '3.2.2'
        'com.unity.modules.jsonserialize' = '1.0.0'
    }
} | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText(
    (Join-Path $isolatedPackages 'manifest.json'),
    $manifest,
    [Text.UTF8Encoding]::new($false))

$isolatedAnalyzerPath = Join-Path $isolatedAibtRoot 'Analyzers\AIBT.CodeGen.dll'
$isolatedAnalyzerHash = (Get-FileHash -LiteralPath $isolatedAnalyzerPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($sourceAnalyzerHash -ne $isolatedAnalyzerHash) {
    throw 'The isolated source-generator DLL does not match the frozen source artifact.'
}
$sourceRuntimeHash = Get-FileSetFingerprint (Join-Path $aibtRoot 'Runtime')
$isolatedRuntimeHash = Get-FileSetFingerprint (Join-Path $isolatedAibtRoot 'Runtime')
if ($sourceRuntimeHash -ne $isolatedRuntimeHash) {
    throw 'The isolated Runtime snapshot does not match the frozen source tree.'
}
$sourceWindowsBaselineHash = (Get-FileHash -LiteralPath $windowsBaselineSource -Algorithm SHA256).Hash.ToLowerInvariant()
$isolatedWindowsBaselineHash = (Get-FileHash -LiteralPath $isolatedWindowsBaseline -Algorithm SHA256).Hash.ToLowerInvariant()
if ($sourceWindowsBaselineHash -ne $isolatedWindowsBaselineHash) {
    throw 'The isolated Windows baseline probe does not match its source snapshot.'
}

$buildArguments = @(
    '-batchmode',
    '-nographics',
    '-burst-enable-compilation',
    '-projectPath', $isolatedRoot,
    '-executeMethod', 'AIBT.Benchmarks.Phase2.Dispatch.Player.Editor.GeneratedDispatchPlayerAotBuild.Build',
    '-aibtP2PlayerOutput', $playerExecutable,
    '-aibtP2BuildEvidence', $buildEvidencePath,
    '-logFile', $buildLog,
    '-quit'
)

Write-Output "Isolated project: $isolatedRoot"
Write-Output "Building Windows x64 IL2CPP Player: $playerExecutable"
$buildExitCode = Start-BoundedProcess `
    -FilePath $unityExecutable `
    -Arguments $buildArguments `
    -TimeoutSeconds $BuildTimeoutSeconds `
    -Description 'Unity Windows Player build'
if ($buildExitCode -ne 0) {
    throw "Unity Windows Player build failed with exit code $buildExitCode. See $buildLog"
}

if (-not (Test-Path -LiteralPath $playerExecutable -PathType Leaf)) {
    throw "Unity exited successfully but did not produce the Player: $playerExecutable"
}
if (-not (Test-Path -LiteralPath $buildEvidencePath -PathType Leaf)) {
    throw "Unity exited successfully but did not produce build evidence: $buildEvidencePath"
}
Assert-CleanLog -Path $buildLog -Description 'Unity build log'
Assert-PositiveBuildProof -Path $buildLog

$buildEvidence = Get-Content -Raw -LiteralPath $buildEvidencePath | ConvertFrom-Json
if ($buildEvidence.result -ne 'Succeeded' -or
    $buildEvidence.target -ne 'StandaloneWindows64' -or
    $buildEvidence.architecture -ne 'x86_64' -or
    $buildEvidence.scriptingBackend -ne 'IL2CPP' -or
    -not $buildEvidence.burstEnabled -or
    $buildEvidence.developmentBuild -or
    -not $buildEvidence.catalogUsable) {
    throw 'Build evidence does not prove a release IL2CPP x64 Player with Burst and the generated catalog.'
}
$packagesLockPath = Join-Path $isolatedPackages 'packages-lock.json'
if (-not (Test-Path -LiteralPath $packagesLockPath -PathType Leaf)) {
    throw 'Unity did not write Packages/packages-lock.json for the isolated build.'
}
$packagesLock = Get-Content -Raw -LiteralPath $packagesLockPath | ConvertFrom-Json
$resolvedBurstVersion = $packagesLock.dependencies.'com.unity.burst'.version
$resolvedCollectionsVersion = $packagesLock.dependencies.'com.unity.collections'.version
$resolvedNewtonsoftVersion = $packagesLock.dependencies.'com.unity.nuget.newtonsoft-json'.version
if ($resolvedBurstVersion -ne '1.8.29' -or
    $resolvedCollectionsVersion -ne '6.5.0' -or
    $resolvedNewtonsoftVersion -ne '3.2.2') {
    throw "Unexpected resolved packages: Burst=$resolvedBurstVersion Collections=$resolvedCollectionsVersion NewtonsoftJson=$resolvedNewtonsoftVersion."
}

$burstLibraries = @(
    Get-ChildItem -LiteralPath $buildRoot -Recurse -File -Filter 'lib_burst_generated.dll'
)
if ($burstLibraries.Count -ne 1 -or $burstLibraries[0].Length -le 0 -or
    $burstLibraries[0].FullName -notmatch '[\\/]Plugins[\\/]x86_64[\\/]lib_burst_generated\.dll$') {
    throw 'The Windows Player does not contain exactly one non-empty lib_burst_generated.dll AOT artifact.'
}
$gameAssembly = Join-Path $buildRoot 'GameAssembly.dll'
$globalMetadata = Join-Path $buildRoot 'AIBTP2012GeneratedDispatch_Data\il2cpp_data\Metadata\global-metadata.dat'
if (-not (Test-Path -LiteralPath $gameAssembly -PathType Leaf) -or
    -not (Test-Path -LiteralPath $globalMetadata -PathType Leaf)) {
    throw 'The Windows Player is missing IL2CPP GameAssembly.dll or global-metadata.dat.'
}

$playerArguments = @(
    '-batchmode',
    '-nographics',
    '-logFile', $playerLog,
    '-aibtP2PlayerResult', $playerEvidencePath,
    '-aibtP2WindowsBaselineResult', $windowsBaselineEvidencePath
)
Write-Output 'Launching generated-dispatch Player probe.'
$playerExitCode = Start-BoundedProcess `
    -FilePath $playerExecutable `
    -Arguments $playerArguments `
    -TimeoutSeconds $PlayerTimeoutSeconds `
    -Description 'Generated-dispatch Player probe'
if ($playerExitCode -ne 0) {
    throw "Generated-dispatch Player probe failed with exit code $playerExitCode. See $playerLog"
}

Assert-CleanLog -Path $playerLog -Description 'Player log'
if (-not (Test-Path -LiteralPath $playerEvidencePath -PathType Leaf)) {
    throw "The Player did not produce runtime evidence: $playerEvidencePath"
}
$playerLogText = Get-Content -Raw -LiteralPath $playerLog
if ($playerLogText -notmatch 'AIBT_P2_012_PLAYER_AOT_OK\|') {
    throw 'The Player success marker is missing.'
}
if ($playerLogText -notmatch 'AIBT_P2_022_WINDOWS_BASELINE_OK\|') {
    throw 'The Windows baseline success marker is missing.'
}

$playerEvidence = Get-Content -Raw -LiteralPath $playerEvidencePath | ConvertFrom-Json
if (-not $playerEvidence.passed -or
    -not $playerEvidence.il2cpp -or
    -not $playerEvidence.process64Bit -or
    -not $playerEvidence.burstEnabled -or
    -not $playerEvidence.catalogUsable -or
    $playerEvidence.managedPathSentinel -ne 0 -or
    $playerEvidence.executionCode -ne 'Success' -or
    $playerEvidence.callbackFailure -ne 'Success' -or
    $playerEvidence.callbackStatus -ne 'Success' -or
    $playerEvidence.memoryValue -ne 38 -or
    -not $playerEvidence.zeroAssetIdSentinelPreserved -or
    -not $playerEvidence.behaviorMatrixPassed -or
    @($playerEvidence.behaviorMatrixCases).Count -ne 3) {
    throw 'Runtime evidence does not prove generated Burst dispatch and its expected observable behavior.'
}
if (-not (Test-Path -LiteralPath $windowsBaselineEvidencePath -PathType Leaf)) {
    throw "The Player did not produce Windows baseline evidence: $windowsBaselineEvidencePath"
}
$windowsBaselineRaw = Get-Content -Raw -LiteralPath $windowsBaselineEvidencePath | ConvertFrom-Json
if (-not $windowsBaselineRaw.passed -or
    $windowsBaselineRaw.schema -ne 'aibt-p2-022-windows-baseline-raw-v1' -or
    $windowsBaselineRaw.stopwatchFrequency -le 0 -or
    $windowsBaselineRaw.generatedDispatchProgramPayloadBytes -le 0 -or
    $windowsBaselineRaw.generatedDispatchInstancePayloadBytes -le 0 -or
    -not $windowsBaselineRaw.behaviorCasesPassed -or
    @($windowsBaselineRaw.behaviorCases).Count -ne 4 -or
    @($windowsBaselineRaw.samples).Count -ne 4) {
    throw 'The Windows baseline raw evidence is malformed or incomplete.'
}

$scenarioSummaries = @()
foreach ($scenario in @($windowsBaselineRaw.samples)) {
    $ticks = @($scenario.rawElapsedTicks | ForEach-Object { [double]$_ })
    if ($ticks.Count -ne 15 -or @($ticks | Where-Object { $_ -le 0 }).Count -ne 0 -or
        $scenario.iterationsPerSample -le 0 -or $scenario.nativeProgramBytes -lt 0 -or
        $scenario.nativeBytesPerInstance -lt 0) {
        throw "Windows baseline scenario '$($scenario.name)' has invalid raw measurements."
    }
    $frameNanoseconds = @($ticks | ForEach-Object {
        $_ * 1000000000.0 / [double]$windowsBaselineRaw.stopwatchFrequency
    })
    $perIteration = @($frameNanoseconds | ForEach-Object { $_ / [double]$scenario.iterationsPerSample })
    $stepsPerSecond = @($ticks | ForEach-Object {
        if ($scenario.stepsPerSample -eq 0) { 0.0 }
        else { [double]$scenario.stepsPerSample * [double]$windowsBaselineRaw.stopwatchFrequency / $_ }
    })
    $commandsPerSecond = @($ticks | ForEach-Object {
        if ($scenario.commandsPerSample -eq 0) { 0.0 }
        else { [double]$scenario.commandsPerSample * [double]$windowsBaselineRaw.stopwatchFrequency / $_ }
    })
    $summary = [ordered]@{
        name = $scenario.name
        iterationsPerSample = [int]$scenario.iterationsPerSample
        rawElapsedTicks = @($scenario.rawElapsedTicks)
        p50FrameContributionNanoseconds = Get-Percentile $frameNanoseconds 50
        p95FrameContributionNanoseconds = Get-Percentile $frameNanoseconds 95
        p99FrameContributionNanoseconds = Get-Percentile $frameNanoseconds 99
        p50NanosecondsPerIteration = Get-Percentile $perIteration 50
        p50StepsPerSecond = Get-Percentile $stepsPerSecond 50
        p50CommandsPerSecond = Get-Percentile $commandsPerSecond 50
        p50SchedulingNanoseconds = 0.0
        p50CompletionNanoseconds = 0.0
        nativeProgramBytes = [long]$scenario.nativeProgramBytes
        nativeBytesPerInstance = [long]$scenario.nativeBytesPerInstance
        measuredHeapDeltaBytes = [long]$scenario.measuredHeapDeltaBytes
        gen0CollectionDelta = [int]$scenario.gen0CollectionDelta
        allocationMetricLimitation = $scenario.allocationMetricLimitation
    }
    if ($scenario.name -eq 'scheduling-overhead') {
        $scheduleTicks = @($scenario.rawSchedulingTicks | ForEach-Object { [double]$_ })
        $completionTicks = @($scenario.rawCompletionTicks | ForEach-Object { [double]$_ })
        if ($scheduleTicks.Count -ne 15 -or $completionTicks.Count -ne 15 -or
            @($scheduleTicks + $completionTicks | Where-Object { $_ -le 0 }).Count -ne 0) {
            throw 'Scheduling scenario is missing its separate scheduling/completion measurements.'
        }
        $summary.p50SchedulingNanoseconds = Get-Percentile @($scheduleTicks | ForEach-Object {
            $_ * 1000000000.0 / [double]$windowsBaselineRaw.stopwatchFrequency / [double]$scenario.iterationsPerSample
        }) 50
        $summary.p50CompletionNanoseconds = Get-Percentile @($completionTicks | ForEach-Object {
            $_ * 1000000000.0 / [double]$windowsBaselineRaw.stopwatchFrequency / [double]$scenario.iterationsPerSample
        }) 50
    }
    $scenarioSummaries += $summary
}

$blackboardSamples = @($playerEvidence.rawNanosecondsPerDispatch | ForEach-Object { [double]$_ })
if ($blackboardSamples.Count -lt 7 -or @($blackboardSamples | Where-Object { $_ -le 0 }).Count -ne 0) {
    throw 'The generated blackboard-read dispatch scenario has invalid raw measurements.'
}
$blackboardFrameSamples = @($blackboardSamples | ForEach-Object { $_ * [double]$playerEvidence.measurementIterationsPerSample })
$scenarioSummaries += [ordered]@{
    name = 'blackboard-heavy-generated-dispatch'
    iterationsPerSample = [int]$playerEvidence.measurementIterationsPerSample
    rawNanosecondsPerIteration = $blackboardSamples
    p50FrameContributionNanoseconds = Get-Percentile $blackboardFrameSamples 50
    p95FrameContributionNanoseconds = Get-Percentile $blackboardFrameSamples 95
    p99FrameContributionNanoseconds = Get-Percentile $blackboardFrameSamples 99
    p50NanosecondsPerIteration = Get-Percentile $blackboardSamples 50
    p50StepsPerSecond = 1000000000.0 / (Get-Percentile $blackboardSamples 50)
    p50CommandsPerSecond = 0.0
    p50SchedulingNanoseconds = 0.0
    p50CompletionNanoseconds = 0.0
    nativeProgramBytes = [long]$windowsBaselineRaw.generatedDispatchProgramPayloadBytes
    nativeBytesPerInstance = [long]$windowsBaselineRaw.generatedDispatchInstancePayloadBytes
    measuredHeapDeltaBytes = [long]$playerEvidence.measuredHeapDeltaBytes
    gen0CollectionDelta = [int]$playerEvidence.gen0CollectionDelta
    allocationMetricLimitation = $playerEvidence.allocationMetricLimitation
}

$postRunSourceAnalyzerHash = (Get-FileHash -LiteralPath $analyzerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$postRunSourceRuntimeHash = Get-FileSetFingerprint (Join-Path $aibtRoot 'Runtime')
$postRunSourceHarnessHash = Get-FileSetFingerprint $unityHarnessSource
if ($postRunSourceAnalyzerHash -ne $sourceAnalyzerHash -or
    $postRunSourceRuntimeHash -ne $sourceRuntimeHash -or
    $postRunSourceHarnessHash -ne $sourceHarnessHash -or
    (Get-FileHash -LiteralPath $windowsBaselineSource -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sourceWindowsBaselineHash) {
    throw 'The frozen analyzer, production Runtime, or Player harness source drifted while the pipeline was running.'
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$visualStudioPath = if (Test-Path -LiteralPath $vswhere) {
    & $vswhere -latest -products * -version '[17.0,18.0)' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
}
else { $null }
$msvcCompiler = if ($visualStudioPath) {
    Get-ChildItem -LiteralPath (Join-Path $visualStudioPath 'VC\Tools\MSVC') -Filter cl.exe -Recurse -File |
        Where-Object FullName -match '\\bin\\Hostx64\\x64\\cl\.exe$' |
        Sort-Object FullName -Descending | Select-Object -First 1
}
else { $null }
$windowsSdk = Get-ItemProperty 'HKLM:\SOFTWARE\Wow6432Node\Microsoft\Microsoft SDKs\Windows\v10.0' -ErrorAction SilentlyContinue
if (!$msvcCompiler -or !$windowsSdk) { throw 'The completed Windows build has no detectable MSVC/SDK environment authority.' }
$environmentSnapshot = [ordered]@{
    operatingSystem = $playerEvidence.operatingSystem
    processorType = $playerEvidence.processorType
    processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    logicalProcessorCount = [int]$playerEvidence.logicalProcessorCount
    jobWorkerCount = [int]$playerEvidence.jobWorkerCount
    unityVersion = $unityVersion
    burstVersion = $resolvedBurstVersion
    collectionsVersion = $resolvedCollectionsVersion
    msvcCompiler = $msvcCompiler.FullName
    msvcFileVersion = $msvcCompiler.VersionInfo.FileVersion
    windowsSdkRoot = $windowsSdk.InstallationFolder
    windowsSdkVersion = $windowsSdk.ProductVersion
    developmentBuild = $false
    scriptingBackend = 'IL2CPP'
    architecture = 'x86_64'
}
$environmentJson = $environmentSnapshot | ConvertTo-Json -Compress

$finalEvidence = [ordered]@{
    schema = 'aibt-p2-012-player-aot-acceptance-v1'
    passed = $true
    observedAtUtc = [DateTime]::UtcNow.ToString('o')
    unityEditor = $unityExecutable
    unityVersion = $unityVersion
    isolatedProject = $isolatedRoot
    packageRoot = $aibtRoot
    environment = [ordered]@{
        snapshot = $environmentSnapshot
        sha256 = Get-TextSha256 $environmentJson
    }
    generator = [ordered]@{
        analyzerGuid = '31a3f09584684895a0a72916d3ad4de0'
        sourceAnalyzerSha256 = $sourceAnalyzerHash
        isolatedAnalyzerSha256 = $isolatedAnalyzerHash
        sourceRuntimeFileSetSha256 = $sourceRuntimeHash
        isolatedRuntimeFileSetSha256 = $isolatedRuntimeHash
        sourceHarnessFileSetSha256 = $sourceHarnessHash
        isolatedHarnessFileSetSha256 = $isolatedBaseHarnessHash
        windowsBaselineProbeSha256 = $sourceWindowsBaselineHash
        nodeDeclarationSha256 = (Get-FileHash -LiteralPath (Join-Path $unityHarnessSource 'Runtime\Nodes\GeneratedDispatchCanaryNodeDeclarations.cs') -Algorithm SHA256).Hash.ToLowerInvariant()
        catalogDeclarationSha256 = (Get-FileHash -LiteralPath (Join-Path $unityHarnessSource 'Runtime\GeneratedDispatchCanaryDeclarations.cs') -Algorithm SHA256).Hash.ToLowerInvariant()
        nodeAsmdefSha256 = (Get-FileHash -LiteralPath $nodeAsmdefPath -Algorithm SHA256).Hash.ToLowerInvariant()
        catalogAsmdefSha256 = (Get-FileHash -LiteralPath $playerAsmdefPath -Algorithm SHA256).Hash.ToLowerInvariant()
        probeSha256 = (Get-FileHash -LiteralPath (Join-Path $unityHarnessSource 'Runtime\GeneratedDispatchPlayerAotProbe.cs') -Algorithm SHA256).Hash.ToLowerInvariant()
        buildDriverSha256 = (Get-FileHash -LiteralPath (Join-Path $unityHarnessSource 'Editor\GeneratedDispatchPlayerAotBuild.cs') -Algorithm SHA256).Hash.ToLowerInvariant()
        generatedCatalog = 'AIBT.Tests.Runtime.NativeExecution.Dispatch.GeneratedDispatchCanaryCatalog'
        generatedEntryPoint = 'ExecuteImmediate(ref BurstExecutionBatch)'
        burstCaller = 'AIBT.Tests.Runtime.NativeExecution.Dispatch.GeneratedDispatchPlayerAotJob.Execute'
    }
    build = $buildEvidence
    runtime = $playerEvidence
    windowsBaseline = [ordered]@{
        schema = 'aibt-p2-022-windows-baseline-v1'
        warmupCount = [int]$windowsBaselineRaw.warmupCount
        rawEvidence = $windowsBaselineEvidencePath
        rawEvidenceSha256 = (Get-FileHash -LiteralPath $windowsBaselineEvidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
        scenarios = $scenarioSummaries
        behaviorMatrix = @(
            @($windowsBaselineRaw.behaviorCases)
            @($playerEvidence.behaviorMatrixCases)
        ) | Sort-Object -Unique
        behaviorMatrixApplicability = 'Player-native representatives cover empty composite success/failure, Immediate/Budgeted parity, command publication, Burst scheduling, and a scheduled generated user node. Exact full P1 oracle equivalence remains the P2-020 15-case/policy gate and is not duplicated into the release Player.'
        interpretation = 'Raw measurements from this workstation only; no policy threshold or universal default is inferred.'
        nativeByteMetric = 'Payload bytes of every retained fixed NativeArray arena, including the caller shape, workspace-owned shape copy, workspace scratch/control, and borrowed per-request storage; allocator metadata is excluded.'
    }
    packages = [ordered]@{
        burst = $resolvedBurstVersion
        collections = $resolvedCollectionsVersion
        newtonsoftJson = $resolvedNewtonsoftVersion
        lockFile = $packagesLockPath
        lockFileSha256 = (Get-FileHash -LiteralPath $packagesLockPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    proofs = [ordered]@{
        sourceGeneratorExecuted = $true
        sourceGeneratorProof = 'Generated IsUsable/handshake/ExecuteImmediate members compiled, built, and executed from the analyzer-bound Player assembly.'
        nonDevelopmentIl2CppX64 = $true
        burstAotLibraryPresent = $true
        burstDiscardManagedPathSentinelZero = $true
        generatedMemoryAndStatusAssertionsPassed = $true
        zeroAssetIdSentinelPathPassed = $true
        positiveBurstIlppLogMarkersPresent = $true
    }
    artifacts = [ordered]@{
        playerExecutable = $playerExecutable
        playerExecutableSha256 = (Get-FileHash -LiteralPath $playerExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
        gameAssembly = $gameAssembly
        gameAssemblySha256 = (Get-FileHash -LiteralPath $gameAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
        globalMetadata = $globalMetadata
        globalMetadataSha256 = (Get-FileHash -LiteralPath $globalMetadata -Algorithm SHA256).Hash.ToLowerInvariant()
        burstLibrary = $burstLibraries[0].FullName
        burstLibraryBytes = $burstLibraries[0].Length
        burstLibrarySha256 = (Get-FileHash -LiteralPath $burstLibraries[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    logs = [ordered]@{
        build = $buildLog
        player = $playerLog
        buildRawEvidence = $buildEvidencePath
        playerRawEvidence = $playerEvidencePath
        windowsBaselineRawEvidence = $windowsBaselineEvidencePath
        buildSha256 = (Get-FileHash -LiteralPath $buildLog -Algorithm SHA256).Hash.ToLowerInvariant()
        playerSha256 = (Get-FileHash -LiteralPath $playerLog -Algorithm SHA256).Hash.ToLowerInvariant()
        buildRawEvidenceSha256 = (Get-FileHash -LiteralPath $buildEvidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
        playerRawEvidenceSha256 = (Get-FileHash -LiteralPath $playerEvidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
        windowsBaselineRawEvidenceSha256 = (Get-FileHash -LiteralPath $windowsBaselineEvidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
        buildScan = 'clean: CS/BC, Burst/AOT, ILPP, managed fallback, missing script, native leak, failure marker'
        playerScan = 'clean: CS/BC, Burst/AOT, ILPP, managed fallback, missing script, native leak, failure marker'
    }
    processes = [ordered]@{
        buildExitCode = $buildExitCode
        playerExitCode = $playerExitCode
        buildTimeoutSeconds = $BuildTimeoutSeconds
        playerTimeoutSeconds = $PlayerTimeoutSeconds
    }
    cleanup = [ordered]@{
        mode = 'preserved-for-inspection'
        isolatedProject = $isolatedRoot
        buildArtifacts = $buildRoot
        recursiveDeletePerformed = $false
    }
}
[IO.File]::WriteAllText(
    $finalEvidencePath,
    ($finalEvidence | ConvertTo-Json -Depth 12),
    [Text.UTF8Encoding]::new($false))

$windowsEvidenceVerifier = Join-Path $aibtRoot 'Tools~\Verification\P2\Windows\Verify-WindowsBaselineEvidence.ps1'
& $windowsEvidenceVerifier -EvidencePath $windowsBaselineEvidencePath
& $windowsEvidenceVerifier -EvidencePath $finalEvidencePath

Write-Output "AIBT P2-012 Windows Player/AOT acceptance passed. Evidence: $finalEvidencePath"
