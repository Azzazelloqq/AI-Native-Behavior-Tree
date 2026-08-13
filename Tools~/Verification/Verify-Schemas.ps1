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
$UvxPath = if ([string]::IsNullOrWhiteSpace($UvxPath)) { $env:AIBT_UVX_PATH } else { $UvxPath }

if ([string]::IsNullOrWhiteSpace($UvxPath)) {
    $uvxCommand = Get-Command 'uvx' -ErrorAction SilentlyContinue
    if ($null -eq $uvxCommand) {
        throw "JSON Schema validation requires uvx. Install uv, pass -UvxPath, or set AIBT_UVX_PATH."
    }

    $UvxPath = $uvxCommand.Source
}

$uvxExecutable = [System.IO.Path]::GetFullPath($UvxPath)
if (-not (Test-Path -LiteralPath $uvxExecutable -PathType Leaf)) {
    throw "uvx executable was not found at '$uvxExecutable'. Pass -UvxPath or set AIBT_UVX_PATH."
}

$schemaRoot = Join-Path $repositoryRoot 'Schemas~'
if (-not (Test-Path -LiteralPath $schemaRoot -PathType Container)) {
    throw "Schema directory was not found at '$schemaRoot'."
}

$schemaFiles = @(Get-ChildItem -LiteralPath $schemaRoot -File -Filter '*.schema.json' | Sort-Object Name)
if ($schemaFiles.Count -eq 0) {
    throw "No JSON Schema files were found under '$schemaRoot'."
}

$toolArguments = @('--from', 'check-jsonschema==0.38.0', 'check-jsonschema')
& $uvxExecutable @toolArguments '--check-metaschema' @($schemaFiles.FullName)
if ($LASTEXITCODE -ne 0) {
    throw 'One or more JSON Schema documents are invalid.'
}

$validationPairs = @(
    @{ Schema = 'work-item-index.schema.json'; Document = 'Planning~/work-items.json' },
    @{ Schema = 'policy.schema.json'; Document = '.aibt/policy.json' }
)

foreach ($pair in $validationPairs) {
    $schemaPath = Join-Path $schemaRoot $pair.Schema
    $documentPath = Join-Path $repositoryRoot $pair.Document
    if (-not (Test-Path -LiteralPath $schemaPath -PathType Leaf)) {
        throw "Required schema was not found at '$schemaPath'."
    }
    if (-not (Test-Path -LiteralPath $documentPath -PathType Leaf)) {
        throw "Schema-bound document was not found at '$documentPath'."
    }

    & $uvxExecutable @toolArguments '--schemafile' $schemaPath $documentPath
    if ($LASTEXITCODE -ne 0) {
        throw "'$documentPath' does not conform to '$schemaPath'."
    }
}

Write-Output "AIBT JSON Schema verification passed. Schemas: $($schemaFiles.Count)."
