<#
.SYNOPSIS
    Installs the MSVC x64 and Windows SDK components that P2-022 requires.

.DESCRIPTION
    Resolves the smallest install plan that satisfies
    'Assert-WindowsToolchain.ps1' and, only when '-Apply' is passed from an
    elevated shell, executes it.

    Three plans are possible:
      Modify        an existing Visual Studio 2022 installation is missing the
                    components, so the VS Installer modifies it in place;
      Winget        no Visual Studio 2022 exists, so Build Tools 2022 is installed
                    through winget with an explicit component override;
      Bootstrapper  the same install driven by a locally downloaded
                    vs_BuildTools.exe, for hosts without winget.

    This script is never invoked automatically by a verification or benchmark
    entrypoint. It changes machine state, so it is an explicit operator action
    listed in 'Planning~/USER_ACTIONS.md'.

.OUTPUTS
    A single stable marker line:
        AIBT_P2_022_TOOLCHAIN_INSTALL_PLANNED|<plan>
        AIBT_P2_022_TOOLCHAIN_ALREADY_PRESENT
        AIBT_P2_022_TOOLCHAIN_INSTALL_OK|<plan>
        AIBT_P2_022_TOOLCHAIN_INSTALL_FAILED|<reason>

    Exit code 0 on success or on a dry run, 1 otherwise.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File ./Install-WindowsToolchain.ps1
    Prints the resolved plan and changes nothing.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File ./Install-WindowsToolchain.ps1 -Apply
    Executes the plan. Must run elevated.
#>
[CmdletBinding()]
param(
    [switch] $Apply,
    [ValidateSet('Auto', 'Winget', 'Bootstrapper')]
    [string] $Method = 'Auto',
    [string] $BootstrapperPath,
    [string] $WindowsSdkComponentId = 'Microsoft.VisualStudio.Component.Windows11SDK.22621',
    [int] $TimeoutSeconds = 3600
)

$ErrorActionPreference = 'Stop'

$compilerComponentId = 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64'
$vcToolsWorkloadId = 'Microsoft.VisualStudio.Workload.VCTools'
$buildToolsPackageId = 'Microsoft.VisualStudio.2022.BuildTools'
$preflight = Join-Path $PSScriptRoot 'Assert-WindowsToolchain.ps1'

function Fail {
    param([string] $Reason)

    Write-Output "AIBT_P2_022_TOOLCHAIN_INSTALL_FAILED|$Reason"
    exit 1
}

function Test-Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-Plan {
    param(
        [string] $FilePath,
        [string] $ArgumentLine,
        [string] $Description
    )

    Write-Host "Running: $FilePath $ArgumentLine"
    $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentLine -PassThru -NoNewWindow
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Fail "$Description did not exit within $TimeoutSeconds seconds"
    }

    if ($process.ExitCode -eq 3010) {
        Write-Host "$Description completed and requests a reboot (exit code 3010). Reboot before rerunning the P2-022 harness."
        return
    }

    if ($process.ExitCode -ne 0) {
        Fail "$Description exited with code $($process.ExitCode)"
    }
}

if (-not (Test-Path -LiteralPath $preflight -PathType Leaf)) {
    Fail "the preflight script was not found: $preflight"
}

& $preflight -Quiet | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host 'The required MSVC and Windows SDK components are already present.'
    Write-Output 'AIBT_P2_022_TOOLCHAIN_ALREADY_PRESENT'
    exit 0
}

$vswherePath = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsInstallerPath = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\setup.exe"
$existingInstallation = $null
if (Test-Path -LiteralPath $vswherePath -PathType Leaf) {
    $candidate = & $vswherePath -products * -version '[17.0,18.0)' -property installationPath
    if (-not [string]::IsNullOrWhiteSpace($candidate)) {
        $existingInstallation = ($candidate | Select-Object -First 1).Trim()
    }
}

$plan = $null
$planFilePath = $null
$planArguments = $null
$componentArguments = "--add $compilerComponentId --add $WindowsSdkComponentId"
$installerFlags = '--quiet --wait --norestart --nocache'

if ($existingInstallation -and $Method -eq 'Auto') {
    if (-not (Test-Path -LiteralPath $vsInstallerPath -PathType Leaf)) {
        Fail "Visual Studio 2022 is installed at '$existingInstallation' but the VS Installer was not found at '$vsInstallerPath'"
    }
    $plan = 'Modify'
    $planFilePath = $vsInstallerPath
    $planArguments = "modify --installPath `"$existingInstallation`" $componentArguments $installerFlags"
}
elseif ($Method -eq 'Bootstrapper' -or $BootstrapperPath) {
    if (-not $BootstrapperPath) {
        Fail 'the Bootstrapper method requires -BootstrapperPath pointing at a downloaded vs_BuildTools.exe'
    }
    if (-not (Test-Path -LiteralPath $BootstrapperPath -PathType Leaf)) {
        Fail "the bootstrapper was not found: $BootstrapperPath"
    }
    $plan = 'Bootstrapper'
    $planFilePath = [IO.Path]::GetFullPath($BootstrapperPath)
    $planArguments = "$installerFlags --add $vcToolsWorkloadId $componentArguments"
}
else {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        Fail 'winget is unavailable; download vs_BuildTools.exe and rerun with -Method Bootstrapper -BootstrapperPath <file>'
    }
    $plan = 'Winget'
    $planFilePath = $winget.Source
    $planArguments = "install --id $buildToolsPackageId --exact --source winget " +
        '--accept-package-agreements --accept-source-agreements ' +
        "--override `"$installerFlags $componentArguments`""
}

Write-Host "Resolved plan: $plan"
Write-Host "  executable: $planFilePath"
Write-Host "  arguments:  $planArguments"
Write-Host "  components: $compilerComponentId, $WindowsSdkComponentId"

if (-not $Apply) {
    Write-Host ''
    Write-Host 'Dry run only. Nothing was installed or modified.'
    Write-Host 'Rerun the same command with -Apply from an elevated shell to execute this plan.'
    Write-Output "AIBT_P2_022_TOOLCHAIN_INSTALL_PLANNED|$plan"
    exit 0
}

if (-not (Test-Elevated)) {
    Fail 'the -Apply run requires an elevated shell; start PowerShell as Administrator and repeat the command'
}

Invoke-Plan -FilePath $planFilePath -ArgumentLine $planArguments -Description "the $plan install"

& $preflight
if ($LASTEXITCODE -ne 0) {
    Fail 'the install completed but the preflight still reports missing components; inspect the Visual Studio Installer log'
}

Write-Output "AIBT_P2_022_TOOLCHAIN_INSTALL_OK|$plan"
exit 0
