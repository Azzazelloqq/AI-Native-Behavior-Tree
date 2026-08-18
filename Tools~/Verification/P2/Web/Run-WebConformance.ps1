[CmdletBinding()]
param(
    [string] $UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe',
    [string] $OutputPath,
    [string] $IsolatedProjectPath,
    [int] $BuildTimeoutSeconds = 3600,
    [int] $Port = 18124
)

$ErrorActionPreference = 'Stop'
function FileSetHash([string]$Root) {
    $rootPath=[IO.Path]::GetFullPath($Root).TrimEnd('\');$prefix=$rootPath+'\'
    $lines=@(Get-ChildItem -LiteralPath $rootPath -Recurse -File|Sort-Object FullName|ForEach-Object{$_.FullName.Substring($prefix.Length).Replace('\','/')+"`t"+(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()})
    $sha=[Security.Cryptography.SHA256]::Create();try{([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines-join"`n"))))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
}

$unity=[IO.Path]::GetFullPath($UnityPath);$scriptRoot=Split-Path -Parent $MyInvocation.MyCommand.Path
$aibtRoot=[IO.Path]::GetFullPath((Join-Path $scriptRoot '..\..\..\..'))
$webModule=Join-Path (Split-Path $unity -Parent) 'Data\PlaybackEngines\WebGLSupport'
if(!(Test-Path -LiteralPath $webModule -PathType Container)){throw 'WebGL Build Support is missing.'}
$chrome='C:\Program Files\Google\Chrome\Application\chrome.exe';$firefox='C:\Program Files\Mozilla Firefox\firefox.exe'
if(!(Test-Path $chrome) -or !(Test-Path $firefox)){throw 'Chrome and Firefox are both required.'}
if([string]::IsNullOrWhiteSpace($OutputPath)){$OutputPath=Join-Path $scriptRoot ('Results\web-conformance-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.json')}
$final=[IO.Path]::GetFullPath($OutputPath);$resultRoot=Split-Path -Parent $final;$stem=[IO.Path]::GetFileNameWithoutExtension($final)
$log=Join-Path $resultRoot ($stem+'.build.log');$raw=Join-Path $resultRoot ($stem+'.build.raw.json');$chromeResult=Join-Path $resultRoot ($stem+'.chrome.json');$firefoxResult=Join-Path $resultRoot ($stem+'.firefox.json')
foreach($p in @($final,$log,$raw,$chromeResult,$firefoxResult)){if(Test-Path $p){throw "Output exists: $p"}};New-Item -ItemType Directory $resultRoot -Force|Out-Null
if([string]::IsNullOrWhiteSpace($IsolatedProjectPath)){$IsolatedProjectPath=Join-Path ([IO.Path]::GetTempPath()) ('aibt-p2-024-web-'+[Guid]::NewGuid().ToString('N'))}
$project=[IO.Path]::GetFullPath($IsolatedProjectPath);if(Test-Path $project){if(@(Get-ChildItem $project -Force).Count){throw "Project is not empty: $project"}}
$assets=Join-Path $project 'Assets';$packages=Join-Path $project 'Packages';$settings=Join-Path $project 'ProjectSettings';New-Item -ItemType Directory $assets,$packages,$settings,(Join-Path $assets 'AIBT') -Force|Out-Null
Copy-Item (Join-Path $aibtRoot 'Runtime') (Join-Path $assets 'AIBT\Runtime') -Recurse
Copy-Item (Join-Path $aibtRoot 'Analyzers') (Join-Path $assets 'AIBT\Analyzers') -Recurse
$harnessSource=Join-Path $aibtRoot 'Benchmarks~\Phase2\Dispatch\Player\Unity';$harness=Join-Path $assets 'AIBTP2GeneratedDispatchWeb';Copy-Item $harnessSource $harness -Recurse
Copy-Item (Join-Path $scriptRoot 'WebGeneratedDispatchBuild.cs.txt') (Join-Path $harness 'Editor\GeneratedDispatchPlayerAotBuild.cs') -Force
$sourceProject=Split-Path (Split-Path $aibtRoot -Parent) -Parent;Copy-Item (Join-Path $sourceProject 'ProjectSettings\ProjectVersion.txt') (Join-Path $settings 'ProjectVersion.txt')
$manifest=[ordered]@{dependencies=[ordered]@{'com.unity.burst'='1.8.29';'com.unity.collections'='6.5.0';'com.unity.nuget.newtonsoft-json'='3.2.2';'com.unity.modules.jsonserialize'='1.0.0'}}|ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $packages 'manifest.json'),$manifest,[Text.UTF8Encoding]::new($false))
$runtimeHash=FileSetHash (Join-Path $aibtRoot 'Runtime');if($runtimeHash -ne (FileSetHash (Join-Path $assets 'AIBT\Runtime'))){throw 'Runtime snapshot drifted.'}
$buildRoot=Join-Path $project 'Build\WebGL'
$args=@('-batchmode','-nographics','-burst-enable-compilation','-projectPath',$project,'-executeMethod','AIBT.Benchmarks.Phase2.Dispatch.Player.Editor.GeneratedDispatchWebBuild.Build','-aibtP2WebOutput',$buildRoot,'-aibtP2WebEvidence',$raw,'-logFile',$log,'-quit')
$argLine=($args|ForEach-Object{if($_ -match '[\s"]'){'"'+($_-replace'"','\"')+'"'}else{$_}})-join' '
$build=Start-Process $unity -ArgumentList $argLine -PassThru -WindowStyle Hidden
if(!$build.WaitForExit($BuildTimeoutSeconds*1000)){Stop-Process $build.Id -Force;throw 'WebGL build timed out.'};if($build.ExitCode -ne 0){throw "WebGL build failed: $log"}
$logText=Get-Content -Raw $log;foreach($pattern in @('\berror CS\d{4}\b','\bBC\d{4}\b','managed fallback','A Native Collection has not been disposed')){if($logText-match$pattern){throw "Web build log failed scan: $pattern"}}
if($logText-notmatch'AIBT_P2_024_WEB_BUILD_OK\|'){throw 'Web build marker missing.'}
$buildEvidence=Get-Content -Raw $raw|ConvertFrom-Json;if($buildEvidence.result-ne'Succeeded'-or$buildEvidence.scriptingBackend-ne'IL2CPP'-or$buildEvidence.developmentBuild-or!$buildEvidence.catalogUsable){throw 'Web build evidence invalid.'}

$npmRoot=Join-Path $project 'BrowserTools';New-Item -ItemType Directory $npmRoot -Force|Out-Null
& npm install --prefix $npmRoot --no-audit --no-fund --silent selenium-webdriver@4.35.0
if($LASTEXITCODE-ne0){throw 'selenium-webdriver installation failed.'}
$env:NODE_PATH=Join-Path $npmRoot 'node_modules'
$server=Start-Process node -ArgumentList @((Join-Path $scriptRoot 'static-server.js'),$buildRoot,$Port) -PassThru -WindowStyle Hidden
try{
    Start-Sleep -Seconds 2;$url="http://127.0.0.1:$Port/"
    & node (Join-Path $scriptRoot 'browser-probe.js') chrome $url $chromeResult;if($LASTEXITCODE-ne0){throw 'Chrome conformance failed.'}
    & node (Join-Path $scriptRoot 'browser-probe.js') firefox $url $firefoxResult;if($LASTEXITCODE-ne0){throw 'Firefox conformance failed.'}
}finally{Stop-Process $server.Id -Force -ErrorAction SilentlyContinue}
$chromeEvidence=Get-Content -Raw $chromeResult|ConvertFrom-Json;$firefoxEvidence=Get-Content -Raw $firefoxResult|ConvertFrom-Json
$analyzerHash=(Get-FileHash (Join-Path $aibtRoot 'Analyzers\AIBT.CodeGen.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
$files=@(Get-ChildItem $buildRoot -Recurse -File);$buildBytes=($files|Measure-Object Length -Sum).Sum
$evidence=[ordered]@{schema='aibt-p2-024-web-conformance-v1';passed=$true;observedAtUtc=[DateTime]::UtcNow.ToString('o');unity=$unity;isolatedProject=$project;analyzerSha256=$analyzerHash;runtimeFileSetSha256=$runtimeHash;build=$buildEvidence;artifact=[ordered]@{path=$buildRoot;bytes=$buildBytes;fileCount=$files.Count;fileSetSha256=FileSetHash $buildRoot};browsers=@($chromeEvidence,$firefoxEvidence);policy=[ordered]@{publicImmediate='SingleThreadImmediate';publicBudgeted='SingleThreadBudgeted';jobsExposed=$false;internalGeneratedEntry='ExecuteImmediate';decision='Use the unmanaged direct single-thread path; do not expose Web jobs or worker parallelism.'};claims=@('Desktop Chrome and Firefox only','No Safari, mobile Web, worker-parallel, or device-performance claim')}
[IO.File]::WriteAllText($final,($evidence|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false));Write-Output "P2-024 Web conformance passed: $final"
