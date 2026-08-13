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
$unityExecutable = [IO.Path]::GetFullPath($UnityPath)
$projectRoot = [IO.Path]::GetFullPath($ProjectPath)
$resultRoot = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $PSScriptRoot '..\TestResults\Android'
} else {
    [IO.Path]::GetFullPath($OutputPath)
}

if (-not (Test-Path -LiteralPath $unityExecutable -PathType Leaf)) {
    throw "Unity executable was not found at '$unityExecutable'."
}
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -PathType Leaf)) {
    throw "Unity project was not found at '$projectRoot'."
}

$androidRoot = Join-Path (Split-Path (Split-Path $unityExecutable -Parent) -Parent) 'Editor\Data\PlaybackEngines\AndroidPlayer'
foreach ($required in @('SDK', 'NDK', 'OpenJDK')) {
    if (-not (Test-Path -LiteralPath (Join-Path $androidRoot $required) -PathType Container)) {
        throw "Android Build Support is incomplete: bundled $required was not found. Install Android Build Support with SDK, NDK, and OpenJDK for this editor."
    }
}

$generatedRoot = Join-Path $projectRoot 'Assets\AIBT.AndroidBuildSmoke'
$editorRoot = Join-Path $generatedRoot 'Editor'
$driverTarget = Join-Path $editorRoot 'AndroidBuildSmoke.cs'
$outputApk = Join-Path $resultRoot 'AIBT-Android-ARM64.apk'
$rawEvidence = Join-Path $resultRoot 'build-evidence.raw.json'
$rawLog = Join-Path $resultRoot 'android-build.raw.log'

New-Item -ItemType Directory -Path $editorRoot -Force | Out-Null
New-Item -ItemType Directory -Path $resultRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AndroidBuildSmoke.cs.txt') -Destination $driverTarget -Force

$arguments = @(
    '-batchmode',
    '-nographics',
    '-quit',
    '-projectPath', ('"{0}"' -f $projectRoot),
    '-executeMethod', 'AIBT.Verification.Android.AndroidBuildSmoke.Build',
    '-aibtAndroidOutput', ('"{0}"' -f $outputApk),
    '-aibtAndroidEvidence', ('"{0}"' -f $rawEvidence),
    '-logFile', ('"{0}"' -f $rawLog)
)

try {
    $process = Start-Process -FilePath $unityExecutable -ArgumentList $arguments -Wait -PassThru -NoNewWindow
    if ($process.ExitCode -ne 0) {
        throw "Unity Android build failed with exit code $($process.ExitCode). See '$rawLog'."
    }
    if (-not (Test-Path -LiteralPath $outputApk -PathType Leaf)) {
        throw "Unity exited successfully but did not produce '$outputApk'."
    }
    if (-not (Test-Path -LiteralPath $rawEvidence -PathType Leaf)) {
        throw "Unity exited successfully but did not produce '$rawEvidence'."
    }

    $apkEntries = @(& tar -tf $outputApk)
    if ($LASTEXITCODE -ne 0) {
        throw 'The produced APK could not be inspected with the system tar command.'
    }
    $nativeLibraries = @($apkEntries | Where-Object { $_ -like 'lib/*' })
    if ($nativeLibraries -notcontains 'lib/arm64-v8a/libil2cpp.so' -or
        $nativeLibraries -notcontains 'lib/arm64-v8a/lib_burst_generated.so') {
        throw 'The APK does not contain both IL2CPP and Burst native ARM64 libraries.'
    }
    if ($nativeLibraries | Where-Object { $_ -notlike 'lib/arm64-v8a/*' }) {
        throw 'The APK contains a native architecture other than ARM64.'
    }

    $evidence = Get-Content -LiteralPath $rawEvidence -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($evidence.result -ne 'Succeeded' -or
        $evidence.scriptingBackend -ne 'IL2CPP' -or
        $evidence.architectures -ne 'ARM64' -or
        -not $evidence.burstEnabled) {
        throw 'Android evidence does not prove a successful IL2CPP ARM64 build with Burst enabled.'
    }

    Write-Output "Android IL2CPP ARM64 build passed. APK: '$outputApk'. Evidence: '$rawEvidence'."
}
finally {
    if (Test-Path -LiteralPath $generatedRoot -PathType Container) {
        Remove-Item -LiteralPath $generatedRoot -Recurse -Force
    }
    $generatedMeta = $generatedRoot + '.meta'
    if (Test-Path -LiteralPath $generatedMeta -PathType Leaf) {
        Remove-Item -LiteralPath $generatedMeta -Force
    }
}
