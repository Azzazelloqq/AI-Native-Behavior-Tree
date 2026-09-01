[CmdletBinding()]
param(
    [string] $UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe',
    [string] $OutputPath,
    [string] $IsolatedProjectPath
)

$ErrorActionPreference = 'Stop'

function Require([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$unity = [IO.Path]::GetFullPath($UnityPath)
Require (Test-Path -LiteralPath $unity -PathType Leaf) "Unity executable was not found: $unity"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$aibtRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot '..\..\..\..'))
$sourceProjectRoot = Split-Path -Parent (Split-Path -Parent $aibtRoot)
$dumpTemplate = Join-Path $scriptRoot 'PublicApiDump.cs.txt'
Require (Test-Path -LiteralPath $dumpTemplate) "Dump template is missing: $dumpTemplate"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $scriptRoot ('Results\public-api-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '.txt')
}
$finalOutput = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $finalOutput) -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($IsolatedProjectPath)) {
    $IsolatedProjectPath = Join-Path ([IO.Path]::GetTempPath()) ('aibt-p7-001-publicapi-' + [Guid]::NewGuid().ToString('N'))
}
$project = [IO.Path]::GetFullPath($IsolatedProjectPath)
Require (-not (Test-Path -LiteralPath $project) -or (Get-ChildItem -LiteralPath $project -Force).Count -eq 0) "IsolatedProjectPath must not exist or must be empty: $project"

# Same rationale as Tools~/Verification/P2/Audit/Get-PublicApi.ps1's own comment: reflecting over
# Unity-Mono-compiled netstandard2.1 assemblies from plain Windows PowerShell 5.1 cannot reliably
# resolve netstandard/Unity BCL dependencies, so the dump runs as an -executeMethod Editor script
# inside a real (isolated, disposable) Unity project instead of host-process reflection.
#
# P7-001 widens this to all four public assemblies (P2's own version only ever covered
# AIBT.Runtime/AIBT.Authoring; AIBT.Editor and AIBT.Mcp were added in later phases and every gate
# since P3 improvised an uncommitted, thrown-away extension to cover them -- see
# Planning~/Evidence/P6-GATE/gate-runbook.md). No new package dependency is needed: both
# AIBT.Editor.asmdef and AIBT.Mcp.asmdef reference only AIBT.Runtime/AIBT.Authoring/
# Newtonsoft.Json/Unity.Collections, all already in the manifest below.
$assets = Join-Path $project 'Assets'
$packages = Join-Path $project 'Packages'
$settings = Join-Path $project 'ProjectSettings'
New-Item -ItemType Directory -Path $assets, $packages, $settings, (Join-Path $assets 'AIBT'), (Join-Path $assets 'Editor') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $aibtRoot 'Runtime') -Destination (Join-Path $assets 'AIBT\Runtime') -Recurse
Copy-Item -LiteralPath (Join-Path $aibtRoot 'Authoring') -Destination (Join-Path $assets 'AIBT\Authoring') -Recurse
Copy-Item -LiteralPath (Join-Path $aibtRoot 'Editor') -Destination (Join-Path $assets 'AIBT\Editor') -Recurse
Copy-Item -LiteralPath (Join-Path $aibtRoot 'MCP') -Destination (Join-Path $assets 'AIBT\MCP') -Recurse
Copy-Item -LiteralPath $dumpTemplate -Destination (Join-Path $assets 'Editor\PublicApiDump.cs')
Copy-Item -LiteralPath (Join-Path $sourceProjectRoot 'ProjectSettings\ProjectVersion.txt') -Destination (Join-Path $settings 'ProjectVersion.txt')
$manifest = [ordered]@{
    dependencies = [ordered]@{
        'com.unity.burst' = '1.8.29'
        'com.unity.collections' = '6.5.0'
        'com.unity.nuget.newtonsoft-json' = '3.2.2'
        'com.unity.modules.jsonserialize' = '1.0.0'
    }
} | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $packages 'manifest.json'), $manifest, [Text.UTF8Encoding]::new($false))

$dumpLog = Join-Path $project 'public-api-dump.log'
$dumpArguments = @(
    '-batchmode', '-nographics',
    '-projectPath', $project,
    '-executeMethod', 'PublicApiDump.Run',
    '-aibtPublicApiOutput', $finalOutput,
    '-logFile', $dumpLog,
    '-quit'
)
$argLine = ($dumpArguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
$process = Start-Process -FilePath $unity -ArgumentList $argLine -PassThru -WindowStyle Hidden -Wait
Require ($process.ExitCode -eq 0) "Public API dump Unity process failed with exit code $($process.ExitCode). See $dumpLog"

$logText = Get-Content -Raw -LiteralPath $dumpLog
foreach ($pattern in @('\berror CS\d{4}\b', '\bBC\d{4}\b')) {
    if ($logText -match $pattern) { throw "Public API dump log failed scan: $pattern" }
}
if ($logText -notmatch 'AIBT_PUBLIC_API_OK\|') { throw 'Public API dump success marker is missing.' }
Require (Test-Path -LiteralPath $finalOutput) "Public API dump did not produce output: $finalOutput"

$shaPath = $finalOutput -replace '\.txt$', '.sha256'
Require (Test-Path -LiteralPath $shaPath) "Public API dump did not produce a digest: $shaPath"

Write-Output "Public API dump: $finalOutput"
Write-Output "Digest: $shaPath"
Write-Output ((Select-String -Path $dumpLog -Pattern 'AIBT_PUBLIC_API_OK\|.*').Matches[0].Value)
