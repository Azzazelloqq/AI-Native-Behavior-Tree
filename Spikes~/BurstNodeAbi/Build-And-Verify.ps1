param(
    [string]$UnityEditorPath = $env:UNITY_EDITOR_PATH,
    [switch]$SkipUnity
)

$ErrorActionPreference = 'Stop'
$spikeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$harnessRoot = Join-Path $spikeRoot 'Harness'
$artifactsRoot = Join-Path $spikeRoot 'artifacts'
$generatorProject = Join-Path $spikeRoot 'Generator\AIBT.BurstNodeAbi.Feasibility.csproj'
$runnerProject = Join-Path $spikeRoot 'Runner\AIBT.BurstNodeAbi.Runner.csproj'
$sourceRoot = Join-Path $harnessRoot 'Assets\Source'
$analyzerRoot = Join-Path $harnessRoot 'Assets\Analyzers'

function Wait-HarnessUnityQuiescent {
    param([string]$ProjectPath, [int]$TimeoutSeconds = 30)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $forwardPath = $ProjectPath.Replace('\', '/')
    $lockPath = Join-Path $ProjectPath 'Temp\UnityLockfile'
    $stableChecks = 0
    $processFreeChecks = 0
    do {
        $projectProcesses = @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue |
            Where-Object { $_.CommandLine -and ($_.CommandLine.Contains($ProjectPath) -or $_.CommandLine.Contains($forwardPath)) })
        $lockPresent = Test-Path -LiteralPath $lockPath
        if ($projectProcesses.Count -eq 0) {
            $processFreeChecks++
            if ($lockPresent -and $processFreeChecks -ge 4) {
                try {
                    $lockStream = [IO.File]::Open($lockPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
                    $lockStream.Dispose()
                    Remove-Item -LiteralPath $lockPath -Force
                    $lockPresent = $false
                }
                catch {
                    $lockPresent = $true
                }
            }
        }
        else {
            $processFreeChecks = 0
        }
        if ($projectProcesses.Count -eq 0 -and -not $lockPresent) {
            $stableChecks++
            if ($stableChecks -ge 8) { return }
        }
        else {
            $stableChecks = 0
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    $processIds = ($projectProcesses | ForEach-Object { $_.ProcessId }) -join ','
    throw "Harness Unity did not become quiescent within ${TimeoutSeconds}s. Processes=[$processIds], LockPresent=$lockPresent, LockPath=$lockPath"
}

function Remove-HarnessDirectoryWithRetry {
    param([string]$LiteralPath, [int]$TimeoutSeconds = 15)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastError = $null
    do {
        try {
            if (Test-Path -LiteralPath $LiteralPath) { Remove-Item -LiteralPath $LiteralPath -Recurse -Force -ErrorAction Stop }
            if (-not (Test-Path -LiteralPath $LiteralPath)) { return }
        }
        catch { $lastError = $_ }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Unable to clean validated Harness directory '$LiteralPath' within ${TimeoutSeconds}s. LastError=$lastError"
}

New-Item -ItemType Directory -Force -Path $artifactsRoot, $analyzerRoot | Out-Null
$env:DOTNET_ROLL_FORWARD = 'Major'

dotnet build $generatorProject -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Generator build failed.' }
dotnet build $runnerProject -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Runner build failed.' }

$generatorDll = Join-Path $spikeRoot 'Generator\bin\Release\netstandard2.0\AIBT.BurstNodeAbi.Feasibility.dll'
$analyzerDll = Join-Path $analyzerRoot 'AIBT.BurstNodeAbi.Feasibility.dll'
Copy-Item -LiteralPath $generatorDll -Destination $analyzerDll -Force

$verificationRoot = Join-Path $artifactsRoot ('verification-' + [Guid]::NewGuid().ToString('N'))
$runA = Join-Path $verificationRoot 'clean-a'
$runB = Join-Path $verificationRoot 'clean-b'
New-Item -ItemType Directory -Force -Path $runA, $runB | Out-Null
dotnet run --project $runnerProject -c Release --no-build -- $sourceRoot $runA
if ($LASTEXITCODE -ne 0) { throw 'First isolated Roslyn run failed.' }
dotnet run --project $runnerProject -c Release --no-build -- $sourceRoot $runB
if ($LASTEXITCODE -ne 0) { throw 'Second isolated Roslyn run failed.' }
$filesA = @(Get-ChildItem -LiteralPath $runA -File | Sort-Object Name)
$filesB = @(Get-ChildItem -LiteralPath $runB -File | Sort-Object Name)
if (($filesA.Name -join '|') -ne ($filesB.Name -join '|')) { throw 'Clean runner artifact sets differ.' }
foreach ($fileA in $filesA) {
    $fileB = Join-Path $runB $fileA.Name
    $hashA = (Get-FileHash -Algorithm SHA256 -LiteralPath $fileA.FullName).Hash
    $hashB = (Get-FileHash -Algorithm SHA256 -LiteralPath $fileB).Hash
    if ($hashA -ne $hashB) { throw "Clean runner artifact differs: $($fileA.Name)" }
}
$shaA = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $runA 'AibtBurstCatalogSet.g.cs')).Hash

if ($SkipUnity) {
    Write-Host "Roslyn verification passed; Unity skipped explicitly. SHA-256=$shaA"
    exit 0
}

if ([string]::IsNullOrWhiteSpace($UnityEditorPath)) {
    $UnityEditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe'
}
$resolvedUnity = (Resolve-Path -LiteralPath $UnityEditorPath).Path
$unityVersion = (Get-Item -LiteralPath $resolvedUnity).VersionInfo.ProductVersion
if ($unityVersion -notlike '6000.5.8f1*') { throw "Expected Unity 6000.5.8f1, found $unityVersion" }
$unityDirectory = Split-Path -Parent $resolvedUnity
$roslynCandidates = @(Get-ChildItem -LiteralPath (Join-Path $unityDirectory 'Data\DotNetSdk\sdk') -Filter 'Microsoft.CodeAnalysis.dll' -File -Recurse |
    Where-Object { $_.FullName -like '*\Roslyn\bincore\Microsoft.CodeAnalysis.dll' })
if ($roslynCandidates.Count -ne 1) { throw "Expected exactly one Unity Roslyn compiler assembly, found $($roslynCandidates.Count)." }
$roslynDll = $roslynCandidates[0].FullName
$roslynVersion = [System.Reflection.AssemblyName]::GetAssemblyName($roslynDll).Version
if ($roslynVersion.Major -ne 4 -or $roslynVersion.Minor -ne 10) { throw "Expected Unity Roslyn 4.10, found $roslynVersion" }
$packageManifest = Get-Content -Raw -LiteralPath (Join-Path $spikeRoot '..\..\package.json') | ConvertFrom-Json
$burstVersion = $packageManifest.dependencies.'com.unity.burst'
if ($burstVersion -ne '1.8.29') { throw "Expected Burst package 1.8.29, found $burstVersion" }

$harnessLibrary = [IO.Path]::GetFullPath((Join-Path $harnessRoot 'Library'))
$expectedLibrary = [IO.Path]::Combine([IO.Path]::GetFullPath($harnessRoot).TrimEnd('\'), 'Library')
if ($harnessLibrary -ne $expectedLibrary) { throw "Refusing to clean unexpected Unity Library path: $harnessLibrary" }
Wait-HarnessUnityQuiescent -ProjectPath $harnessRoot
Remove-HarnessDirectoryWithRetry -LiteralPath $harnessLibrary

$analyzerFailurePattern = '(?im)CS8032|AD0001|will not be loaded|Unable to resolve reference|source generator[^\r\n]*failed|analyzer[^\r\n]*exception|YAML parse error'
$probeRoot = Join-Path $harnessRoot 'Assets\AnalyzerProbe'
$probeSource = Join-Path $probeRoot 'InvalidProbe.cs'
$probeAsmdef = Join-Path $probeRoot 'AIBT.BurstAbi.InvalidProbe.asmdef'
$probeLog = Join-Path $artifactsRoot 'unity-invalid-analyzer-probe.log'
New-Item -ItemType Directory -Force -Path $probeRoot | Out-Null
$invalidProbeSource = @'
using AIBT; using AIBT.Burst;
namespace AIBT.BurstAbi.InvalidProbe {
public partial struct Config { [AibtConfigField("value", "aibt.int32", 1u)] public int Value; }
public partial struct Memory { [AibtMemoryField("value", "aibt.int32", 1u)] public int Value; }
[AibtCatalogShard("invalid.probe", 1u)] public partial struct ProbeShard { }
[AibtBurstNode("aibt.invalid.probe", 1u, BurstNodeKind.Action, typeof(Config), typeof(Memory), NodeMemoryLifetime.Activation, true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial, BurstNodeStatusMask.Success)]
[AibtNodeDocumentation("Invalid probe", "Tests", "Probe", "Production", "invalid-probe")]
public struct InvalidProbeNode {
public static void Enter(in Config config, ref Memory memory, ref BurstEnterContext context) { }
public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { return NodeStatus.Success; }
public static void Abort(in Config config, ref Memory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
public static void Exit(in Config config, ref Memory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
} }
'@
$invalidProbeAsmdef = @'
{
  "name": "AIBT.BurstAbi.InvalidProbe",
  "references": ["AIBT.Runtime", "AIBT.BurstAbi.Contracts"],
  "analyzers": ["GUID:8f79970e34924dc1a4e944a9ed8b6281"],
  "includePlatforms": ["Editor"]
}
'@
[IO.File]::WriteAllText($probeSource, $invalidProbeSource, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($probeAsmdef, $invalidProbeAsmdef, [Text.UTF8Encoding]::new($false))
Remove-Item -LiteralPath $probeLog -Force -ErrorAction SilentlyContinue
$probeArguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $harnessRoot + '"'), '-quit', '-logFile', ('"' + $probeLog + '"'))
$probeProcess = Start-Process -FilePath $resolvedUnity -ArgumentList $probeArguments -PassThru -WindowStyle Hidden
if (-not $probeProcess.WaitForExit(120000)) {
    Stop-Process -Id $probeProcess.Id -Force -ErrorAction SilentlyContinue
    throw "Unity invalid analyzer probe did not exit within 120s. PID=$($probeProcess.Id), Log=$probeLog"
}
if (-not (Test-Path -LiteralPath $probeLog)) { throw 'Unity invalid analyzer probe produced no log.' }
$probeLogText = Get-Content -Raw -LiteralPath $probeLog
Remove-Item -LiteralPath $probeSource, $probeAsmdef, ($probeSource + '.meta'), ($probeAsmdef + '.meta') -Force -ErrorAction SilentlyContinue
if ($probeLogText -notmatch 'AIBT5001') { throw 'Unity invalid analyzer probe did not emit AIBT5001.' }
if ($probeLogText -match $analyzerFailurePattern) { throw "Unity invalid analyzer probe contains analyzer load/failure markers. See $probeLog" }
Wait-HarnessUnityQuiescent -ProjectPath $harnessRoot

$testResults = Join-Path $artifactsRoot 'unity-editmode.xml'
$unityLog = Join-Path $artifactsRoot 'unity.log'
Remove-Item -LiteralPath $testResults, $unityLog -Force -ErrorAction SilentlyContinue
$unityArguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $harnessRoot + '"'), '-runTests',
    '-testPlatform', 'EditMode', '-testResults', ('"' + $testResults + '"'), '-logFile', ('"' + $unityLog + '"'))
$unityProcess = Start-Process -FilePath $resolvedUnity -ArgumentList $unityArguments -Wait -PassThru -WindowStyle Hidden
if ($unityProcess.ExitCode -ne 0) { throw "Unity failed with exit code $($unityProcess.ExitCode). See $unityLog" }
if (-not (Test-Path -LiteralPath $testResults)) { throw "Unity did not produce $testResults" }
$resolvedPackagesPath = Join-Path $harnessRoot 'Packages\packages-lock.json'
if (-not (Test-Path -LiteralPath $resolvedPackagesPath)) { throw "Unity did not produce $resolvedPackagesPath" }
$resolvedPackages = Get-Content -Raw -LiteralPath $resolvedPackagesPath | ConvertFrom-Json
$resolvedBurstVersion = $resolvedPackages.dependencies.'com.unity.burst'.version
if ($resolvedBurstVersion -ne '1.8.29') { throw "Expected resolved Burst package 1.8.29, found $resolvedBurstVersion" }
$unityLogText = Get-Content -Raw -LiteralPath $unityLog
$burstRegistrations = [regex]::Matches($unityLogText, 'com\.unity\.burst@(?<version>\d+\.\d+\.\d+)\s+\(location:\s*(?<location>[^\r\n\)]+)\)')
if ($burstRegistrations.Count -eq 0) { throw "Unity log contains no resolved Burst package registration. See $unityLog" }
$finalBurstRegistration = $burstRegistrations[$burstRegistrations.Count - 1]
$loggedBurstVersion = $finalBurstRegistration.Groups['version'].Value
if ($loggedBurstVersion -ne '1.8.29') { throw "Expected final Unity log Burst registration 1.8.29, found $loggedBurstVersion" }
$burstPackagePath = $finalBurstRegistration.Groups['location'].Value.Trim()
$burstPackageManifestPath = Join-Path $burstPackagePath 'package.json'
if (-not (Test-Path -LiteralPath $burstPackageManifestPath)) { throw "Resolved Burst package manifest is missing: $burstPackageManifestPath" }
$actualBurstVersion = (Get-Content -Raw -LiteralPath $burstPackageManifestPath | ConvertFrom-Json).version
if ($actualBurstVersion -ne '1.8.29') { throw "Expected actual Burst package 1.8.29, found $actualBurstVersion" }
$burstFailurePattern = '(?im)\bBC\d{4}\b|Burst compilation failed|Burst compiler failed|falling back to managed'
if ($unityLogText -match $burstFailurePattern) { throw "Unity log contains a Burst compilation error or managed fallback marker. See $unityLog" }
if ($unityLogText -match $analyzerFailurePattern) { throw "Unity log contains analyzer/generator load or execution failure markers. See $unityLog" }
[xml]$results = Get-Content -Raw -LiteralPath $testResults
$failed = [int]$results.'test-run'.failed
$total = [int]$results.'test-run'.total
$skipped = [int]$results.'test-run'.skipped
if ($total -le 0) { throw "Unity discovered no EditMode tests. See $testResults" }
if ($skipped -ne 0) { throw "Unity EditMode skipped tests: $skipped. See $testResults" }
if ($failed -ne 0) { throw "Unity EditMode failures: $failed. See $testResults" }

Write-Host "Burst ABI verification passed. Unity=$unityVersion Roslyn=$roslynVersion Burst=$actualBurstVersion SHA-256=$shaA"
