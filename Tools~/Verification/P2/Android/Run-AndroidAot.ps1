[CmdletBinding()]
param(
    [string] $UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe',
    [string] $OutputPath,
    [string] $IsolatedProjectPath,
    [int] $TimeoutSeconds = 3600
)

$ErrorActionPreference = 'Stop'

function FileSetHash([string] $Root) {
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $prefix = $rootPath + '\'
    $lines = @(Get-ChildItem -LiteralPath $rootPath -Recurse -File | Sort-Object FullName | ForEach-Object {
        $_.FullName.Substring($prefix.Length).Replace('\','/') + "`t" +
            (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    })
    $sha = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))))).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

$unity = [IO.Path]::GetFullPath($UnityPath)
$androidRoot = Join-Path (Split-Path $unity -Parent) 'Data\PlaybackEngines\AndroidPlayer'
foreach ($part in @('SDK','NDK','OpenJDK')) {
    if (!(Test-Path -LiteralPath (Join-Path $androidRoot $part) -PathType Container))
        { throw "Android module is incomplete: $part is missing." }
}
$sdkRoot = Join-Path $androidRoot 'SDK'
$ndkRoot = Join-Path $androidRoot 'NDK'
$jdkRoot = Join-Path $androidRoot 'OpenJDK'
$ndkProperties = Get-Content -Raw -LiteralPath (Join-Path $ndkRoot 'source.properties')
$ndkVersion = ([regex]::Match($ndkProperties, '(?m)^Pkg\.Revision\s*=\s*(.+)$')).Groups[1].Value.Trim()
$buildToolsDirectory = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'build-tools') -Directory |
    Sort-Object { try { [version]$_.Name } catch { [version]'0.0' } } -Descending | Select-Object -First 1
if (!$buildToolsDirectory -or [string]::IsNullOrWhiteSpace($ndkVersion)) { throw 'Android SDK/NDK version metadata is missing.' }
$javaVersionText = (& (Join-Path $jdkRoot 'bin\java.exe') -version 2>&1) -join "`n"
if ($LASTEXITCODE -ne 0 -or $javaVersionText -notmatch 'version\s+"([^"]+)"') { throw 'OpenJDK version detection failed.' }
$jdkVersion = $Matches[1]
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$aibtRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot '..\..\..\..'))
$playerHarness = Join-Path $aibtRoot 'Benchmarks~\Phase2\Dispatch\Player\Unity'
$analyzer = Join-Path $aibtRoot 'Analyzers\AIBT.CodeGen.dll'
$analyzerHash = (Get-FileHash -LiteralPath $analyzer -Algorithm SHA256).Hash.ToLowerInvariant()

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $scriptRoot ('Results\android-arm64-aot-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '.json')
}
$final = [IO.Path]::GetFullPath($OutputPath)
$resultRoot = Split-Path -Parent $final
$stem = [IO.Path]::GetFileNameWithoutExtension($final)
$apk = Join-Path $resultRoot ($stem + '.apk')
$raw = Join-Path $resultRoot ($stem + '.raw.json')
$log = Join-Path $resultRoot ($stem + '.log')
foreach($path in @($final,$apk,$raw,$log)){if(Test-Path -LiteralPath $path){throw "Output exists: $path"}}
New-Item -ItemType Directory -Path $resultRoot -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($IsolatedProjectPath)) {
    $IsolatedProjectPath = Join-Path ([IO.Path]::GetTempPath()) ('aibt-p2-023-android-' + [Guid]::NewGuid().ToString('N'))
}
$project = [IO.Path]::GetFullPath($IsolatedProjectPath)
if(Test-Path -LiteralPath $project){if(@(Get-ChildItem -LiteralPath $project -Force).Count){throw "Isolated project is not empty: $project"}}
$assets = Join-Path $project 'Assets'; $packages = Join-Path $project 'Packages'; $settings = Join-Path $project 'ProjectSettings'
New-Item -ItemType Directory -Path $assets,$packages,$settings -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $assets 'AIBT') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $aibtRoot 'Runtime') -Destination (Join-Path $assets 'AIBT\Runtime') -Recurse
Copy-Item -LiteralPath (Join-Path $aibtRoot 'Analyzers') -Destination (Join-Path $assets 'AIBT\Analyzers') -Recurse
Copy-Item -LiteralPath $playerHarness -Destination (Join-Path $assets 'AIBTP2GeneratedDispatchAndroid') -Recurse
$editor = Join-Path $assets 'AIBTP2GeneratedDispatchAndroid\Editor'
Copy-Item -LiteralPath (Join-Path $scriptRoot 'AndroidGeneratedDispatchBuild.cs.txt') -Destination (Join-Path $editor 'GeneratedDispatchPlayerAotBuild.cs') -Force
Copy-Item -LiteralPath (Join-Path (Split-Path (Split-Path $aibtRoot -Parent) -Parent) 'ProjectSettings\ProjectVersion.txt') -Destination (Join-Path $settings 'ProjectVersion.txt')
$manifest = [ordered]@{dependencies=[ordered]@{'com.unity.burst'='1.8.29';'com.unity.collections'='6.5.0';'com.unity.nuget.newtonsoft-json'='3.2.2';'com.unity.modules.jsonserialize'='1.0.0';'com.unity.modules.androidjni'='1.0.0'}} | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $packages 'manifest.json'),$manifest,[Text.UTF8Encoding]::new($false))

$sourceRuntimeHash = FileSetHash (Join-Path $aibtRoot 'Runtime')
$snapshotRuntimeHash = FileSetHash (Join-Path $assets 'AIBT\Runtime')
if($sourceRuntimeHash -ne $snapshotRuntimeHash){throw 'Runtime snapshot drifted.'}
$arguments = @('-batchmode','-nographics','-burst-enable-compilation','-projectPath',$project,'-executeMethod','AIBT.Benchmarks.Phase2.Dispatch.Player.Editor.GeneratedDispatchAndroidAotBuild.Build','-aibtP2AndroidOutput',$apk,'-aibtP2AndroidEvidence',$raw,'-logFile',$log,'-quit')
$process = Start-Process -FilePath $unity -ArgumentList (($arguments | ForEach-Object {if($_ -match '[\s"]'){'"'+($_ -replace '"','\"')+'"'}else{$_}})-join ' ') -PassThru -WindowStyle Hidden
if(!$process.WaitForExit($TimeoutSeconds*1000)){Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue;throw 'Android build timed out.'}
if($process.ExitCode -ne 0){throw "Android build failed with exit $($process.ExitCode). Log: $log"}
if(!(Test-Path -LiteralPath $apk) -or !(Test-Path -LiteralPath $raw)){throw 'Android build artifacts are missing.'}
$logText = Get-Content -Raw -LiteralPath $log
foreach($pattern in @('\berror CS\d{4}\b','\bBC\d{4}\b','managed fallback','A Native Collection has not been disposed')){if($logText -match $pattern){throw "Android log failed scan: $pattern"}}
if($logText -notmatch 'AIBT_P2_023_ANDROID_AOT_BUILD_OK\|'){throw 'Android success marker is missing.'}
$entries = @(& tar -tf $apk); if($LASTEXITCODE -ne 0){throw 'APK inspection failed.'}
$native = @($entries | Where-Object {$_ -like 'lib/*'})
if($native -notcontains 'lib/arm64-v8a/libil2cpp.so' -or $native -notcontains 'lib/arm64-v8a/lib_burst_generated.so'){throw 'APK lacks ARM64 IL2CPP/Burst libraries.'}
if($native | Where-Object {$_ -notlike 'lib/arm64-v8a/*'}){throw 'APK contains another native ABI.'}
$build = Get-Content -Raw -LiteralPath $raw | ConvertFrom-Json
if($build.result -ne 'Succeeded' -or $build.scriptingBackend -ne 'IL2CPP' -or $build.architecture -ne 'ARM64' -or $build.developmentBuild -or !$build.catalogUsable){throw 'Raw Android evidence is invalid.'}
$lock = Get-Content -Raw -LiteralPath (Join-Path $packages 'packages-lock.json') | ConvertFrom-Json
$burstVersion = $lock.dependencies.'com.unity.burst'.version
if ($burstVersion -ne '1.8.29') { throw "Android harness resolved unexpected Burst version $burstVersion." }
$evidence=[ordered]@{schema='aibt-p2-023-android-aot-v1';passed=$true;observedAtUtc=[DateTime]::UtcNow.ToString('o');unity=$unity;isolatedProject=$project;analyzerSha256=$analyzerHash;runtimeFileSetSha256=$sourceRuntimeHash;environment=[ordered]@{os=[Environment]::OSVersion.VersionString;processArchitecture=[Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString();logicalProcessors=[Environment]::ProcessorCount;unityVersion=(Get-Item $unity).VersionInfo.ProductVersion;burstVersion=$burstVersion;androidSdkRoot=$sdkRoot;androidBuildToolsVersion=$buildToolsDirectory.Name;androidNdkRoot=$ndkRoot;androidNdkVersion=$ndkVersion;openJdkRoot=$jdkRoot;openJdkVersion=$jdkVersion};build=$build;apk=[ordered]@{path=$apk;bytes=(Get-Item $apk).Length;sha256=(Get-FileHash $apk -Algorithm SHA256).Hash.ToLowerInvariant();nativeLibraries=$native};logs=[ordered]@{path=$log;sha256=(Get-FileHash $log -Algorithm SHA256).Hash.ToLowerInvariant();scan='clean CS/BC/fallback/native-leak'};claims=@('Android ARM64 IL2CPP/Burst build compatibility only','No device runtime, performance, battery, or store claim')}
[IO.File]::WriteAllText($final,($evidence|ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
Write-Output "P2-023 Android AOT passed: $final"
