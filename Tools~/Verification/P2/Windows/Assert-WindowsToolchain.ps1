<#
.SYNOPSIS
    Read-only P2-022 preflight for the Windows x64 IL2CPP/Burst Player toolchain.

.DESCRIPTION
    Detects the MSVC x64 compiler and Windows SDK that Unity IL2CPP requires before
    a Windows Player build is attempted. The detection authority is identical to the
    post-build environment snapshot taken by
    'Benchmarks~/Phase2/Dispatch/Player/Run-GeneratedDispatchPlayerAot.ps1', so a
    passing preflight means that harness can record a complete environment block
    instead of failing with ToolchainNotFoundException after a long IL2CPP stage.

    The script never installs, modifies, or elevates anything. It only reads vswhere
    output, the filesystem, and the Windows SDK registry key.

.OUTPUTS
    A single stable marker line:
        AIBT_P2_022_TOOLCHAIN_OK|msvc=<version>|sdk=<version>
        AIBT_P2_022_TOOLCHAIN_MISSING|<reason>[; <reason>...]

    Exit code 0 when the toolchain is complete and 1 when a required component is
    missing. A malformed invocation still throws.

.NOTES
    Generated reports are machine-local diagnostics and must not be committed.
    Write them under the ignored 'Tools~/Verification/TestResults/' directory.
#>
[CmdletBinding()]
param(
    [string] $ReportPath,
    [version] $MinimumWindowsSdkVersion = '10.0.19041',
    [switch] $Quiet
)

$ErrorActionPreference = 'Stop'

function Write-Status {
    param([string] $Message)

    if (-not $Quiet) {
        Write-Host $Message
    }
}

$osArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
if ($osArchitecture -eq 'Arm64') {
    Write-Status 'Note: this host reports an ARM64 operating system. The P2-022 baseline requires the x64 MSVC toolchain.'
}

$missing = [Collections.Generic.List[string]]::new()

$vswherePath = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $vswherePath -PathType Leaf)) {
    $vswherePath = $null
    $missing.Add('vswhere.exe is absent, so no Visual Studio 2022 installer metadata exists on this host')
}

$visualStudioPath = $null
if ($vswherePath) {
    $detectedPath = & $vswherePath `
        -latest `
        -products * `
        -version '[17.0,18.0)' `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath
    if ([string]::IsNullOrWhiteSpace($detectedPath)) {
        $missing.Add('no Visual Studio 2022 installation provides Microsoft.VisualStudio.Component.VC.Tools.x86.x64')
    }
    else {
        $visualStudioPath = ($detectedPath | Select-Object -First 1).Trim()
    }
}

$msvcCompiler = $null
if ($visualStudioPath) {
    $msvcRoot = Join-Path $visualStudioPath 'VC\Tools\MSVC'
    if (Test-Path -LiteralPath $msvcRoot -PathType Container) {
        $msvcCompiler = Get-ChildItem -LiteralPath $msvcRoot -Filter cl.exe -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object FullName -match '\\bin\\Hostx64\\x64\\cl\.exe$' |
            Sort-Object FullName -Descending |
            Select-Object -First 1
    }
    if (-not $msvcCompiler) {
        $missing.Add('the x64 hosted x64 compiler bin\Hostx64\x64\cl.exe is absent from the detected Visual Studio installation')
    }
}

$sdkRegistry = Get-ItemProperty `
    'HKLM:\SOFTWARE\Wow6432Node\Microsoft\Microsoft SDKs\Windows\v10.0' `
    -ErrorAction SilentlyContinue
$sdkRoot = $null
$sdkRegistryVersion = $null
if ($sdkRegistry) {
    $sdkRoot = $sdkRegistry.InstallationFolder
    $sdkRegistryVersion = $sdkRegistry.ProductVersion
}
if (-not $sdkRoot) {
    $missing.Add('the Windows 10/11 SDK registry key HKLM:\SOFTWARE\Wow6432Node\Microsoft\Microsoft SDKs\Windows\v10.0 is absent')
}

$sdkVersions = @()
$selectedSdkVersion = $null
if ($sdkRoot -and (Test-Path -LiteralPath $sdkRoot -PathType Container)) {
    $includeRoot = Join-Path $sdkRoot 'Include'
    $libRoot = Join-Path $sdkRoot 'Lib'
    if (Test-Path -LiteralPath $includeRoot -PathType Container) {
        $sdkVersions = @(
            Get-ChildItem -LiteralPath $includeRoot -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
                ForEach-Object { $_.Name } |
                Sort-Object { [version]$_ }
        )
    }

    $descending = @($sdkVersions | Sort-Object { [version]$_ } -Descending)
    foreach ($candidate in $descending) {
        if ([version]$candidate -lt $MinimumWindowsSdkVersion) {
            continue
        }

        $required = @(
            (Join-Path $includeRoot (Join-Path $candidate 'um\windows.h')),
            (Join-Path $includeRoot (Join-Path $candidate 'ucrt\stdio.h')),
            (Join-Path $libRoot (Join-Path $candidate 'um\x64\kernel32.lib')),
            (Join-Path $libRoot (Join-Path $candidate 'ucrt\x64\libucrt.lib'))
        )

        $complete = $true
        foreach ($path in $required) {
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                $complete = $false
                break
            }
        }

        if ($complete) {
            $selectedSdkVersion = $candidate
            break
        }
    }

    if (-not $selectedSdkVersion) {
        $missing.Add("no installed Windows SDK is both at least $MinimumWindowsSdkVersion and complete with the x64 headers and libraries IL2CPP links against")
    }
}
elseif ($sdkRoot) {
    $missing.Add("the Windows SDK registry key points at a missing directory: $sdkRoot")
}

$passed = $missing.Count -eq 0
$msvcCompilerPath = $null
$msvcFileVersion = $null
if ($msvcCompiler) {
    $msvcCompilerPath = $msvcCompiler.FullName
    $msvcFileVersion = $msvcCompiler.VersionInfo.FileVersion
}

$report = [ordered]@{
    schema = 'aibt-p2-022-windows-toolchain-preflight-v1'
    observedOn = (Get-Date).ToUniversalTime().ToString('o')
    passed = $passed
    minimumWindowsSdkVersion = $MinimumWindowsSdkVersion.ToString()
    osArchitecture = $osArchitecture
    vswherePath = $vswherePath
    visualStudioInstallationPath = $visualStudioPath
    msvcCompiler = $msvcCompilerPath
    msvcFileVersion = $msvcFileVersion
    windowsSdkRoot = $sdkRoot
    windowsSdkRegistryVersion = $sdkRegistryVersion
    windowsSdkInstalledVersions = $sdkVersions
    selectedWindowsSdkVersion = $selectedSdkVersion
    missing = @($missing)
}

if ($ReportPath) {
    $reportFullPath = [IO.Path]::GetFullPath($ReportPath)
    $reportDirectory = Split-Path -Parent $reportFullPath
    if ($reportDirectory -and -not (Test-Path -LiteralPath $reportDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    }
    [IO.File]::WriteAllText(
        $reportFullPath,
        ($report | ConvertTo-Json -Depth 4),
        [Text.UTF8Encoding]::new($false))
    Write-Status "Preflight report written to $reportFullPath"
}

if ($passed) {
    Write-Status "MSVC compiler: $msvcCompilerPath"
    Write-Status "Windows SDK:   $sdkRoot ($selectedSdkVersion)"
    Write-Output "AIBT_P2_022_TOOLCHAIN_OK|msvc=$msvcFileVersion|sdk=$selectedSdkVersion"
    exit 0
}

foreach ($reason in $missing) {
    Write-Status "Missing: $reason"
}

$installer = Join-Path $PSScriptRoot 'Install-WindowsToolchain.ps1'
Write-Status ''
Write-Status 'Install the missing components from an elevated shell with:'
Write-Status "    powershell -NoProfile -ExecutionPolicy Bypass -File '$installer' -Apply"
Write-Status 'See README.md in this directory for the full P2-022 runbook.'
Write-Output ('AIBT_P2_022_TOOLCHAIN_MISSING|' + ($missing -join '; '))
exit 1
