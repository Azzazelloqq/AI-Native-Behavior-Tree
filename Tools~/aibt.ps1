# Bootstrap wrapper. For MCP stdio clients use the built CLI DLL directly (see cli.md).
$ErrorActionPreference = 'Stop'
$packageRoot = Split-Path $PSScriptRoot -Parent
$cliProject = Join-Path $packageRoot 'MCP~/Cli/AibtCli.csproj'
$buildOutput = & dotnet build $cliProject --nologo -v quiet 2>&1
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine(($buildOutput | Out-String))
    exit $LASTEXITCODE
}
& dotnet (Join-Path $packageRoot 'MCP~/Cli/bin/Debug/net10.0/aibt.dll') @args
exit $LASTEXITCODE
