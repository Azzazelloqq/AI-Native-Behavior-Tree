[CmdletBinding()]
param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.7.0b1\Editor\Unity.exe',
    [string]$IsolatedProjectPath,
    [string]$OutputPath,
    [int]$BuildTimeoutSeconds = 1800,
    [int]$PlayerTimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity not found: $UnityPath" }
if ([string]::IsNullOrWhiteSpace($IsolatedProjectPath)) {
    $IsolatedProjectPath = Join-Path ([IO.Path]::GetTempPath()) ('aibt-consumer-player-' + [Guid]::NewGuid().ToString('N'))
}
$project = [IO.Path]::GetFullPath($IsolatedProjectPath)
if ((Test-Path -LiteralPath $project) -and (Get-ChildItem -LiteralPath $project -Force).Count -gt 0) { throw 'Use an empty isolated project directory.' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $project 'Evidence' }
$results = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path (Join-Path $project 'Assets/Verification/Editor'), (Join-Path $project 'Packages'),
    (Join-Path $project 'ProjectSettings'), $results -Force | Out-Null
$manifest = @{ dependencies = @{
    'com.azzazello.aibt' = 'file:' + ($packageRoot -replace '\\', '/')
    'com.unity.modules.physics' = '1.0.0'
    'com.unity.modules.jsonserialize' = '1.0.0'
} } | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $project 'Packages/manifest.json'), $manifest)
Copy-Item -LiteralPath (Join-Path (Split-Path -Parent (Split-Path -Parent $packageRoot)) 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $project 'ProjectSettings/ProjectVersion.txt')
Copy-Item -LiteralPath (Join-Path $packageRoot 'Samples~/FirstAgent') -Destination (Join-Path $project 'Assets/FirstAgent') -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ConsumerProbe.cs.txt') -Destination (Join-Path $project 'Assets/Verification/ConsumerProbe.cs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ConsumerBuild.cs.txt') -Destination (Join-Path $project 'Assets/Verification/Editor/ConsumerBuild.cs')
$runtimeAssembly = @{ name = 'AIBT.ConsumerVerification'; references = @('AIBT.Runtime', 'AIBT.Samples.FirstAgent', 'AIBT.Samples.FirstAgent.Catalog') } | ConvertTo-Json
$editorAssembly = @{ name = 'AIBT.ConsumerVerification.Editor'; references = @('AIBT.ConsumerVerification', 'AIBT.Runtime', 'AIBT.Editor', 'AIBT.Samples.FirstAgent.Editor', 'Unity.Burst'); includePlatforms = @('Editor') } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $project 'Assets/Verification/AIBT.ConsumerVerification.asmdef'), $runtimeAssembly)
[IO.File]::WriteAllText((Join-Path $project 'Assets/Verification/Editor/AIBT.ConsumerVerification.Editor.asmdef'), $editorAssembly)
$player = Join-Path $project 'Build/Consumer.exe'
$buildLog = Join-Path $project 'build.log'
$playerLog = Join-Path $project 'player.log'
$buildEvidence = Join-Path $results 'build.raw.json'
$playerEvidence = Join-Path $results 'player.raw.json'
function Invoke-BoundedProcess([string]$Executable, [string[]]$Arguments, [int]$TimeoutSeconds) {
    $quoted = @($Arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } })
    $process = Start-Process -FilePath $Executable -ArgumentList $quoted -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id
        throw "Verification process timed out: $Executable"
    }
    if ($process.ExitCode -ne 0) { throw "Verification process failed with exit code $($process.ExitCode). Logs: $project" }
}
Invoke-BoundedProcess $UnityPath @('-batchmode', '-nographics', '-projectPath', $project, '-executeMethod', 'ConsumerBuild.Build',
    '-aibtPlayerOutput', $player, '-aibtBuildEvidence', $buildEvidence, '-logFile', $buildLog, '-quit') $BuildTimeoutSeconds
if (-not (Test-Path -LiteralPath $player)) { throw "Player not built. See $buildLog" }
Invoke-BoundedProcess $player @('-batchmode', '-nographics', '-aibtReport', $playerEvidence, '-logFile', $playerLog) $PlayerTimeoutSeconds
if ((Get-Content -LiteralPath $playerLog -Raw) -notmatch 'AIBT_CONSUMER_PLAYER_OK\|cells=6') { throw "Player success marker missing. See $playerLog" }
Write-Output "Consumer Player workflow passed. Evidence: $results; logs and build: $project"
