[CmdletBinding()]
param(
    [Parameter()]
    [string] $UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe',

    [Parameter()]
    [string] $RepositoryPath,

    [Parameter()]
    [string] $HarnessPath
)

$ErrorActionPreference = 'Stop'
$spikeRoot = $PSScriptRoot
$packageRoot = [System.IO.Path]::GetFullPath((Join-Path $spikeRoot '../..'))
$repositoryRoot = if ([string]::IsNullOrWhiteSpace($RepositoryPath)) {
    [System.IO.Path]::GetFullPath((Join-Path $packageRoot '../..'))
} else { [System.IO.Path]::GetFullPath($RepositoryPath) }
$harnessRoot = if ([string]::IsNullOrWhiteSpace($HarnessPath)) {
    $runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    Join-Path $packageRoot "Tools~/Verification/TestResults/P0-003-WebHarness/$runId"
} else { [System.IO.Path]::GetFullPath($HarnessPath) }
$harnessRoot = [System.IO.Path]::GetFullPath($harnessRoot)
$allowedDefaultRoot = [System.IO.Path]::GetFullPath((Join-Path $packageRoot 'Tools~/Verification/TestResults'))
if ([string]::IsNullOrWhiteSpace($HarnessPath) -and -not $harnessRoot.StartsWith($allowedDefaultRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Default harness path escaped the ignored verification root: '$harnessRoot'."
}
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity was not found at '$UnityPath'." }

if (Test-Path -LiteralPath $harnessRoot) { throw "Harness path already exists: '$harnessRoot'." }
New-Item -ItemType Directory -Path $harnessRoot -Force | Out-Null
Copy-Item -Path (Join-Path $spikeRoot 'Harness/*') -Destination $harnessRoot -Recurse -Force

$assets = Join-Path $harnessRoot 'Assets'
New-Item -ItemType Directory -Path (Join-Path $assets 'AIBT') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $packageRoot 'Runtime') -Destination (Join-Path $assets 'AIBT/Runtime') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $packageRoot 'Authoring') -Destination (Join-Path $assets 'AIBT/Authoring') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $packageRoot 'Tests/BehaviorCases/Framework') -Destination (Join-Path $assets 'WebBackend/BehaviorCases') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $packageRoot 'Tests/Integration/SemanticSlice/ReferenceBehaviorCaseAdapter.cs') -Destination (Join-Path $assets 'WebBackend/ReferenceBehaviorCaseAdapter.cs') -Force
Copy-Item -LiteralPath (Join-Path $packageRoot 'Tests/Integration/SemanticSlice/SemanticSliceNodeContracts.cs') -Destination (Join-Path $assets 'WebBackend/SemanticSliceNodeContracts.cs') -Force
$behaviorTests = Join-Path $assets 'WebBackend/BehaviorCases/Tests'
if (Test-Path -LiteralPath $behaviorTests) { Remove-Item -LiteralPath $behaviorTests -Recurse -Force }
Get-ChildItem -LiteralPath (Join-Path $assets 'WebBackend/BehaviorCases') -Recurse -Filter '*.asmdef' | Remove-Item -Force

$streaming = Join-Path $assets 'StreamingAssets/Golden'
New-Item -ItemType Directory -Path $streaming -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $packageRoot 'Tests/Fixtures/Golden/Trees') -Destination (Join-Path $streaming 'Trees') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $packageRoot 'Tests/Fixtures/Golden/Cases') -Destination (Join-Path $streaming 'Cases') -Recurse -Force

$buildOutput = Join-Path $harnessRoot 'Builds/Web'
$logPath = Join-Path $harnessRoot 'web-build.log'
$arguments = @(
    '-batchmode', '-nographics', '-quit',
    '-projectPath', ('"{0}"' -f $harnessRoot),
    '-executeMethod', 'AIBT.Spikes.WebBackend.Editor.WebBackendBuild.Build',
    '-aibtWebOutput', ('"{0}"' -f $buildOutput),
    '-logFile', ('"{0}"' -f $logPath)
)
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -Wait -PassThru -NoNewWindow
if ($process.ExitCode -ne 0) { throw "WebGL build failed with exit code $($process.ExitCode). See '$logPath'." }
if (-not (Test-Path -LiteralPath (Join-Path $buildOutput 'index.html'))) { throw 'WebGL build did not produce index.html.' }
Write-Output $buildOutput
