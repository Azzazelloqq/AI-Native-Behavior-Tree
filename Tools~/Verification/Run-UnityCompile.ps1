[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $UnityPath,

    [Parameter(Mandatory)]
    [string] $ProjectPath,

    [Parameter()]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$OutputPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $PSScriptRoot 'TestResults'
}
else {
    $OutputPath
}
$unityExecutable = [System.IO.Path]::GetFullPath($UnityPath)
$projectRoot = [System.IO.Path]::GetFullPath($ProjectPath)
$resultRoot = [System.IO.Path]::GetFullPath($OutputPath)

if (-not (Test-Path -LiteralPath $unityExecutable -PathType Leaf)) {
    throw "Unity executable was not found at '$unityExecutable'."
}
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt'))) {
    throw "Unity project was not found at '$projectRoot'."
}

New-Item -ItemType Directory -Path $resultRoot -Force | Out-Null
$logFile = Join-Path $resultRoot 'Compile.log'
$arguments = @(
    '-batchmode',
    '-nographics',
    '-projectPath', ('"{0}"' -f $projectRoot),
    '-quit',
    '-logFile', ('"{0}"' -f $logFile)
)
$process = Start-Process -FilePath $unityExecutable -ArgumentList $arguments -Wait -PassThru -NoNewWindow
if ($process.ExitCode -ne 0) {
    throw "Unity compile validation failed with exit code $($process.ExitCode). See '$logFile'."
}

Write-Output "Unity compile validation passed. Log: '$logFile'."
