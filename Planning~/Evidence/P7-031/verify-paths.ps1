param([string]$ProjectRoot = 'C:\UnityProjects\Modules')
$ErrorActionPreference = 'Stop'
$package = Join-Path $ProjectRoot 'Assets/AIBT'
$types = Add-Type -Path @(
    (Join-Path $package 'MCP/NodeDevelopment/StagingSlot.cs'),
    (Join-Path $package 'Runtime/Core/Identity/StableHash.cs')) -PassThru
$slot = $types[0].Assembly.GetType('AIBT.Mcp.NodeDevelopment.StagingSlot')
$flags = [System.Reflection.BindingFlags]'NonPublic,Static'
function Invoke-Slot([string]$Name, [string[]]$Arguments) {
    $slot.GetMethod($Name, $flags).Invoke($null, [object[]]$Arguments)
}
$fixture = Join-Path $ProjectRoot ('Temp/aibt-p731-paths-' + [guid]::NewGuid().ToString('N'))
$assets = Join-Path $fixture 'Assets'
$outside = Join-Path $fixture 'Outside'
$junction = Join-Path $assets 'Link'
$sourceJunction = $null
$checks = [System.Collections.Generic.List[string]]::new()
try {
    $node = Invoke-Slot 'WriteNode' @($assets, 'Node.cs', 'original')
    foreach ($destination in @('../Escape', '..\AssetsSibling\Node', '/Rooted', '\Rooted', 'C:Relative', 'C:\Absolute', '\\server\share', '.', 'Folder/../..', 'Folder. /Node')) {
        $rejected = $false
        try { Invoke-Slot 'MoveTo' @($assets, $destination) | Out-Null }
        catch { if ($_.Exception.InnerException -is [ArgumentException]) { $rejected = $true } else { throw } }
        if (!$rejected -or [IO.File]::ReadAllText($node) -ne 'original') { throw "Invalid rejection: $destination" }
        $checks.Add('rejected: ' + $destination)
    }
    New-Item -ItemType Directory -Path $outside | Out-Null
    New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
    $rejected = $false
    try { Invoke-Slot 'MoveTo' @($assets, 'Link/Node') | Out-Null }
    catch { if ($_.Exception.InnerException -is [ArgumentException]) { $rejected = $true } else { throw } }
    if (!$rejected -or [IO.Directory]::GetFileSystemEntries($outside).Length -ne 0 -or ![IO.File]::Exists($node)) { throw 'Junction boundary failed' }
    $checks.Add('destination junction rejected without source/target mutation')
    Remove-Item -LiteralPath $junction
    foreach ($destination in @('Generated/One/Node', 'Generated\Two\Node')) {
        Invoke-Slot 'WriteNode' @($assets, 'Node.cs', 'original') | Out-Null
        $moved = @(Invoke-Slot 'MoveTo' @($assets, $destination))
        if ($moved.Count -ne 2 -or [IO.File]::ReadAllText($moved[0]) -ne 'original') { throw 'Valid move failed' }
        $checks.Add('accepted: ' + $destination)
    }
    Invoke-Slot 'WriteNode' @($assets, 'Node.cs', 'original') | Out-Null
    $rejected = $false
    try { Invoke-Slot 'MoveTo' @($assets, 'Generated/One/Node') | Out-Null }
    catch { if ($_.Exception.InnerException -is [InvalidOperationException]) { $rejected = $true } else { throw } }
    if (!$rejected -or ![IO.File]::Exists($node)) { throw 'Existing destination changed' }
    $checks.Add('existing destination rejected')
    $stagingRoot = Invoke-Slot 'RootPath' @($assets)
    $sourceTarget = Join-Path $outside 'StagedSource'
    $resolvedFixture = [IO.Path]::GetFullPath($fixture) + [IO.Path]::DirectorySeparatorChar
    foreach ($candidate in @($stagingRoot, $sourceTarget)) {
        if (![IO.Path]::GetFullPath($candidate).StartsWith($resolvedFixture, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe source fixture move' }
    }
    Move-Item -LiteralPath $stagingRoot -Destination $sourceTarget
    $sourceJunction = $stagingRoot
    New-Item -ItemType Junction -Path $sourceJunction -Target $sourceTarget | Out-Null
    $rejected = $false
    try { Invoke-Slot 'MoveTo' @($assets, 'Generated/FromLink') | Out-Null }
    catch { if ($_.Exception.InnerException -is [ArgumentException]) { $rejected = $true } else { throw } }
    if (!$rejected -or [IO.Directory]::Exists((Join-Path $assets 'Generated/FromLink')) -or [IO.File]::ReadAllText((Join-Path $sourceTarget 'Node.cs')) -ne 'original') { throw 'Staging junction boundary failed' }
    $checks.Add('staging junction rejected without source/target mutation')
    [pscustomobject]@{ environment = 'PowerShell .NET; production C# path implementation, not Unity tests'; passed = $checks.Count; checks = $checks } | ConvertTo-Json -Depth 4
}
finally {
    $expectedParent = [IO.Path]::GetFullPath((Join-Path $ProjectRoot 'Temp')) + [IO.Path]::DirectorySeparatorChar
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (!$resolved.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path' }
    if (Test-Path -LiteralPath $junction) { Remove-Item -LiteralPath $junction }
    if ($sourceJunction -and (Test-Path -LiteralPath $sourceJunction)) { Remove-Item -LiteralPath $sourceJunction }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
