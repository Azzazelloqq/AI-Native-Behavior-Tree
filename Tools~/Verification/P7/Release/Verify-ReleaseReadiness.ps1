[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $TargetVersion,

    [Parameter()]
    [string] $RepositoryPath
)

# P7-015: local-first release-readiness validation, mirroring Verify-Static.ps1's own
# param/error-handling conventions exactly. Used identically by a human running this locally and by
# .github/workflows/release.yml's own "readiness" job -- per P0-005's own established rule ("Local
# verification entrypoints remain the source of CI commands"), inherited explicitly by this card.
# Never mutates package.json/CHANGELOG.md itself -- this is a dry-run check only; the actual
# version-bump/changelog-move/tag/publish steps are the workflow's own later, explicit "publish" job.

$ErrorActionPreference = 'Stop'
$RepositoryPath = if ([string]::IsNullOrWhiteSpace($RepositoryPath)) {
    (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
}
else {
    $RepositoryPath
}
$repositoryRoot = [System.IO.Path]::GetFullPath($RepositoryPath)

if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot 'package.json'))) {
    throw "AIBT repository was not found at '$repositoryRoot'."
}

function ConvertTo-SemVer {
    param([Parameter(Mandatory)][string] $Value, [Parameter(Mandatory)][string] $Description)

    if ($Value -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
        throw "$Description ('$Value') is not a valid semver X.Y.Z version."
    }

    return [PSCustomObject]@{
        Major = [int]$Matches[1]
        Minor = [int]$Matches[2]
        Patch = [int]$Matches[3]
        Text  = $Value
    }
}

function Compare-SemVer {
    param([Parameter(Mandatory)] $Left, [Parameter(Mandatory)] $Right)

    if ($Left.Major -ne $Right.Major) { return $Left.Major - $Right.Major }
    if ($Left.Minor -ne $Right.Minor) { return $Left.Minor - $Right.Minor }
    return $Left.Patch - $Right.Patch
}

$packagePath = Join-Path $repositoryRoot 'package.json'
$package = Get-Content -LiteralPath $packagePath -Raw -Encoding UTF8 | ConvertFrom-Json
$currentSemVer = ConvertTo-SemVer -Value $package.version -Description 'package.json version'
$targetSemVer = ConvertTo-SemVer -Value $TargetVersion -Description 'TargetVersion'

if ((Compare-SemVer -Left $targetSemVer -Right $currentSemVer) -le 0) {
    throw "TargetVersion '$($targetSemVer.Text)' must be strictly greater than package.json's current version '$($currentSemVer.Text)'."
}

$changelogPath = Join-Path $repositoryRoot 'CHANGELOG.md'
if (-not (Test-Path -LiteralPath $changelogPath)) {
    throw "CHANGELOG.md was not found at '$changelogPath'."
}
$changelog = Get-Content -LiteralPath $changelogPath -Raw -Encoding UTF8

$headingPattern = [regex]'(?m)^## \[([^\]]+)\]'
$headingMatches = $headingPattern.Matches($changelog)
if ($headingMatches.Count -eq 0 -or $headingMatches[0].Groups[1].Value -ne 'Unreleased') {
    throw "CHANGELOG.md's first '## [...]' heading must be '## [Unreleased]'."
}

foreach ($match in $headingMatches) {
    if ($match.Groups[1].Value -eq $targetSemVer.Text) {
        throw "CHANGELOG.md already has a '## [$($targetSemVer.Text)]' heading -- this version was already released."
    }
}

$unreleasedStart = $headingMatches[0].Index + $headingMatches[0].Length
$unreleasedEnd = if ($headingMatches.Count -gt 1) { $headingMatches[1].Index } else { $changelog.Length }
$unreleasedBody = $changelog.Substring($unreleasedStart, $unreleasedEnd - $unreleasedStart).Trim()

if ([string]::IsNullOrWhiteSpace($unreleasedBody)) {
    throw "CHANGELOG.md's '[Unreleased]' section is empty -- nothing to release."
}

$tagName = "v$($targetSemVer.Text)"
$existingTag = & git -C $repositoryRoot tag -l $tagName
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to list git tags.'
}
if (-not [string]::IsNullOrWhiteSpace($existingTag)) {
    throw "Git tag '$tagName' already exists."
}

Write-Output "Release readiness OK: $($currentSemVer.Text) -> $($targetSemVer.Text)."
Write-Output "Would move CHANGELOG.md's [Unreleased] section under '## [$($targetSemVer.Text)] - <date>'."
Write-Output "Would create git tag '$tagName'."
Write-Output '--- [Unreleased] section that would be released ---'
Write-Output $unreleasedBody
