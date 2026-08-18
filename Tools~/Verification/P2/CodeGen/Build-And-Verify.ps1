[CmdletBinding()]
param(
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe',
    [switch]$KeepArtifacts
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$packageRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..\..')).Path
$unityDotnet = Join-Path (Split-Path -Parent $UnityEditor) 'Data\DotNetSdk\dotnet.exe'
$project = Join-Path $packageRoot 'CodeGen~\AIBT.CodeGen\AIBT.CodeGen.csproj'
$verifier = Join-Path $PSScriptRoot 'Verifier\AIBT.CodeGen.Tests.csproj'
$checkedAnalyzer = Join-Path $packageRoot 'Analyzers\AIBT.CodeGen.dll'
$analyzerHashFile = Join-Path $packageRoot 'Analyzers\AIBT.CodeGen.sha256'
$analyzerMeta = $checkedAnalyzer + '.meta'
$abiFixture = Join-Path $packageRoot 'Tests\Editor\CodeGen\Contracts\ExpectedPublicAbiV2.txt'
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('AIBT-P2-012-' + [Guid]::NewGuid().ToString('N'))

Require (Test-Path -LiteralPath $UnityEditor) "Unity Editor not found: $UnityEditor"
Require (Test-Path -LiteralPath $unityDotnet) "Unity .NET SDK not found: $unityDotnet"
Require (Test-Path -LiteralPath $checkedAnalyzer) 'Checked-in analyzer DLL is missing.'
Require (Test-Path -LiteralPath $analyzerHashFile) 'Checked-in analyzer hash is missing.'

New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
    # The csproj's <PathMap> only normalizes $(MSBuildProjectDirectory) itself;
    # it does not make the compiler's deterministic hash insensitive to the
    # *relative* BaseOutputPath/BaseIntermediateOutputPath structure beneath
    # it (generated files such as the GeneratedMSBuildEditorConfig carry that
    # relative suffix into the hash). Building to a system-Temp path outside
    # the project directory — or to two differently-named subfolders — was
    # observed to produce a DLL that differs from a same-recipe checked-in
    # build in exactly the PE TimeDateStamp and MVID bytes, even though the
    # source and IL are identical. Building twice, sequentially, into the
    # exact same in-project relative subdirectory keeps every build under
    # this project's PathMap-covered prefix with an identical relative
    # suffix, which is what makes the result match the checked-in artifact.
    $buildDir = Join-Path $packageRoot 'CodeGen~\AIBT.CodeGen\bin\.p2-codegen-gate'
    $objDir = Join-Path $packageRoot 'CodeGen~\AIBT.CodeGen\obj\.p2-codegen-gate'
    $dllPath = Join-Path $buildDir 'Release\netstandard2.0\AIBT.CodeGen.dll'

    if (Test-Path -LiteralPath $buildDir) { Remove-Item -LiteralPath $buildDir -Recurse -Force }
    if (Test-Path -LiteralPath $objDir) { Remove-Item -LiteralPath $objDir -Recurse -Force }
    & $unityDotnet build $project -c Release --nologo -t:Rebuild "-p:BaseOutputPath=$buildDir\" "-p:BaseIntermediateOutputPath=$objDir\"
    Require ($LASTEXITCODE -eq 0) 'Analyzer build A failed.'
    $bytesA = [IO.File]::ReadAllBytes($dllPath)

    Remove-Item -LiteralPath $buildDir -Recurse -Force
    Remove-Item -LiteralPath $objDir -Recurse -Force
    & $unityDotnet build $project -c Release --nologo -t:Rebuild "-p:BaseOutputPath=$buildDir\" "-p:BaseIntermediateOutputPath=$objDir\"
    Require ($LASTEXITCODE -eq 0) 'Analyzer build B failed.'
    $bytesB = [IO.File]::ReadAllBytes($dllPath)

    $dllA = Join-Path $tempRoot 'build-a-AIBT.CodeGen.dll'
    [IO.File]::WriteAllBytes($dllA, $bytesA)
    Require ([Convert]::ToBase64String($bytesA) -ceq [Convert]::ToBase64String($bytesB)) 'Independent analyzer builds are not byte-identical.'
    Require ([Convert]::ToBase64String($bytesA) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($checkedAnalyzer))) 'Checked-in analyzer differs from reproducible build.'

    $actualHash = (Get-FileHash -LiteralPath $dllA -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedHash = (Get-Content -Raw -LiteralPath $analyzerHashFile).Trim().ToLowerInvariant()
    Require ($actualHash -eq $expectedHash) "Analyzer hash mismatch: expected $expectedHash, actual $actualHash."
    Require ((Get-FileHash -LiteralPath $abiFixture -Algorithm SHA256).Hash -eq 'A6E3844F1EFE96E71B4E392321570F0EDD0C1143F0A5A5726F3FE988FA6635B4') 'Accepted 352-record ABI v2 fixture hash differs.'

    $meta = Get-Content -Raw -LiteralPath $analyzerMeta
    Require ($meta -match '(?m)^guid: 31a3f09584684895a0a72916d3ad4de0$') 'Analyzer meta GUID differs.'
    Require ($meta -match '(?m)^- RoslynAnalyzer$') 'Analyzer meta lacks RoslynAnalyzer label.'
    Require ($meta -match '(?ms)^    Any:\s+enabled: 0\s') 'Analyzer ordinary Any platform must be disabled.'
    Require ($meta -match '(?ms)^    Editor:\s+enabled: 0\s') 'Analyzer ordinary Editor platform must be disabled.'

    & $unityDotnet run --project $verifier -c Release -- $packageRoot
    Require ($LASTEXITCODE -eq 0) 'Roslyn analyzer/generator matrix failed.'

    $unityProject = Join-Path $tempRoot 'UnityProject'
    $packages = Join-Path $unityProject 'Packages'
    New-Item -ItemType Directory -Path (Join-Path $unityProject 'Assets'), $packages, (Join-Path $unityProject 'ProjectSettings') | Out-Null
    $packageUri = 'file:' + ($packageRoot -replace '\\', '/')
    $manifest = @{
        dependencies = [ordered]@{
            'com.azzazello.aibt' = $packageUri
            'com.unity.test-framework' = '1.7.0'
        }
        testables = @('com.azzazello.aibt')
    } | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText((Join-Path $packages 'manifest.json'), $manifest, [Text.UTF8Encoding]::new($false))

    # Prove the packaged analyzer in the same clean Unity project without
    # importing the disposable P2-001 feasibility contracts. AIBT.Runtime is
    # the sole public ABI authority for this production packaging gate.
    $probeRoot = Join-Path $unityProject 'Assets\AnalyzerProbe'
    $probeSource = Join-Path $probeRoot 'InvalidProbe.cs'
    $probeAsmdef = Join-Path $probeRoot 'AIBT.CodeGen.InvalidAnalyzerProbe.asmdef'
    $probeLog = Join-Path $tempRoot 'unity-invalid-analyzer-probe.log'
    New-Item -ItemType Directory -Path $probeRoot | Out-Null
    $invalidProbeSource = @'
using AIBT;
using AIBT.Burst;

namespace AIBT.CodeGen.InvalidAnalyzerProbe
{
    public partial struct ProbeConfiguration
    {
        [AibtConfigField("enabled", "Bool", 1u)]
        public bool Enabled;
    }

    public partial struct ProbeMemory
    {
        [AibtMemoryField("count", "UInt32", 1u)]
        public uint Count;
    }

    [AibtCatalogShard("aibt.codegen.invalid-probe-shard", 1u)]
    public partial struct ProbeShard { }

    [AibtBurstNode(
        "aibt.codegen.invalid-probe",
        1u,
        BurstNodeKind.Condition,
        typeof(ProbeConfiguration),
        typeof(ProbeMemory),
        NodeMemoryLifetime.Activation,
        true,
        BurstCancellationMode.NotApplicable,
        BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    [AibtNodeDocumentation("Invalid analyzer probe", "Tests", "Probe", "Never use in a tree", "invalid-probe")]
    public struct InvalidProbeNode
    {
        public static void Enter(in ProbeConfiguration config, ref ProbeMemory memory, ref BurstEnterContext context) { }
        public static NodeStatus Tick(in ProbeConfiguration config, ref ProbeMemory memory, ref BurstTickContext context) => NodeStatus.Success;
        public static void Abort(in ProbeConfiguration config, ref ProbeMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
        public static void Exit(in ProbeConfiguration config, ref ProbeMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }
}
'@
    $invalidProbeAsmdef = @'
{
  "name": "AIBT.CodeGen.InvalidAnalyzerProbe",
  "rootNamespace": "AIBT.CodeGen.InvalidAnalyzerProbe",
  "references": ["AIBT.Runtime"],
  "includePlatforms": ["Editor"],
  "autoReferenced": false,
  "analyzers": ["GUID:31a3f09584684895a0a72916d3ad4de0"]
}
'@
    [IO.File]::WriteAllText($probeSource, $invalidProbeSource, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($probeAsmdef, $invalidProbeAsmdef, [Text.UTF8Encoding]::new($false))
    $probeArguments = @(
        '-batchmode', '-nographics', '-projectPath', $unityProject,
        '-quit', '-logFile', $probeLog
    )
    $probeProcess = Start-Process -FilePath $UnityEditor -ArgumentList $probeArguments -Wait -PassThru -WindowStyle Hidden
    Require (Test-Path -LiteralPath $probeLog) 'Unity invalid analyzer probe produced no log.'
    $probeLogText = Get-Content -Raw -LiteralPath $probeLog
    Require ($probeLogText -match 'error AIBT5001') 'Unity invalid analyzer probe did not emit AIBT5001.'
    $probeFailurePattern = 'CS0433|error CS[0-9]+|error AIBT(?!5001)[0-9]+|CS8032|AD0001|will not be loaded|Could not load file or assembly|Analyzer.+failed|Generator.+failed'
    Require ($probeLogText -notmatch $probeFailurePattern) 'Unity invalid analyzer probe contains an unexpected compiler/analyzer failure.'

    Remove-Item -LiteralPath $probeRoot -Recurse -Force

    $testXml = Join-Path $tempRoot 'unity-editmode.xml'
    $unityLog = Join-Path $tempRoot 'unity.log'
    $arguments = @(
        '-batchmode', '-nographics', '-projectPath', $unityProject,
        '-runTests', '-testPlatform', 'EditMode', '-assemblyNames', 'AIBT.CodeGen.ContractTests;AIBT.NativeBurstDispatch.Tests',
        '-testResults', $testXml, '-logFile', $unityLog
    )
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    Require ($process.ExitCode -eq 0) "Clean Unity test process failed with exit code $($process.ExitCode)."
    Require (Test-Path -LiteralPath $testXml) 'Unity test XML was not produced.'
    [xml]$results = Get-Content -Raw -LiteralPath $testXml
    $run = $results.'test-run'
    Require ([int]$run.total -gt 0 -and [int]$run.passed -eq [int]$run.total -and [int]$run.failed -eq 0 -and [int]$run.skipped -eq 0) 'Clean Unity contract tests did not all pass.'
    $requiredTests = @(
        'PublicSurface_MatchesAcceptedV2ManifestLineForLine',
        'PublicSurfaceV2_ChangesOnlyEnterAndTickOpaqueSizePins',
        'PinnedLayouts_ContextPrefixes_AndDefaultOpaqueValues_FailClosed',
        'ProductionGenerator_EmitsOnlyApprovedMetadataBoundary',
        'Authority_ExactlyMatchesCanonicalAuthoringBuiltIns',
        'AuthorityVerifier_RejectsStaleRegistryBytesAndHash',
        'GeneratedExecuteImmediate_DecodesConfigurationAndHandle_ThenCompletesCallback',
        'GeneratedSchedule_BurstJobExecutesTheSameGeneratedFacade'
    )
    $testNames = @($results.SelectNodes('//test-case') | ForEach-Object { [string]$_.fullname })
    foreach ($requiredTest in $requiredTests) {
        Require (@($testNames | Where-Object { $_.EndsWith('.' + $requiredTest, [StringComparison]::Ordinal) }).Count -eq 1) "Required Unity contract test was not executed exactly once: $requiredTest"
    }

    $log = Get-Content -Raw -LiteralPath $unityLog
    $failurePattern = 'CS8032|AD0001|will not be loaded|Could not load file or assembly|Analyzer.+failed|Generator.+failed|error CS[0-9]+'
    Require ($log -notmatch $failurePattern) 'Unity log contains analyzer/generator/compiler failure markers.'
    $lockPath = Join-Path $packages 'packages-lock.json'
    Require (Test-Path -LiteralPath $lockPath) 'Unity did not resolve a package lock.'

    # Import the UPM sample into a second clean project without making the package
    # itself testable. The sample's friend test assembly can then exercise the
    # internal batch owner while the sample nodes remain public-API-only consumers.
    $sampleProject = Join-Path $tempRoot 'SampleUnityProject'
    $samplePackages = Join-Path $sampleProject 'Packages'
    New-Item -ItemType Directory -Path (Join-Path $sampleProject 'Assets'), $samplePackages, (Join-Path $sampleProject 'ProjectSettings') | Out-Null
    $sampleManifest = @{
        dependencies = [ordered]@{
            'com.azzazello.aibt' = $packageUri
            'com.unity.test-framework' = '1.7.0'
        }
    } | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText((Join-Path $samplePackages 'manifest.json'), $sampleManifest, [Text.UTF8Encoding]::new($false))

    $sampleSource = Join-Path $packageRoot 'Samples~\BurstNodes'
    $sampleTarget = Join-Path $sampleProject 'Assets\PublicBurstNodeSample'
    Require (Test-Path -LiteralPath $sampleSource) 'Public Burst-node sample is missing.'
    Copy-Item -LiteralPath $sampleSource -Destination $sampleTarget -Recurse

    # The friend test assembly is not part of the shipped Samples~/BurstNodes
    # content (it exercises internal batch-owner APIs users never see), so it
    # lives here as .txt fixtures and is assembled into the sample project.
    $sampleGoldenSource = Join-Path $PSScriptRoot 'SampleGolden'
    $sampleGoldenTarget = Join-Path $sampleTarget 'Tests'
    New-Item -ItemType Directory -Path $sampleGoldenTarget -Force | Out-Null
    foreach ($fixture in @('AIBT.Samples.BurstNodes.Tests.asmdef', 'PublicBurstNodeSampleGoldenTests.cs')) {
        $fixtureSource = Join-Path $sampleGoldenSource ($fixture + '.txt')
        Require (Test-Path -LiteralPath $fixtureSource) "Sample golden fixture is missing: $fixture"
        Copy-Item -LiteralPath $fixtureSource -Destination (Join-Path $sampleGoldenTarget $fixture)
    }

    $sampleTestXml = Join-Path $tempRoot 'unity-sample-editmode.xml'
    $sampleUnityLog = Join-Path $tempRoot 'unity-sample.log'
    $sampleArguments = @(
        '-batchmode', '-nographics', '-projectPath', $sampleProject,
        '-runTests', '-testPlatform', 'EditMode', '-assemblyNames', 'AIBT.Samples.BurstNodes.Tests',
        '-testResults', $sampleTestXml, '-logFile', $sampleUnityLog
    )
    $sampleProcess = Start-Process -FilePath $UnityEditor -ArgumentList $sampleArguments -Wait -PassThru -WindowStyle Hidden
    Require ($sampleProcess.ExitCode -eq 0) "Clean Unity sample process failed with exit code $($sampleProcess.ExitCode)."
    Require (Test-Path -LiteralPath $sampleTestXml) 'Unity sample test XML was not produced.'
    [xml]$sampleResults = Get-Content -Raw -LiteralPath $sampleTestXml
    $sampleRun = $sampleResults.'test-run'
    Require ([int]$sampleRun.total -gt 0 -and [int]$sampleRun.passed -eq [int]$sampleRun.total -and [int]$sampleRun.failed -eq 0 -and [int]$sampleRun.skipped -eq 0) 'Clean Unity sample golden tests did not all pass.'
    $requiredSampleTests = @(
        'ThresholdCondition_GeneratedDispatchReadsTypedBlackboardValue',
        'AsyncWriteAction_GeneratedDispatchRunsThenConsumesCompletion',
        'AsyncWriteAction_GeneratedDispatchAbortPublishesCancellation'
    )
    $sampleTestNames = @($sampleResults.SelectNodes('//test-case') | ForEach-Object { [string]$_.fullname })
    foreach ($requiredSampleTest in $requiredSampleTests) {
        Require (@($sampleTestNames | Where-Object { $_.EndsWith('.' + $requiredSampleTest, [StringComparison]::Ordinal) }).Count -eq 1) "Required Unity sample golden was not executed exactly once: $requiredSampleTest"
    }

    $sampleLog = Get-Content -Raw -LiteralPath $sampleUnityLog
    Require ($sampleLog -notmatch $failurePattern) 'Unity sample log contains analyzer/generator/compiler failure markers.'
    $nodesAssembly = Join-Path $sampleProject 'Library\ScriptAssemblies\AIBT.BurstNodes.Sample.dll'
    $catalogAssembly = Join-Path $sampleProject 'Library\ScriptAssemblies\AIBT.BurstNodes.Sample.Catalog.dll'
    $sampleTestAssembly = Join-Path $sampleProject 'Library\ScriptAssemblies\AIBT.Samples.BurstNodes.Tests.dll'
    Require (Test-Path -LiteralPath $nodesAssembly) 'Public sample node assembly was not compiled.'
    Require (Test-Path -LiteralPath $catalogAssembly) 'Public sample catalog assembly was not compiled.'
    Require (Test-Path -LiteralPath $sampleTestAssembly) 'Public sample golden test assembly was not compiled.'
    Require (Test-Path -LiteralPath (Join-Path $samplePackages 'packages-lock.json')) 'Unity sample project did not resolve a package lock.'

    Write-Host "Analyzer SHA256: $actualHash"
    Write-Host 'Roslyn matrix: PASS (AIBT5001-AIBT5012; deterministic; opt-in)'
    Write-Host "Unity clean CodeGen/Dispatch tests: PASS ($($run.passed)/$($run.total))"
    Write-Host "Unity clean public sample goldens: PASS ($($sampleRun.passed)/$($sampleRun.total))"
    Write-Host "Artifacts: $tempRoot"
}
finally {
    if (-not $KeepArtifacts) {
        $buildDir = Join-Path $packageRoot 'CodeGen~\AIBT.CodeGen\bin\.p2-codegen-gate'
        $objDir = Join-Path $packageRoot 'CodeGen~\AIBT.CodeGen\obj\.p2-codegen-gate'
        if (Test-Path -LiteralPath $buildDir) { Remove-Item -LiteralPath $buildDir -Recurse -Force }
        if (Test-Path -LiteralPath $objDir) { Remove-Item -LiteralPath $objDir -Recurse -Force }
    }
    if (-not $KeepArtifacts -and (Test-Path -LiteralPath $tempRoot)) {
        $resolvedTemp = [IO.Path]::GetFullPath($tempRoot)
        $tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        Require ($resolvedTemp.StartsWith($tempParent, [StringComparison]::OrdinalIgnoreCase)) 'Refusing to remove a path outside the system temp directory.'
        # Imported Unity package cache content (e.g. com.unity.ai.navigation
        # samples) can nest deep enough to exceed Windows' classic MAX_PATH,
        # which makes plain Remove-Item fail mid-recursion. robocopy's mirror
        # of an empty directory is the standard reliable way to clear a tree
        # like that; fall back to Remove-Item for anything it leaves behind.
        try {
            Remove-Item -LiteralPath $resolvedTemp -Recurse -Force -ErrorAction Stop
        }
        catch {
            $emptyDir = Join-Path ([IO.Path]::GetTempPath()) ('AIBT-P2-012-empty-' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $emptyDir | Out-Null
            & robocopy $emptyDir $resolvedTemp /MIR /NFL /NDL /NJH /NJS | Out-Null
            Remove-Item -LiteralPath $emptyDir -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $resolvedTemp -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
