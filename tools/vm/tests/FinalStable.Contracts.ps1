$ErrorActionPreference='Stop'
Set-StrictMode -Version 2
$helper=Join-Path $PSScriptRoot '..\Final-Stable.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($helper,[ref]$tokens,[ref]$errors)
if (@($errors).Count) {throw ($errors.Message -join '; ')}
$definition=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-StableVersion'},$true)
if (-not $definition) {throw 'Missing stable runtime guard.'}
. ([scriptblock]::Create($definition.Extent.Text))
$scanGuard=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-DefenderScanComplete'},$true)
if (-not $scanGuard) {throw 'Missing scan completion guard.'}
. ([scriptblock]::Create($scanGuard.Extent.Text))
function Event([int]$id,[string]$scanId) {
    [pscustomobject]@{Id=$id;Xml=('<Event><EventData><Data Name="Scan ID">'+$scanId+'</Data></EventData></Event>')}
}
$start=Event 1000 'one';$end=Event 1001 'one'
$null=Assert-DefenderScanComplete @($start,$end) ''
$scanCases=@(
    @{Events=@();Error=''},
    @{Events=@($start,$end);Error='query failed'},
    @{Events=@($start);Error=''},
    @{Events=@($end);Error=''},
    @{Events=@($start,(Event 1001 'other'));Error=''},
    @{Events=@($start,$end,(Event 1002 'one'));Error=''},
    @{Events=@((Event 1000 ''),$end);Error=''},
    @{Events=@([pscustomobject]@{Id=1000;Xml='invalid'});Error=''}
)
foreach($case in $scanCases){$rejected=$false;try{$null=Assert-DefenderScanComplete $case.Events $case.Error}catch{$rejected=$true};if(-not $rejected){throw 'Incomplete/broken Defender scan evidence accepted.'}}
$candidate=[pscustomobject]@{Head=('a'*40)}
$value=[pscustomobject]@{Version='1.0.3';FileVersion='1.0.3.9000';MsiVersion='1.0.3999';IsTest=$false;InformationalVersion=('1.0.3+'+('a'*40))}
Assert-StableVersion $value $candidate
foreach ($field in @('Version','FileVersion','MsiVersion','IsTest','InformationalVersion')) {
    $saved=$value.$field
    $value.$field=if ($field -eq 'IsTest') {$true} else {'wrong'}
    $rejected=$false
    try {Assert-StableVersion $value $candidate} catch {$rejected=$true}
    if (-not $rejected) {throw ('Invalid stable identity passed: '+$field)}
    $value.$field=$saved
}
# Exercise the actual workflow's version block in child PS5 processes; only
# temporary environment files are written, never GitHub/artifact/product APIs.
$workflow=Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\..\..\.github\workflows\ci.yml') -Raw
$block=[regex]::Match($workflow,'(?s)- name: Release version \(D-024\).*?run: \|\r?\n(?<code>.*?)(?=\r?\n      - name:)')
if (-not $block.Success) {throw 'Workflow version block not found.'}
$code=($block.Groups['code'].Value -split '\r?\n' | ForEach-Object {$_ -replace '^          ',''}) -join "`r`n"
$temp=Join-Path ([IO.Path]::GetTempPath()) ('StableVersionContract-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$scriptPath=Join-Path $temp 'version.ps1'
$code | Set-Content -LiteralPath $scriptPath -Encoding UTF8
$names=@('GITHUB_EVENT_NAME','GITHUB_ENV','WINTOOL_REQUESTED_TAG','WINTOOL_MANUAL_STABLE','WINTOOL_MANUAL_RC')
$savedEnv=@{};foreach($name in $names){$savedEnv[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try {
    $cases=@(
        @{Name='stable';Tag='v1.0.3';Stable='v1.0.3';Rc='';Event='workflow_dispatch';Exit=0;Version='1.0.3.9000'},
        @{Name='rc';Tag='v1.0.3-rc.8';Stable='';Rc='v1.0.3-rc.8';Event='workflow_dispatch';Exit=0;Version='1.0.3.3008'},
        @{Name='both';Tag='v1.0.3';Stable='v1.0.3';Rc='v1.0.3';Event='workflow_dispatch';Exit=1;Version=''},
        @{Name='badstable';Tag='v1.0.3-rc.8';Stable='v1.0.3-rc.8';Rc='';Event='workflow_dispatch';Exit=1;Version=''},
        @{Name='badrc';Tag='v1.0.3';Stable='';Rc='v1.0.3';Event='workflow_dispatch';Exit=1;Version=''},
        @{Name='release';Tag='v1.0.3';Stable='';Rc='';Event='push';Exit=0;Version='1.0.3.9000'}
    )
    foreach($case in $cases){
        $env:GITHUB_EVENT_NAME=$case.Event;$env:GITHUB_ENV=Join-Path $temp ($case.Name+'.env')
        $env:WINTOOL_REQUESTED_TAG=$case.Tag;$env:WINTOOL_MANUAL_STABLE=$case.Stable;$env:WINTOOL_MANUAL_RC=$case.Rc
        $process=Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "'+$scriptPath+'"') -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $temp ($case.Name+'.out')) -RedirectStandardError (Join-Path $temp ($case.Name+'.err'))
        if($process.ExitCode -ne $case.Exit){throw ('Unexpected version validation exit: '+$case.Name)}
        if($case.Exit -eq 0){if((Get-Content -LiteralPath $env:GITHUB_ENV -Raw) -notmatch ('WINTOOL_FILE_VERSION='+[regex]::Escape($case.Version))){throw ('Wrong version mapping: '+$case.Name)}}
        elseif(Test-Path -LiteralPath $env:GITHUB_ENV){throw 'Rejected input wrote version environment.'}
    }
} finally {foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$savedEnv[$name],'Process')}}
Write-Host 'Stable preparation: 6 runtime, 9 Defender evidence, 6 workflow version cases passed under PS5.1; no product/security/GitHub mutation.'
