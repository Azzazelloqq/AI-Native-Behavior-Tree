[CmdletBinding()]
param(
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe',
    [switch]$KeepArtifacts
)

# P6-022 disposable spike runner. A scoped-down copy of
# Tools~/Verification/P2/CodeGen/Build-And-Verify.ps1's own "SampleUnityProject" stage
# (~lines 200-262): only what is needed to compile the real Roslyn-generated
# PublicBurstNodeCatalog dispatch body and run GenericNativeDispatchSpikeTests against it. Not part
# of any required verification gate; never wired into CI. See ADR-P6-022 and
# Planning~/Evidence/P6-022/.

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$spikeRoot = $PSScriptRoot
$packageRoot = (Resolve-Path -LiteralPath (Join-Path $spikeRoot '..\..')).Path
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('AIBT-P6-022-' + [Guid]::NewGuid().ToString('N'))

Require (Test-Path -LiteralPath $UnityEditor) "Unity Editor not found: $UnityEditor"

New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
    $sampleProject = Join-Path $tempRoot 'SampleUnityProject'
    $samplePackages = Join-Path $sampleProject 'Packages'
    New-Item -ItemType Directory -Path (Join-Path $sampleProject 'Assets'), $samplePackages, (Join-Path $sampleProject 'ProjectSettings') | Out-Null
    $packageUri = 'file:' + ($packageRoot -replace '\\', '/')
    $sampleManifest = @{
        dependencies = [ordered]@{
            'com.azzazello.aibt'        = $packageUri
            'com.unity.test-framework'  = '1.7.0'
        }
    } | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText((Join-Path $samplePackages 'manifest.json'), $sampleManifest, [Text.UTF8Encoding]::new($false))

    # The spike defines its own single-node shard/catalog (Harness/Node, Harness/Catalog) rather
    # than importing the real, two-node Samples~/BurstNodes sample -- see the Result note in
    # Planning~/Evidence/P6-022/README.md for why isolating the real sample's ThresholdCondition
    # node (real dispatch index 1) is not achievable within this card's decided single-case scope.
    $harnessSource = Join-Path $spikeRoot 'Harness'
    $harnessTarget = Join-Path $sampleProject 'Assets\GenericNativeDispatchSpikeHarness'
    Require (Test-Path -LiteralPath $harnessSource) 'Spike harness source is missing.'
    Copy-Item -LiteralPath $harnessSource -Destination $harnessTarget -Recurse

    $testXml = Join-Path $tempRoot 'unity-spike-editmode.xml'
    $unityLog = Join-Path $tempRoot 'unity-spike.log'
    $arguments = @(
        '-batchmode', '-nographics', '-projectPath', $sampleProject,
        '-runTests', '-testPlatform', 'EditMode', '-assemblyNames', 'AIBT.Samples.BurstNodes.Tests',
        '-testResults', $testXml, '-logFile', $unityLog
    )
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    Require (Test-Path -LiteralPath $testXml) 'Unity spike test XML was not produced.'
    [xml]$results = Get-Content -Raw -LiteralPath $testXml
    $run = $results.'test-run'
    Write-Host "Unity process exit code: $($process.ExitCode)"
    Write-Host "Spike tests: $($run.passed)/$($run.total) passed, $($run.failed) failed, $($run.skipped) skipped"
    if ([int]$run.failed -gt 0) {
        $failures = $results.SelectNodes('//test-case[@result="Failed"]')
        foreach ($failure in $failures) {
            Write-Host "FAILED: $($failure.fullname)"
            Write-Host $failure.failure.message.InnerText
            Write-Host $failure.failure.'stack-trace'.InnerText
        }
    }
    $failurePattern = 'CS8032|AD0001|will not be loaded|Could not load file or assembly|Analyzer.+failed|Generator.+failed|error CS[0-9]+'
    $log = Get-Content -Raw -LiteralPath $unityLog
    if ($log -match $failurePattern) {
        Write-Host 'Unity log contains analyzer/generator/compiler failure markers:'
        Write-Host ($log -split "`n" | Select-String -Pattern $failurePattern | Select-Object -First 20)
    }

    Require ([int]$run.total -gt 0 -and [int]$run.passed -eq [int]$run.total -and [int]$run.failed -eq 0) 'Spike tests did not all pass.'
    Write-Host 'PASS: generic translator drove real ExecuteImmediate and matched the golden test result.'
    Write-Host "Artifacts: $tempRoot"
}
finally {
    if (-not $KeepArtifacts -and (Test-Path -LiteralPath $tempRoot)) {
        $resolvedTemp = [IO.Path]::GetFullPath($tempRoot)
        $tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        Require ($resolvedTemp.StartsWith($tempParent, [StringComparison]::OrdinalIgnoreCase)) 'Refusing to remove a path outside the system temp directory.'
        try {
            Remove-Item -LiteralPath $resolvedTemp -Recurse -Force -ErrorAction Stop
        }
        catch {
            $emptyDir = Join-Path ([IO.Path]::GetTempPath()) ('AIBT-P6-022-empty-' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $emptyDir | Out-Null
            & robocopy $emptyDir $resolvedTemp /MIR /NFL /NDL /NJH /NJS | Out-Null
            Remove-Item -LiteralPath $emptyDir -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $resolvedTemp -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
