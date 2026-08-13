[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $UnityPath,

    [Parameter(Mandatory)]
    [string] $ProjectPath,

    [Parameter()]
    [ValidateSet('EditMode', 'PlayMode')]
    [string] $Mode = 'EditMode',

    [Parameter()]
    [string] $TestFilter,

    [Parameter()]
    [ValidateSet('Focused', 'Full')]
    [string] $Scope = 'Full',

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

if ($Scope -eq 'Focused' -and [string]::IsNullOrWhiteSpace($TestFilter)) {
    throw 'Focused test scope requires -TestFilter.'
}
if ($Scope -eq 'Full' -and -not [string]::IsNullOrWhiteSpace($TestFilter)) {
    throw 'Full test scope does not accept -TestFilter. Use -Scope Focused.'
}

if (-not (Test-Path -LiteralPath $unityExecutable -PathType Leaf)) {
    throw "Unity executable was not found at '$unityExecutable'."
}

if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt'))) {
    throw "Unity project was not found at '$projectRoot'."
}

New-Item -ItemType Directory -Path $resultRoot -Force | Out-Null
$resultFile = Join-Path $resultRoot "$Mode-$Scope-results.xml"
$logFile = Join-Path $resultRoot "$Mode-$Scope.log"

$arguments = @(
    '-batchmode',
    '-nographics',
    '-projectPath', ('"{0}"' -f $projectRoot),
    '-runTests',
    '-testPlatform', $Mode,
    '-testResults', ('"{0}"' -f $resultFile),
    '-logFile', ('"{0}"' -f $logFile)
)

if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
    $arguments += @('-testFilter', $TestFilter)
}

$process = Start-Process -FilePath $unityExecutable -ArgumentList $arguments -Wait -PassThru -NoNewWindow
if ($process.ExitCode -ne 0) {
    throw "Unity tests failed with exit code $($process.ExitCode). See '$logFile'."
}

if (-not (Test-Path -LiteralPath $resultFile)) {
    throw "Unity exited successfully but did not create '$resultFile'."
}

[xml] $result = Get-Content -LiteralPath $resultFile -Raw -Encoding UTF8
$testRun = $result.'test-run'
if ($null -eq $testRun) {
    throw "Unity test results do not contain a test-run root. See '$resultFile'."
}

$total = [int] $testRun.total
if ($total -le 0) {
    throw "Unity discovered no tests for scope '$Scope'. See '$resultFile'."
}

$failed = [int] $testRun.failed
if ($failed -gt 0) {
    throw "$failed Unity test(s) failed. See '$resultFile'."
}

Write-Output "Unity $Mode tests passed. Results: '$resultFile'."
