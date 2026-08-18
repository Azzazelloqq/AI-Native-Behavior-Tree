$ErrorActionPreference = 'Stop'

$spikeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$invariant = [Globalization.CultureInfo]::InvariantCulture
$vectors = (Get-Content -Raw (Join-Path $spikeRoot 'Fixtures/float32-canonical-vectors.json') | ConvertFrom-Json).vectors
$node = (Get-Command node -ErrorAction Stop).Source
$actualJson = (& $node (Join-Path $spikeRoot 'model-tests.mjs') '--print-float32') -join "`n"
if ($LASTEXITCODE -ne 0) {
    throw "Float32 producer failed with exit code $LASTEXITCODE."
}
$actual = $actualJson | ConvertFrom-Json

function Normalize-Decimal([string] $Value) {
    $text = $Value.ToLowerInvariant()
    if ($text.Contains('e')) {
        $parts = $text.Split('e')
        $mantissa = $parts[0].TrimEnd('0').TrimEnd('.')
        $exponent = [int]::Parse($parts[1], [Globalization.NumberStyles]::AllowLeadingSign, $invariant)
        return $mantissa + 'e' + $exponent.ToString($invariant)
    }
    if ($text.Contains('.')) { return $text.TrimEnd('0').TrimEnd('.') }
    return $text
}

function Get-EquivalentForms([string] $Value) {
    $normalized = Normalize-Decimal $Value
    if ($normalized -notmatch '^(-?)([0-9]+)(?:\.([0-9]*))?(?:e(-?[0-9]+))?$') {
        throw "Invalid independent decimal candidate $Value."
    }
    $sign = $Matches[1]
    $integer = $Matches[2]
    $fraction = $Matches[3]
    $exponent = if ($Matches[4]) { [int]$Matches[4] } else { 0 }
    $digits = $integer + $fraction
    $point = $integer.Length + $exponent
    $leading = 0
    while ($leading -lt $digits.Length -and $digits[$leading] -eq '0') { $leading++ }
    $digits = $digits.Substring($leading)
    $point -= $leading
    if ($digits.Length -eq 0) { return @('0') }
    $digits = $digits.TrimEnd('0')

    if ($point -le 0) { $plain = '0.' + ('0' * (-$point)) + $digits }
    elseif ($point -ge $digits.Length) { $plain = $digits + ('0' * ($point - $digits.Length)) }
    else { $plain = $digits.Substring(0, $point) + '.' + $digits.Substring($point) }

    $mantissa = if ($digits.Length -eq 1) { $digits } else { $digits[0] + '.' + $digits.Substring(1) }
    $scientificExponent = $point - 1
    $scientific = if ($scientificExponent -eq 0) { $mantissa } else { $mantissa + 'e' + $scientificExponent.ToString($invariant) }
    return @((Normalize-Decimal ($sign + $plain)), (Normalize-Decimal ($sign + $scientific))) | Select-Object -Unique
}

function Get-SingleBits([single] $Value) {
    return [BitConverter]::ToUInt32([BitConverter]::GetBytes($Value), 0)
}

function Get-IndependentShortest([uint32] $Bits) {
    if (($Bits -band 0x7fffffff) -eq 0) { return '0' }
    $value = [BitConverter]::ToSingle([BitConverter]::GetBytes($Bits), 0)
    $candidates = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for ($precision = 1; $precision -le 9; $precision++) {
        $formats = @(('G' + $precision), ('E' + ($precision - 1)))
        foreach ($format in $formats) {
            foreach ($candidate in (Get-EquivalentForms ($value.ToString($format, $invariant)))) {
                $parsed = [single]::Parse($candidate, [Globalization.NumberStyles]::Float, $invariant)
                if ((Get-SingleBits $parsed) -eq $Bits) { [void]$candidates.Add($candidate) }
            }
        }
    }
    $best = $null
    foreach ($candidate in $candidates) {
        $ordinalComparison = if ($null -eq $best) { -1 } else { [StringComparer]::Ordinal.Compare($candidate, $best) }
        if ($null -eq $best -or $candidate.Length -lt $best.Length -or ($candidate.Length -eq $best.Length -and $ordinalComparison -lt 0)) {
            $best = $candidate
        }
    }
    if ($null -eq $best) { throw "No independent round-trip candidate for bits $($Bits.ToString('x8'))." }
    return $best
}

foreach ($vector in $vectors) {
    $bits = [Convert]::ToUInt32($vector.bitsHex, 16)
    $oracle = Get-IndependentShortest $bits
    if ($oracle -cne $vector.expected) {
        throw "Pinned Float32 oracle mismatch for $($vector.bitsHex): expected $($vector.expected), independent $oracle."
    }
    $produced = $actual.($vector.bitsHex)
    if ($produced -cne $oracle) {
        throw "Float32 canonicalizer mismatch for $($vector.bitsHex): oracle $oracle, produced $produced."
    }
}

Write-Host "Independent Float32 oracle: PASS ($($vectors.Count) vectors)"
