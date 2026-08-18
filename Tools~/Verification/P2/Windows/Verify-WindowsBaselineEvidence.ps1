[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $EvidencePath,
    [string] $UvxPath
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
$document = [IO.Path]::GetFullPath($EvidencePath)
if (-not (Test-Path -LiteralPath $document -PathType Leaf)) {
    throw "Evidence document was not found: $document"
}

function Get-TextSha256 {
    param([string] $Text)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString(
            $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text))))
            .Replace('-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
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
                $relative + [char]9 + $hash
            }
    )
    return Get-TextSha256 ($lines -join [char]10)
}

function Assert-FileDigest {
    param([string] $Path, [string] $Expected, [string] $Description)
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "$Description is missing: $resolved"
    }
    $actual = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Expected) {
        throw "$Description digest mismatch. Expected=$Expected Actual=$actual"
    }
}

function Get-Percentile {
    param([double[]] $Values, [double] $Percentile)
    if (-not $Values -or $Values.Count -eq 0) { throw 'Percentile input is empty.' }
    $ordered = @($Values | Sort-Object)
    $rank = [Math]::Ceiling(($Percentile / 100.0) * $ordered.Count) - 1
    $index = [Math]::Max(0, [Math]::Min($ordered.Count - 1, [int]$rank))
    return [double]$ordered[$index]
}

function Assert-NearlyEqual {
    param([double] $Actual, [double] $Expected, [string] $Description)
    $tolerance = [Math]::Max(0.000001, [Math]::Abs($Expected) * 0.0000000001)
    if ([Math]::Abs($Actual - $Expected) -gt $tolerance) {
        throw "$Description mismatch. Expected=$Expected Actual=$Actual"
    }
}

function Assert-ArrayEqual {
    param([object[]] $Actual, [object[]] $Expected, [string] $Description)
    if ($Actual.Count -ne $Expected.Count) {
        throw "$Description length mismatch. Expected=$($Expected.Count) Actual=$($Actual.Count)"
    }
    for ($index = 0; $index -lt $Actual.Count; $index++) {
        if ([string]$Actual[$index] -ne [string]$Expected[$index]) {
            throw "$Description differs at index $index. Expected=$($Expected[$index]) Actual=$($Actual[$index])"
        }
    }
}

$parsed = Get-Content -Raw -LiteralPath $document | ConvertFrom-Json
$schemaName = switch ($parsed.schema) {
    'aibt-p2-022-windows-baseline-raw-v1' { 'windows-baseline-raw.schema.json' }
    'aibt-p2-012-player-aot-acceptance-v1' { 'windows-baseline-acceptance.schema.json' }
    default { throw "Unsupported Windows evidence schema: $($parsed.schema)" }
}
$schema = Join-Path $root "Benchmarks~\Phase2\Windows\Schemas\$schemaName"

if ([string]::IsNullOrWhiteSpace($UvxPath)) {
    $uvx = Get-Command uvx -ErrorAction SilentlyContinue
    if ($null -eq $uvx) { throw 'uvx is required for JSON Schema validation.' }
    $UvxPath = $uvx.Source
}
& $UvxPath --from check-jsonschema==0.38.0 check-jsonschema --check-metaschema $schema
if ($LASTEXITCODE -ne 0) { throw "Invalid JSON Schema: $schema" }
& $UvxPath --from check-jsonschema==0.38.0 check-jsonschema --schemafile $schema $document
if ($LASTEXITCODE -ne 0) { throw "Evidence does not conform to $schemaName." }

if ($parsed.schema -eq 'aibt-p2-012-player-aot-acceptance-v1') {
    $names = @($parsed.windowsBaseline.scenarios | ForEach-Object name)
    $expectedNames = @(
        'scheduling-overhead',
        'cheap-tree',
        'blackboard-heavy-generated-dispatch',
        'command-heavy',
        'mixed-population'
    )
    if (@($names | Sort-Object -Unique).Count -ne 5 -or
        @($expectedNames | Where-Object { $names -notcontains $_ }).Count -ne 0) {
        throw 'Windows acceptance evidence does not contain the exact five required scenarios.'
    }

    $rawPath = [IO.Path]::GetFullPath([string]$parsed.windowsBaseline.rawEvidence)
    Assert-FileDigest $rawPath $parsed.windowsBaseline.rawEvidenceSha256 'Windows raw evidence'
    if ($rawPath -ne [IO.Path]::GetFullPath([string]$parsed.logs.windowsBaselineRawEvidence) -or
        $parsed.windowsBaseline.rawEvidenceSha256 -ne $parsed.logs.windowsBaselineRawEvidenceSha256) {
        throw 'Windows raw evidence path/digest authorities disagree.'
    }
    $rawSchema = Join-Path $root 'Benchmarks~\Phase2\Windows\Schemas\windows-baseline-raw.schema.json'
    & $UvxPath --from check-jsonschema==0.38.0 check-jsonschema --schemafile $rawSchema $rawPath
    if ($LASTEXITCODE -ne 0) { throw 'Nested Windows raw evidence does not conform to its schema.' }
    $raw = Get-Content -Raw -LiteralPath $rawPath | ConvertFrom-Json

    $environmentHash = Get-TextSha256 ($parsed.environment.snapshot | ConvertTo-Json -Compress)
    if ($environmentHash -ne $parsed.environment.sha256) {
        throw "Environment snapshot digest mismatch. Expected=$($parsed.environment.sha256) Actual=$environmentHash"
    }

    if ($parsed.generator.sourceAnalyzerSha256 -ne $parsed.generator.isolatedAnalyzerSha256 -or
        $parsed.generator.sourceRuntimeFileSetSha256 -ne $parsed.generator.isolatedRuntimeFileSetSha256 -or
        $parsed.generator.sourceHarnessFileSetSha256 -ne $parsed.generator.isolatedHarnessFileSetSha256) {
        throw 'Source and isolated analyzer/runtime/harness fingerprints disagree.'
    }
    Assert-FileDigest (Join-Path $root 'Analyzers\AIBT.CodeGen.dll') $parsed.generator.sourceAnalyzerSha256 'Checked source generator'
    $runtimeHash = Get-FileSetFingerprint (Join-Path $root 'Runtime')
    if ($runtimeHash -ne $parsed.generator.sourceRuntimeFileSetSha256) {
        throw 'Current Runtime file-set fingerprint differs from the accepted snapshot.'
    }
    $harnessHash = Get-FileSetFingerprint (Join-Path $root 'Benchmarks~\Phase2\Dispatch\Player\Unity')
    if ($harnessHash -ne $parsed.generator.sourceHarnessFileSetSha256) {
        throw 'Current Player harness fingerprint differs from the accepted snapshot.'
    }
    Assert-FileDigest (Join-Path $root 'Benchmarks~\Phase2\Windows\Unity\Runtime\GeneratedDispatchWindowsBaselineProbe.cs') $parsed.generator.windowsBaselineProbeSha256 'Windows baseline probe'

    Assert-FileDigest $parsed.packages.lockFile $parsed.packages.lockFileSha256 'Resolved package lock'
    Assert-FileDigest $parsed.artifacts.playerExecutable $parsed.artifacts.playerExecutableSha256 'Player executable'
    Assert-FileDigest $parsed.artifacts.gameAssembly $parsed.artifacts.gameAssemblySha256 'GameAssembly'
    Assert-FileDigest $parsed.artifacts.globalMetadata $parsed.artifacts.globalMetadataSha256 'IL2CPP global metadata'
    Assert-FileDigest $parsed.artifacts.burstLibrary $parsed.artifacts.burstLibrarySha256 'Burst AOT library'
    Assert-FileDigest $parsed.logs.build $parsed.logs.buildSha256 'Unity build log'
    Assert-FileDigest $parsed.logs.player $parsed.logs.playerSha256 'Player log'
    Assert-FileDigest $parsed.logs.buildRawEvidence $parsed.logs.buildRawEvidenceSha256 'Build raw evidence'
    Assert-FileDigest $parsed.logs.playerRawEvidence $parsed.logs.playerRawEvidenceSha256 'Player raw evidence'

    $summaries = @{}
    foreach ($summary in @($parsed.windowsBaseline.scenarios)) {
        $summaries[[string]$summary.name] = $summary
    }
    foreach ($scenario in @($raw.samples)) {
        $name = [string]$scenario.name
        $summary = $summaries[$name]
        if ($null -eq $summary) { throw "Missing acceptance summary for raw scenario '$name'." }
        if ([int]$summary.iterationsPerSample -ne [int]$scenario.iterationsPerSample -or
            [long]$summary.nativeProgramBytes -ne [long]$scenario.nativeProgramBytes -or
            [long]$summary.nativeBytesPerInstance -ne [long]$scenario.nativeBytesPerInstance -or
            [long]$summary.measuredHeapDeltaBytes -ne [long]$scenario.measuredHeapDeltaBytes -or
            [int]$summary.gen0CollectionDelta -ne [int]$scenario.gen0CollectionDelta) {
            throw "Copied summary fields differ from raw scenario '$name'."
        }
        Assert-ArrayEqual @($summary.rawElapsedTicks) @($scenario.rawElapsedTicks) "$name raw elapsed ticks"
        $ticks = @($scenario.rawElapsedTicks | ForEach-Object { [double]$_ })
        $frameNs = @($ticks | ForEach-Object { $_ * 1000000000.0 / [double]$raw.stopwatchFrequency })
        $perIteration = @($frameNs | ForEach-Object { $_ / [double]$scenario.iterationsPerSample })
        $stepsPerSecond = @($ticks | ForEach-Object {
            if ([long]$scenario.stepsPerSample -eq 0) { 0.0 }
            else { [double]$scenario.stepsPerSample * [double]$raw.stopwatchFrequency / $_ }
        })
        $commandsPerSecond = @($ticks | ForEach-Object {
            if ([long]$scenario.commandsPerSample -eq 0) { 0.0 }
            else { [double]$scenario.commandsPerSample * [double]$raw.stopwatchFrequency / $_ }
        })
        Assert-NearlyEqual $summary.p50FrameContributionNanoseconds (Get-Percentile $frameNs 50) "$name p50 frame"
        Assert-NearlyEqual $summary.p95FrameContributionNanoseconds (Get-Percentile $frameNs 95) "$name p95 frame"
        Assert-NearlyEqual $summary.p99FrameContributionNanoseconds (Get-Percentile $frameNs 99) "$name p99 frame"
        Assert-NearlyEqual $summary.p50NanosecondsPerIteration (Get-Percentile $perIteration 50) "$name p50 iteration"
        Assert-NearlyEqual $summary.p50StepsPerSecond (Get-Percentile $stepsPerSecond 50) "$name p50 steps/s"
        Assert-NearlyEqual $summary.p50CommandsPerSecond (Get-Percentile $commandsPerSecond 50) "$name p50 commands/s"

        if ($name -eq 'scheduling-overhead') {
            $scheduleNs = @($scenario.rawSchedulingTicks | ForEach-Object {
                [double]$_ * 1000000000.0 / [double]$raw.stopwatchFrequency / [double]$scenario.iterationsPerSample
            })
            $completionNs = @($scenario.rawCompletionTicks | ForEach-Object {
                [double]$_ * 1000000000.0 / [double]$raw.stopwatchFrequency / [double]$scenario.iterationsPerSample
            })
            Assert-NearlyEqual $summary.p50SchedulingNanoseconds (Get-Percentile $scheduleNs 50) 'scheduling p50 schedule'
            Assert-NearlyEqual $summary.p50CompletionNanoseconds (Get-Percentile $completionNs 50) 'scheduling p50 completion'
        }
        elseif ([double]$summary.p50SchedulingNanoseconds -ne 0.0 -or
            [double]$summary.p50CompletionNanoseconds -ne 0.0) {
            throw "Non-scheduling scenario '$name' reports scheduling/completion time."
        }
    }

    $blackboard = $summaries['blackboard-heavy-generated-dispatch']
    $blackboardSamples = @($parsed.runtime.rawNanosecondsPerDispatch | ForEach-Object { [double]$_ })
    Assert-ArrayEqual @($blackboard.rawNanosecondsPerIteration) @($parsed.runtime.rawNanosecondsPerDispatch) 'blackboard raw nanoseconds'
    if ([int]$blackboard.iterationsPerSample -ne [int]$parsed.runtime.measurementIterationsPerSample -or
        [long]$blackboard.nativeProgramBytes -ne [long]$raw.generatedDispatchProgramPayloadBytes -or
        [long]$blackboard.nativeBytesPerInstance -ne [long]$raw.generatedDispatchInstancePayloadBytes) {
        throw 'Blackboard summary does not match runtime/raw payload authorities.'
    }
    $blackboardFrames = @($blackboardSamples | ForEach-Object {
        $_ * [double]$parsed.runtime.measurementIterationsPerSample
    })
    $blackboardP50 = Get-Percentile $blackboardSamples 50
    Assert-NearlyEqual $blackboard.p50FrameContributionNanoseconds (Get-Percentile $blackboardFrames 50) 'blackboard p50 frame'
    Assert-NearlyEqual $blackboard.p95FrameContributionNanoseconds (Get-Percentile $blackboardFrames 95) 'blackboard p95 frame'
    Assert-NearlyEqual $blackboard.p99FrameContributionNanoseconds (Get-Percentile $blackboardFrames 99) 'blackboard p99 frame'
    Assert-NearlyEqual $blackboard.p50NanosecondsPerIteration $blackboardP50 'blackboard p50 iteration'
    Assert-NearlyEqual $blackboard.p50StepsPerSecond (1000000000.0 / $blackboardP50) 'blackboard p50 steps/s'

    $expectedBehavior = @(
        @($raw.behaviorCases)
        @($parsed.runtime.behaviorMatrixCases)
    ) | Sort-Object -Unique
    Assert-ArrayEqual @($parsed.windowsBaseline.behaviorMatrix) @($expectedBehavior) 'behavior matrix'
}

Write-Output "Windows baseline evidence validation passed: $document"
