$ErrorActionPreference = 'Stop'

$spikeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$node = (Get-Command node -ErrorAction Stop).Source
$json = (& $node (Join-Path $spikeRoot 'model-tests.mjs') '--print-streams') -join "`n"
if ($LASTEXITCODE -ne 0) {
    throw "Byte-stream producer failed with exit code $LASTEXITCODE."
}

$streams = $json | ConvertFrom-Json
$expected = Get-Content -Raw (Join-Path $spikeRoot 'Fixtures/expected-hashes.json') | ConvertFrom-Json

function Get-BytesFromHex([string] $Hex) {
    if (($Hex.Length % 2) -ne 0 -or $Hex -notmatch '^[0-9a-f]*$') {
        throw 'A pinned stream is not lowercase even-length hexadecimal.'
    }

    $bytes = New-Object byte[] ($Hex.Length / 2)
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        $bytes[$index] = [Convert]::ToByte($Hex.Substring($index * 2, 2), 16)
    }

    return $bytes
}

function Get-Sha256([string] $Hex) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash((Get-BytesFromHex $Hex)))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

$checks = @(
    @('agentSchemaBytes', 'agentSchema'),
    @('sharedSchemaBytes', 'sharedSchema'),
    @('agentLayoutBytes', 'agentLayout'),
    @('sharedLayoutBytes', 'sharedLayout'),
    @('compiledContentBytes', 'compiledContent'),
    @('compiledAgentOnlyBytes', 'compiledAgentOnly'),
    @('compiledSharedOnlyBytes', 'compiledSharedOnly'),
    @('losslessInt64AgentSchemaBytes', 'losslessInt64AgentSchema'),
    @('losslessInt64CompiledBytes', 'losslessInt64Compiled'),
    @('typedDefaultsCompiledBytes', 'typedDefaultsCompiled')
)

foreach ($check in $checks) {
    $actual = Get-Sha256 $streams.($check[0])
    $pin = $expected.($check[1])
    if ($actual -cne $pin) {
        throw "Independent SHA-256 mismatch for $($check[1]): expected $pin, actual $actual."
    }
}

Write-Host "Independent byte-stream pins: PASS ($($checks.Count) hashes)"
