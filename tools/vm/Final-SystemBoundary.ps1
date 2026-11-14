param([ValidateSet('Setup','StandardUser','Finish')][string]$Phase='Setup', [switch]$ConfirmDisposableVM, [switch]$ConfirmReviewedHelpers, [switch]$ConfirmOffline, [string]$SnapshotLabel)
$ErrorActionPreference='Stop'
if(-not $ConfirmDisposableVM -or -not $ConfirmReviewedHelpers -or -not $ConfirmOffline) { throw 'Reviewed helpers, clean disposable VM and disconnected VM network required.' }
if($PSVersionTable.PSEdition -ne 'Desktop' -or [IntPtr]::Size -ne 8) { throw 'Use64bit Windows PowerShell5.1.' }
if(@(Get-NetAdapter | Where-Object Status -eq 'Up').Count) { throw 'Disconnect VM network in the hypervisor; no adapter was changed.' }
$isAdmin=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if(($Phase -eq 'StandardUser') -eq $isAdmin) { throw 'Setup/Finish require administrator; StandardUser requires an actual standard account.' }
$lab=[IO.Path]::GetFullPath($PSScriptRoot)
$ev=Join-Path $lab 'Evidence'; $userEv=Join-Path $ev 'StandardUser'; $checkpoint=Join-Path $ev 'checkpoint.json'
$custom=Join-Path $lab 'WritableInstall'; $source=Join-Path $custom 'WinTool-by-PcNinja.exe'
$data=Join-Path $env:ProgramData 'PcNinja-WinTool'; $scheduler=Join-Path $data 'Scheduler'
$installed=Join-Path $env:ProgramFiles 'PcNinja\WinTool\WinTool-by-PcNinja.exe'
$rc=Join-Path $lab 'RC7\WinTool-by-PcNinja.exe'; $msi=Join-Path $lab 'RC7\WinTool-by-PcNinja.msi'; $baseline=Join-Path $lab 'Baseline\WinTool-by-PcNinja.msi'
$rcHash='8b0b7607290341b0b62c51635dcab2e2ce4d58bb425b61692053bd6afb40d954'
foreach($pair in @(@($rc,$rcHash),@($msi,'f93995c1761dc9f33731ece626880bc710aae1e9dd07a51d561c07ded36c3027'),@($baseline,'6f523806cbcf90636a2f1aaf4b57a2df6c62725588f4b0895427c415a70173a7'))) {
    if((Get-FileHash -LiteralPath $pair[0]).Hash -ne $pair[1]) { throw 'Pinned fixture mismatch; no installer started.' }
}
if(@(Get-Process -Name 'WinTool-by-PcNinja' -ErrorAction SilentlyContinue).Count) { throw 'Close idle WinTool first; never stop an active worker.' }
$env:WINTOOL_DATA_ROOT=$null; $env:WINTOOL_UPDATE_FEED=$null
$result=[ordered]@{Phase=$Phase;Result='FAIL';Steps=@();BinarySource='b58c0364d16d1079b7196ef83f434d4a04b60d8f';BinarySHA256=$rcHash;ConfirmedOffline=$true;NoNativeScanClaim=$false}
function OwnTasks { @(Get-ScheduledTask | Where-Object { $_.TaskName.StartsWith('WinTool by PcNinja',[StringComparison]::OrdinalIgnoreCase) }) }
function MainTask { Get-ScheduledTask -TaskPath '\PcNinja\' -TaskName 'WinTool by PcNinja' }
function MainXml { Export-ScheduledTask -TaskPath '\PcNinja\' -TaskName 'WinTool by PcNinja' }
function InvokeExe([string]$file,[string]$arguments,[string]$label,[int]$expected=0) {
    $p=Start-Process -FilePath $file -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $ev ($label+'-stdout.txt')) -RedirectStandardError (Join-Path $ev ($label+'-stderr.txt'))
    $result.Steps+=@{Name=$label;ExitCode=$p.ExitCode;Expected=$expected}
    if($p.ExitCode -ne $expected) { throw ($label+' unexpected exit '+$p.ExitCode) }
}
function Msi([string]$op,[string]$package,[string]$label,[string]$extra='') {
    InvokeExe 'msiexec.exe' ($op+' "'+$package+'" '+$extra+' /qn /norestart /L*v "'+(Join-Path $ev ($label+'-msi.log'))+'"') $label
}
function AssertTask([string]$target) {
    $task=MainTask
    if((OwnTasks).Count -ne 1 -or @($task.Actions).Count -ne 1 -or $task.Actions[0].Execute -ne $target -or $task.Actions[0].Arguments -ne 'update run --scheduled' -or $task.Principal.UserId -notin @('SYSTEM','S-1-5-18')) { throw 'Unexpected SYSTEM task definition.' }
    if($task.State -eq 'Running') { throw 'Task running; do not stop or modify it. Return evidence.' }
}
function SchedulerSnapshot {
    if(-not (Test-Path -LiteralPath $scheduler)){return 'ABSENT'}
    if((Get-Item -LiteralPath $scheduler).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked Scheduler fixture'}
    $rows=@(Get-ChildItem -LiteralPath $scheduler -Recurse -File | Sort-Object FullName | ForEach-Object {[pscustomobject]@{Path=$_.FullName.Substring($scheduler.Length);Hash=(Get-FileHash -LiteralPath $_.FullName).Hash}})
    ConvertTo-Json -InputObject $rows -Compress
}
function ImportStore {
    $app=[Reflection.Assembly]::LoadFrom($rc)
    $null=$app.GetType('WinTool.App.Infrastructure.EmbeddedAssemblies',$true).GetMethod('Register').Invoke($null,@())
    $assemblies=@{}
    foreach($name in @('WinTool.Core','WinTool.WindowsUpdate')) {
        $stream=$app.GetManifestResourceStream('WinTool.Embedded.'+$name+'.dll'); if(-not $stream){throw 'Missing embedded module'}
        $memory=New-Object IO.MemoryStream
        try {$stream.CopyTo($memory);$assemblies[$name]=[Reflection.Assembly]::Load($memory.ToArray())} finally {$stream.Dispose();$memory.Dispose()}
    }
    $script:core=$assemblies['WinTool.Core'];$script:wu=$assemblies['WinTool.WindowsUpdate']
    $logType=$core.GetType('WinTool.Core.Diagnostics.AppLog',$true)
    $argsForLog=New-Object 'object[]' 2; $argsForLog[0]=[string](Join-Path $ev 'FixtureLogs'); $argsForLog[1]=$null
    $logger=$logType.GetConstructor([Type[]]@([string],[Func[DateTime]])).Invoke($argsForLog)
    $logType.GetProperty('Current').SetValue($null,$logger,$null)
    $storeType=$wu.GetType('WinTool.WindowsUpdate.Storage.WindowsUpdateStore',$true)
    $argsForStore=New-Object 'object[]' 1; $argsForStore[0]=[string](Join-Path $data 'WindowsUpdate')
    $script:store=$storeType.GetConstructor([Type[]]@([string])).Invoke($argsForStore)
}
function IsProtected([string]$path) { [bool]$core.GetType('WinTool.Core.Security.ProtectedMachineData',$true).GetMethod('IsProtectedExecutable').Invoke($null,[object[]]@($path)) }
function SeedCooldown {
    # A synthetic saved timestamp is TEST INPUT, not evidence of a successful run.
    $lease=$core.GetType('WinTool.Core.Operations.OperationGate',$true).GetMethod('EnterWindows').Invoke($null,@())
    try {
        $settings=$store.LoadSettings();$settings.MinimumCooldownMinutes=1440;$settings.AutoRetryEnabled=$false;$store.SaveSettings($settings)
        $state=$store.LoadState(); $state.LastRunFinished=[DateTime]::Now.ToString('s',[Globalization.CultureInfo]::InvariantCulture); $state.LastResult='Cancelled';$state.LastMessage='SYSTEM-boundary fixture cooldown only; no native run performed.';$store.SaveState($state)
        if($store.LoadSettings().MinimumCooldownMinutes -ne 1440 -or $store.LoadState().LastRunFinished -ne $state.LastRunFinished) {throw 'Cooldown precondition not persisted'}
    } finally {$lease.Dispose()}
}
function SetFutureTask([string]$exe,[string]$label) {
    $day=if((Get-Date).Day -eq 28){1}else{28}
    InvokeExe $exe ('schedule set --enable --frequency Monthly --month-day '+$day+' --time 23:59 --no-at-startup --no-run-if-missed --no-wake --no-auto-retry --cooldown 1440') $label
    $null=Disable-ScheduledTask -TaskPath '\PcNinja\' -TaskName 'WinTool by PcNinja'
}
function GrantFolder([string]$folder,[bool]$writable) {
    if((Get-Item -LiteralPath $folder).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Refuse linked lab folder'}
    $acl=New-Object Security.AccessControl.DirectorySecurity
    $admin=New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-544';$acl.SetOwner($admin);$acl.SetAccessRuleProtection($true,$false)
    foreach($sid in @('S-1-5-18','S-1-5-32-544','S-1-5-32-545')) {
        $principalSid=New-Object Security.Principal.SecurityIdentifier $sid
        $rights=if($sid -ne 'S-1-5-32-545'){'FullControl'}elseif($writable){'Modify'}else{'ReadAndExecute'}
        $rule=New-Object Security.AccessControl.FileSystemAccessRule($principalSid,$rights,'ContainerInherit, ObjectInherit','None','Allow');$acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $folder -AclObject $acl
}
function ProbeWrite([string]$path) {
    try {$stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Write,[IO.FileShare]::Read);$stream.Dispose();return $true}
    catch {if($_.Exception.GetBaseException() -is [UnauthorizedAccessException]){return $false};throw}
}
if($Phase -eq 'StandardUser') {
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    if(@($identity.Groups | Where-Object Value -eq 'S-1-5-32-544').Count -or @(Get-LocalGroup -SID 'S-1-5-32-544' | Get-LocalGroupMember | Where-Object {$_.SID.Value -eq $identity.User.Value}).Count) {throw 'Use real standard account, not filtered administrator.'}
    $saved=Get-Content -LiteralPath $checkpoint -Raw | ConvertFrom-Json
    $target=$saved.ProtectedTarget
    if((Get-FileHash -LiteralPath $source).Hash -ne $rcHash -or (Get-FileHash -LiteralPath $target).Hash -ne $rcHash){throw 'Fixture bytes changed'}
    $sourceWritable=ProbeWrite $source; $targetWritable=ProbeWrite $target
    $probe=Join-Path (Split-Path $target) ('Boundary-'+[Guid]::NewGuid().ToString('N')+'.tmp');$folderDenied=$false;$folderCreated=$false
    try {$stream=[IO.File]::Open($probe,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None);$stream.Dispose();$folderCreated=$true}
    catch {if($_.Exception.GetBaseException() -is [UnauthorizedAccessException]){$folderDenied=$true}else{throw}}
    $cleanupError=$null
    if($folderCreated){try {Remove-Item -LiteralPath $probe}catch {$cleanupError=$_.Exception.Message}}
    $proof=[ordered]@{RunId=$saved.RunId;IsAdministrator=$false;UserSid=$identity.User.Value;SourceWritable=$sourceWritable;ProtectedTargetWritable=$targetWritable;ProtectedFolderCreateDenied=$folderDenied;ProtectedFolderFileCreated=$folderCreated;ProbeCleanupError=$cleanupError;Result=$(if($sourceWritable -and -not $targetWritable -and $folderDenied -and -not $folderCreated){'PASS'}else{'FAIL'})}
    $proof | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $userEv 'proof.json') -Encoding utf8
    $proof | ConvertTo-Json
    Write-Output 'Return to administrator; Finish only after PASS. No executable contents changed.'
    exit
}
New-Item -ItemType Directory -Path $ev -Force | Out-Null
try {
    if($Phase -eq 'Setup') {
        if([string]::IsNullOrWhiteSpace($SnapshotLabel)){throw 'Supply actual restored clean snapshot name.'}
        if((Test-Path -LiteralPath $checkpoint) -or (Test-Path -LiteralPath $custom) -or (Test-Path -LiteralPath $data) -or (Test-Path -LiteralPath (Join-Path $env:ProgramData 'PcNinja\WinTool')) -or (OwnTasks).Count -or (Test-Path -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\PcNinja\WinTool')) {throw 'Restore clean pre-WinTool snapshot first; preserve older evidence. No fixture removed.'}
        GrantFolder $lab $false
        Msi '/i' $baseline 'ownership-baseline-install'
        if([Diagnostics.FileVersionInfo]::GetVersionInfo($installed).FileVersion -ne '1.0.2.9000'){throw 'Baseline must be older than portable; avoid a downgrade-guard false pass'}
        SetFutureTask $installed 'ownership-baseline-schedule'; AssertTask $installed
        InvokeExe $rc '--initialize-data' 'initialize-data'
        ImportStore
        if(-not (IsProtected $installed)){throw 'Baseline installed target not protected'}
        SeedCooldown
        $xmlBefore=MainXml;$treeBefore=SchedulerSnapshot
        $xmlBefore | Set-Content -LiteralPath (Join-Path $ev 'ownership-before.xml') -Encoding utf8
        $treeBefore | Set-Content -LiteralPath (Join-Path $ev 'scheduler-before.json') -Encoding utf8
        $logFile=Join-Path $data ('Logs\WinTool-'+(Get-Date).ToString('yyyyMMdd')+'.log')
        $offset=if(Test-Path -LiteralPath $logFile){(Get-Item -LiteralPath $logFile).Length}else{0L}
        InvokeExe $rc 'update run --scheduled --json' 'portable-update-run' 21
        $outcome=Get-Content -LiteralPath (Join-Path $ev 'portable-update-run-stdout.txt') -Raw | ConvertFrom-Json
        $kind=if($outcome.Result -is [string]){$outcome.Result}else{[Enum]::GetName($wu.GetType('WinTool.WindowsUpdate.Engine.RunResult',$true),[int]$outcome.Result)}
        if($kind -ne 'CooldownActive'){throw 'Not cooldown branch; do not classify AlreadyRunning/21 as this test passing'}
        AssertTask $installed
        $xmlAfter=MainXml;$treeAfter=SchedulerSnapshot
        $xmlAfter | Set-Content -LiteralPath (Join-Path $ev 'ownership-after.xml') -Encoding utf8
        $treeAfter | Set-Content -LiteralPath (Join-Path $ev 'scheduler-after.json') -Encoding utf8
        if($xmlBefore -cne $xmlAfter -or $treeBefore -cne $treeAfter){throw 'Portable update command changed protected installed task or Scheduler'}
        $stream=[IO.File]::Open($logFile,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
        try {$stream.Position=$offset;$reader=New-Object IO.StreamReader($stream);try {$newLog=$reader.ReadToEnd()}finally{$reader.Dispose()}}finally{$stream.Dispose()}
        $newLog | Set-Content -LiteralPath (Join-Path $ev 'portable-update-run-new-log.txt') -Encoding utf8
        if($newLog -notmatch 'Cooldown active' -or $newLog -match 'Worker refresh deferred|Task maintenance deferred|Query begin|Pass \d+ (downloaded|installed)|Starting service:|Microsoft Update registered'){throw 'Unexpected native work or refresh error; review evidence, no repeat'}
        $result.Ownership='PASS';$result.NoNativeScanClaim=$true
        Msi '/x' $baseline 'ownership-baseline-uninstall'
        if((OwnTasks).Count){throw 'Baseline own tasks remain'}
        New-Item -ItemType Directory -Path $custom | Out-Null;GrantFolder $custom $true
        Msi '/i' $msi 'custom-install' ('INSTALLFOLDER="'+$custom+'"')
        if((Get-FileHash -LiteralPath $source).Hash -ne $rcHash -or (IsProtected $source)){throw 'Custom install fixture is not expected writable RC7 source'}
        SetFutureTask $source 'custom-schedule'
        $target=(MainTask).Actions[0].Execute;AssertTask $target
        if($target -eq $source -or -not (IsProtected $target) -or -not $core.GetType('WinTool.Core.Security.ProtectedTaskCopy',$true).GetMethod('IsManagedPath').Invoke($null,[object[]]@($target)) -or (Get-FileHash -LiteralPath $target).Hash -ne $rcHash){throw 'Writable source not converted to valid protected managed target'}
        MainXml | Set-Content -LiteralPath (Join-Path $ev 'custom-task.xml') -Encoding utf8
        Get-Acl -LiteralPath $custom | Format-List * | Out-File -LiteralPath (Join-Path $ev 'custom-source-acl.txt') -Encoding utf8
        Get-Acl -LiteralPath $target | Format-List * | Out-File -LiteralPath (Join-Path $ev 'custom-target-acl.txt') -Encoding utf8
        New-Item -ItemType Directory -Path $userEv | Out-Null;GrantFolder $userEv $true
        [ordered]@{RunId=[Guid]::NewGuid().ToString('N');Snapshot=$SnapshotLabel;ProtectedTarget=$target;Source=$source;Phase='AwaitingStandardUser'} | ConvertTo-Json | Set-Content -LiteralPath $checkpoint -Encoding utf8
        $result.Result='SETUP_PASS_STANDARD_USER_PENDING'
    } else {
        $saved=Get-Content -LiteralPath $checkpoint -Raw | ConvertFrom-Json
        $proof=Get-Content -LiteralPath (Join-Path $userEv 'proof.json') -Raw | ConvertFrom-Json
        if($proof.RunId -ne $saved.RunId -or $proof.Result -ne 'PASS' -or $proof.IsAdministrator -or -not $proof.SourceWritable -or $proof.ProtectedTargetWritable -or -not $proof.ProtectedFolderCreateDenied -or $proof.ProtectedFolderFileCreated){throw 'Matching standard-user PASS required'}
        if((Get-FileHash -LiteralPath $source).Hash -ne $rcHash -or (Get-FileHash -LiteralPath $saved.ProtectedTarget).Hash -ne $rcHash){throw 'Source/target changed; do not launch uninstall from unexpected bytes'}
        AssertTask $saved.ProtectedTarget
        Msi '/x' $msi 'custom-uninstall'
        if((OwnTasks).Count){throw 'Own tasks remain after normal uninstall'}
        $result.Result='PASS';$result.StandardUser=$proof;$result.NoNativeScanClaim=$true
    }
} catch {$result.Error=$_.Exception.ToString();$result.Result='FAIL';Write-Warning $_.Exception.Message}
try {
    $productLogs=Join-Path $data 'Logs'
    if(Test-Path -LiteralPath $productLogs) {
        if((Get-Item -LiteralPath $productLogs).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Refuse linked logs'}
        $destination=Join-Path $ev ($Phase+'-ProductLogs');New-Item -ItemType Directory -Path $destination -Force | Out-Null
        foreach($file in Get-ChildItem -LiteralPath $productLogs -File -Filter '*.log') {
            if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Refuse linked log file'}
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $destination $file.Name)
        }
    }
} catch {$result.LogCaptureError=$_.Exception.Message}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ev ($Phase+'-summary.json')) -Encoding utf8
$archive=Join-Path $lab ('SYSTEM-'+$Phase+'-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.zip')
$manifest=@(Get-ChildItem -LiteralPath $ev -Recurse -File | Where-Object Name -ne 'manifest.json' | ForEach-Object {[pscustomobject]@{Name=$_.FullName.Substring($ev.Length+1);SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
ConvertTo-Json -InputObject $manifest | Set-Content -LiteralPath (Join-Path $ev 'manifest.json') -Encoding utf8
Compress-Archive -Path (Join-Path $ev '*') -DestinationPath $archive
Write-Output ($Phase+': '+$result.Result+'; return ZIP privately: '+$archive)
Write-Output 'No task was started. No update Search/Download/Install intended; fixture scheduled CLI must end CooldownActive before probes. Stop on any FAIL. No merge/tag/release.'
