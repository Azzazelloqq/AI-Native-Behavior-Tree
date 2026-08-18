[CmdletBinding()]
param(
    [string] $UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe',
    [Parameter(Mandatory)][string] $ProjectPath,
    [string] $OutputPath,
    [int] $TimeoutSeconds = 300
)

$ErrorActionPreference='Stop'
function SharedFileHash([string]$Path) {
    $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    $sha=[Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
}
$unity=[IO.Path]::GetFullPath($UnityPath);$project=[IO.Path]::GetFullPath($ProjectPath);$root=Split-Path -Parent $MyInvocation.MyCommand.Path
if([string]::IsNullOrWhiteSpace($OutputPath)){$OutputPath=Join-Path $root ('Results\allocation-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.json')}
$final=[IO.Path]::GetFullPath($OutputPath);$resultRoot=Split-Path -Parent $final;$stem=[IO.Path]::GetFileNameWithoutExtension($final);$xml=Join-Path $resultRoot ($stem+'.xml');$log=Join-Path $resultRoot ($stem+'.log')
foreach($p in @($final,$xml,$log)){if(Test-Path $p){throw "Output exists: $p"}};New-Item -ItemType Directory $resultRoot -Force|Out-Null
$args=@('-batchmode','-nographics','-projectPath',$project,'-runTests','-testPlatform','EditMode','-testFilter','AIBT.Tests.Runtime.NativeExecution.Allocation.NativeExecutionAllocationTests','-testResults',$xml,'-logFile',$log)
$line=($args|ForEach-Object{if($_ -match '[\s"]'){'"'+($_-replace'"','\"')+'"'}else{$_}})-join' '
$process=Start-Process $unity -ArgumentList $line -PassThru -WindowStyle Hidden;$deadline=(Get-Date).AddSeconds($TimeoutSeconds)
while((Get-Date)-lt$deadline -and !(Test-Path $xml)){Start-Sleep -Seconds 2}
if(!(Test-Path $xml)){Stop-Process $process.Id -Force -ErrorAction SilentlyContinue;throw 'Allocation tests timed out.'}
Start-Sleep -Seconds 1
if(!$process.HasExited){
    Stop-Process $process.Id -Force -ErrorAction SilentlyContinue
    Wait-Process -Id $process.Id -Timeout 10 -ErrorAction SilentlyContinue
}
[xml]$results=Get-Content -Raw $xml;if([int]$results.'test-run'.failed-ne0 -or [int]$results.'test-run'.passed-ne3){throw "Allocation tests failed: $xml"}
$text=(Get-Content -Raw $xml)+"`n"+(Get-Content -Raw $log);$matches=[regex]::Matches($text,'AIBT_P2_021_SAMPLE\|policy=([^|<\r\n]+)\|index=(\d+)\|gcEvents=(\d+)')
$samples=@();$seen=@{};foreach($match in $matches){$key=$match.Groups[1].Value+':'+$match.Groups[2].Value;if($seen[$key]){continue};$seen[$key]=$true;$samples+=[ordered]@{policy=$match.Groups[1].Value;index=[int]$match.Groups[2].Value;gcEvents=[long]$match.Groups[3].Value}}
$measured=@($samples|Where-Object policy -ne 'ControlledCanary');$canary=@($samples|Where-Object policy -eq 'ControlledCanary')
if($measured.Count-ne12 -or @($measured|Where-Object gcEvents -ne 0).Count-ne0 -or $canary.Count-ne1 -or $canary[0].gcEvents-lt1){throw "Raw allocation markers are incomplete or invalid. samples=$($samples.Count)"}
foreach($pattern in @('\berror CS\d{4}\b','\bBC\d{4}\b','A Native Collection has not been disposed','Found [1-9]\d* leak')){if($text-match$pattern){throw "Allocation log failed scan: $pattern"}}
$evidence=[ordered]@{schema='aibt-p2-021-allocation-gate-v1';passed=$true;observedAtUtc=[DateTime]::UtcNow.ToString('o');unity=$unity;project=$project;tests=[ordered]@{total=3;passed=3;failed=0};samples=$samples;claims=@('Zero GC.Alloc events only inside the 12 initialized measured windows','Success, abort, fault, restart, capacity rejection, and final disposal completed under Unity native leak detection','Initialization, compilation, reference nodes, host materialization, and unmeasured platforms are excluded');artifacts=[ordered]@{xml=$xml;xmlSha256=SharedFileHash $xml;log=$log;logSha256=SharedFileHash $log}}
[IO.File]::WriteAllText($final,($evidence|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false));Write-Output "P2-021 allocation gate passed: $final"
