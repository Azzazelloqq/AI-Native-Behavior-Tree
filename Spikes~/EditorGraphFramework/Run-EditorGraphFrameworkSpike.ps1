[CmdletBinding()]
param(
    [string] $UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe',
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$spikeRoot = $PSScriptRoot
$harnessRoot = Join-Path $spikeRoot 'Harness'
$unity = [IO.Path]::GetFullPath($UnityPath)

if (-not (Test-Path -LiteralPath $unity -PathType Leaf)) {
    throw "Unity executable was not found: $unity"
}
if (-not (Test-Path -LiteralPath (Join-Path $harnessRoot 'ProjectSettings\ProjectVersion.txt'))) {
    throw "Spike harness project was not found at: $harnessRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $OutputPath = Join-Path $spikeRoot "..\..\Planning~\Evidence\P3-001\spike-results-$stamp.json"
}
$finalOutput = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $finalOutput) -Force | Out-Null

$logPath = Join-Path $harnessRoot 'spike-run.log'
if (Test-Path -LiteralPath $logPath) { Remove-Item -LiteralPath $logPath -Force }

$arguments = @(
    '-batchmode', '-nographics',
    '-projectPath', ('"{0}"' -f $harnessRoot),
    '-executeMethod', 'AIBT.Spikes.EditorGraphFramework.SpikeRunner.Run',
    '-aibtSpikeOutput', ('"{0}"' -f $finalOutput),
    '-logFile', ('"{0}"' -f $logPath),
    '-quit'
)
$process = Start-Process -FilePath $unity -ArgumentList $arguments -Wait -PassThru -NoNewWindow
if (-not (Test-Path -LiteralPath $finalOutput)) {
    throw "Spike runner did not produce output. Exit code $($process.ExitCode). See $logPath"
}
Write-Output "Spike results: $finalOutput"
Write-Output "Log: $logPath"
Write-Output "Exit code: $($process.ExitCode)"
