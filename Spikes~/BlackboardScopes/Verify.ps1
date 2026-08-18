$ErrorActionPreference = 'Stop'

$spikeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$node = Get-Command node -ErrorAction Stop

& $node.Source (Join-Path $spikeRoot 'model-tests.mjs')
if ($LASTEXITCODE -ne 0) {
    throw "Blackboard scope model tests failed with exit code $LASTEXITCODE."
}

& powershell -ExecutionPolicy Bypass -File (Join-Path $spikeRoot 'Verify-Pins.ps1')
if ($LASTEXITCODE -ne 0) {
    throw "Independent byte-stream pin verification failed with exit code $LASTEXITCODE."
}

& powershell -ExecutionPolicy Bypass -File (Join-Path $spikeRoot 'Verify-Float32Oracle.ps1')
if ($LASTEXITCODE -ne 0) {
    throw "Independent Float32 oracle failed with exit code $LASTEXITCODE."
}
