[CmdletBinding()]
param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe',
    [string]$OutputDirectory,
    [int]$BuildTimeoutSeconds = 1800,
    [int]$PlayerTimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'

function Invoke-BoundedProcess {
    param([string]$Executable, [string[]]$Arguments, [int]$TimeoutSeconds)
    $quoted = $Arguments | ForEach-Object { '"' + ($_ -replace '"', '\"') + '"' }
    $process = Start-Process -FilePath $Executable -ArgumentList ($quoted -join ' ') -PassThru -WindowStyle Hidden
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (-not $process.WaitForExit(1000)) {
        if ($timer.Elapsed.TotalSeconds -gt $TimeoutSeconds) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "Timeout: $Executable (PID $($process.Id))"
        }
    }
    if ($process.ExitCode -ne 0) { throw "Exit code $($process.ExitCode): $Executable" }
}

$aibtRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$hostRoot = Split-Path -Parent (Split-Path -Parent $aibtRoot)
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $PSScriptRoot ('Results/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw "Output must be new: $OutputDirectory" }
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Missing Unity: $UnityPath" }
$isolatedRoot = Join-Path ([IO.Path]::GetTempPath()) ('aibt-p7-026-' + [Guid]::NewGuid().ToString('N'))
foreach ($directory in @($OutputDirectory, "$isolatedRoot/Assets/AIBT", "$isolatedRoot/Packages", "$isolatedRoot/ProjectSettings")) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
foreach ($part in @('Runtime', 'Authoring', 'Analyzers')) {
    Copy-Item -LiteralPath (Join-Path $aibtRoot $part) -Destination "$isolatedRoot/Assets/AIBT/$part" -Recurse
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Unity') -Destination "$isolatedRoot/Assets/AIBTBuildSize" -Recurse
Copy-Item -LiteralPath "$hostRoot/ProjectSettings/ProjectVersion.txt" -Destination "$isolatedRoot/ProjectSettings/ProjectVersion.txt"
$manifest = @'
{"dependencies":{"com.unity.burst":"1.8.30","com.unity.collections":"6.5.0","com.unity.nuget.newtonsoft-json":"3.2.2","com.unity.modules.jsonserialize":"1.0.0"}}
'@
[IO.File]::WriteAllText("$isolatedRoot/Packages/manifest.json", $manifest, [Text.UTF8Encoding]::new($false))
$commit = (git -C $aibtRoot rev-parse HEAD).Trim()
@{ sourceCommit = $commit; isolatedProject = $isolatedRoot; unity = $UnityPath; utc = [DateTime]::UtcNow.ToString('o') } |
    ConvertTo-Json | Set-Content -LiteralPath "$OutputDirectory/run.json" -Encoding UTF8
Write-Host "Isolated project: $isolatedRoot"
Write-Host "Evidence: $OutputDirectory"
Invoke-BoundedProcess -Executable $UnityPath -TimeoutSeconds $BuildTimeoutSeconds -Arguments @(
    '-batchmode', '-nographics', '-burst-enable-compilation', '-projectPath', $isolatedRoot,
    '-executeMethod', 'AIBT.Benchmarks.BuildSize.BuildSizeBuild.Build', '-sizeOutput', $OutputDirectory,
    '-logFile', "$OutputDirectory/build.log", '-quit')

$builds = @()
$players = @()
foreach ($count in @(1, 100)) {
    $build = Get-Content -Raw -LiteralPath "$OutputDirectory/$count-build.json" | ConvertFrom-Json
    if ($build.backend -ne 'IL2CPP' -or $build.target -ne 'StandaloneWindows64' -or $build.development -or -not $build.burst) {
        throw "Wrong Player configuration: $count"
    }
    Write-Host "Validating $count shipped trees in Player"
    Invoke-BoundedProcess -Executable "$OutputDirectory/$count/AIBTSizeProbe.exe" -TimeoutSeconds $PlayerTimeoutSeconds -Arguments @(
        '-batchmode', '-nographics', '-expectedTrees', "$count", '-probeResult', "$OutputDirectory/$count-player.json", '-logFile', "$OutputDirectory/$count-player.log")
    $player = Get-Content -Raw -LiteralPath "$OutputDirectory/$count-player.json" | ConvertFrom-Json
    if ($player.trees -ne $count -or $player.contentHash -ne $build.contentHash -or $player.payloadBytes -ne $build.sourceBytes) {
        throw "Player payload mismatch: $count"
    }
    if (@($build.packedTrees | Select-Object -ExpandProperty path -Unique).Count -ne $count) { throw "Packed asset count mismatch: $count" }
    $binary2Text = Join-Path (Split-Path -Parent $UnityPath) 'Data/Tools/binary2text.exe'
    & $binary2Text "$OutputDirectory/$count/AIBTSizeProbe_Data/globalgamemanagers" "$OutputDirectory/$count-globalgamemanagers.txt"
    if ($LASTEXITCODE -ne 0) { throw "Serialized-file inspection failed: $count" }
    $builds += $build
    $players += $player
}
if ($players[0].catalogFingerprint -cnotmatch '^[0-9a-f]{64}$' -or $players[0].catalogFingerprint -ne $players[1].catalogFingerprint) {
    throw 'Missing catalog fingerprint or catalog changed between variants'
}
$before = @{}
foreach ($file in $builds[0].files) { $before[$file.path] = $file }
$after = @{}
foreach ($file in $builds[1].files) { $after[$file.path] = $file }
$differences = foreach ($path in @(@($before.Keys) + @($after.Keys) | Sort-Object -Unique)) {
    $a = $before[$path]; $b = $after[$path]
    if ($null -eq $a -or $null -eq $b -or $a.sha256 -ne $b.sha256) {
        [ordered]@{ path = $path; beforeBytes = [long]$a.bytes; afterBytes = [long]$b.bytes; deltaBytes = [long]$b.bytes - [long]$a.bytes; beforeHash = $a.sha256; afterHash = $b.sha256 }
    }
}
[ordered]@{
    sourceCommit = $commit; target = $builds[0].target; backend = $builds[0].backend; unityVersion = $builds[0].unityVersion
    treeCounts = @(1, 100); sourceBytes = @($builds[0].sourceBytes, $builds[1].sourceBytes)
    packedTreeBytes = @(($builds[0].packedTrees | Measure-Object bytes -Sum).Sum, ($builds[1].packedTrees | Measure-Object bytes -Sum).Sum)
    shippedBytes = @($builds[0].shippedBytes, $builds[1].shippedBytes)
    deltaBytes = $builds[1].shippedBytes - $builds[0].shippedBytes
    catalogFingerprint = $players[0].catalogFingerprint; differences = @($differences)
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$OutputDirectory/comparison.json" -Encoding UTF8
Write-Host "PASS: two IL2CPP builds and both Player payload probes. $OutputDirectory/comparison.json"
