[CmdletBinding()]
param(
    [Parameter()]
    [string] $RepositoryPath,

    [Parameter()]
    [string] $UvxPath
)

$ErrorActionPreference = 'Stop'
$RepositoryPath = if ([string]::IsNullOrWhiteSpace($RepositoryPath)) {
    (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
}
else {
    $RepositoryPath
}
$repositoryRoot = [System.IO.Path]::GetFullPath($RepositoryPath)

if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot 'package.json'))) {
    throw "AIBT repository was not found at '$repositoryRoot'."
}

& (Join-Path $PSScriptRoot 'Verify-Schemas.ps1') -RepositoryPath $repositoryRoot -UvxPath $UvxPath
if ($LASTEXITCODE -ne 0) {
    throw 'Schema verification failed.'
}

$jsonFiles = @(& git -C $repositoryRoot -c core.quotepath=false ls-files --cached --others --exclude-standard -- '*.json')
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate repository JSON files with Git.'
}

$jsonFiles | ForEach-Object {
    $jsonPath = Join-Path $repositoryRoot $_
    try {
        Get-Content -LiteralPath $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json | Out-Null
    }
    catch {
        throw "Invalid JSON '$jsonPath': $($_.Exception.Message)"
    }
}

$workItemsPath = Join-Path $repositoryRoot 'Planning~/work-items.json'
$workItems = Get-Content -LiteralPath $workItemsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$ids = @($workItems.items | ForEach-Object id)

if (($ids | Select-Object -Unique).Count -ne $ids.Count) {
    throw 'Planning~/work-items.json contains duplicate work-item IDs.'
}

foreach ($item in $workItems.items) {
    $cardPath = Join-Path $repositoryRoot $item.card
    if (-not (Test-Path -LiteralPath $cardPath)) {
        throw "Work item '$($item.id)' references missing card '$($item.card)'."
    }

    foreach ($dependency in $item.dependsOn) {
        if ($ids -notcontains $dependency) {
            throw "Work item '$($item.id)' references unknown dependency '$dependency'."
        }
    }
}

$visiting = @{}
$visited = @{}

function Visit-WorkItem {
    param([Parameter(Mandatory)][string] $Id)

    if ($visiting[$Id]) {
        throw "Work-item dependency cycle detected at '$Id'."
    }

    if ($visited[$Id]) {
        return
    }

    $visiting[$Id] = $true
    $item = $workItems.items | Where-Object id -EQ $Id
    foreach ($dependency in $item.dependsOn) {
        Visit-WorkItem -Id $dependency
    }

    $visiting.Remove($Id)
    $visited[$Id] = $true
}

foreach ($id in $ids) {
    Visit-WorkItem -Id $id
}

$brokenLinks = [System.Collections.Generic.List[string]]::new()
$markdownFiles = @(& git -C $repositoryRoot -c core.quotepath=false ls-files --cached --others --exclude-standard -- '*.md')
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate repository Markdown files with Git.'
}

$markdownFiles | ForEach-Object {
    $sourceFile = Get-Item -LiteralPath (Join-Path $repositoryRoot $_)
    $content = Get-Content -LiteralPath $sourceFile.FullName -Raw -Encoding UTF8

    [regex]::Matches($content, '\[[^\]]+\]\(([^)]+)\)') | ForEach-Object {
        $target = $_.Groups[1].Value
        if ($target -match '^(https?://|mailto:|#)' -or $target -match '^<') {
            return
        }

        $pathPart = ($target -split '#')[0]
        if ([string]::IsNullOrEmpty($pathPart)) {
            return
        }

        $resolvedPath = Join-Path $sourceFile.DirectoryName ([uri]::UnescapeDataString($pathPart))
        if (-not (Test-Path -LiteralPath $resolvedPath)) {
            $brokenLinks.Add("$($sourceFile.FullName): $target")
        }
    }
}

if ($brokenLinks.Count -gt 0) {
    throw "Broken local Markdown links:`n$($brokenLinks -join "`n")"
}

$package = Get-Content -LiteralPath (Join-Path $repositoryRoot 'package.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($package.name -ne 'com.azzazello.aibt') {
    throw "Unexpected package ID '$($package.name)'."
}

& git -C $repositoryRoot diff --check
if ($LASTEXITCODE -ne 0) {
    throw 'git diff --check failed.'
}

Write-Output "AIBT static verification passed. Work items: $($ids.Count)."
