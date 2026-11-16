[CmdletBinding()]
param(
    [ValidateSet('Prepare','StandardUser','Finish','Recover')][string]$Phase='Prepare',
    [switch]$ConfirmDisposableVM,
    [switch]$ConfirmReviewedKit,
    [string]$SnapshotLabel,
    [string]$StandardUserName
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2

function Resolve-KitPath([string]$root,[string]$relative) {
    if([IO.Path]::IsPathRooted($relative)) {throw 'Absolute manifest path rejected.'}
    $base=[IO.Path]::GetFullPath($root).TrimEnd('\')+'\'
    $full=[IO.Path]::GetFullPath((Join-Path $root $relative))
    if(-not $full.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)) {throw 'Manifest path escaped kit.'}
    return $full
}
function Assert-Hash([string]$path,[string]$expected) {
    if($expected -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $path).Hash -ne $expected) {throw ('Hash mismatch: '+$path)}
}
function Assert-RootPath([string]$root,[string]$programFiles) {
    $item=New-Object IO.DirectoryInfo([IO.Path]::GetFullPath($root))
    if($item.Name -notmatch '^PcNinja-Issue29-[0-9a-f]{32}$' -or $item.Parent.FullName -ine [IO.Path]::GetFullPath($programFiles).TrimEnd('\')) {throw 'Invalid protected lab root.'}
}
function Assert-UserProof($proof,[string]$runId,[string]$sid) {
    if($proof.RunId -cne $runId -or $proof.UserSid -cne $sid -or $proof.IsAdministrator -ne $false -or
        $proof.UnsafeWritable -ne $true -or $proof.ProtectedWritable -ne $false -or $proof.Result -cne 'PASS') {throw 'Standard-user proof missing or inconsistent.'}
}
function Save-Json($value,[string]$path) {ConvertTo-Json -InputObject $value -Depth 10 | Set-Content -LiteralPath $path -Encoding UTF8}
function Save-Outcome {
    Save-Json ([pscustomobject]@{Phase=$Phase;Result=$result;Failure=$failure;RestoreError=$restoreError;
        RunId=if($null -ne $session){$session.RunId}else{$null};Steps=$steps.ToArray();ProductHead=$candidate.ProductHead;HelperHead=$candidate.HelperHead}) (Join-Path $ev 'summary.json')
}
function Is-Admin {
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
function Require-Yes([string]$text) {
    if((Read-Host ($text+' Type YES')) -cne 'YES') {throw 'Observation not confirmed. Exit zero alone is not acceptance.'}
    $steps.Add([pscustomobject]@{Observation=$text;Confirmed=$true})
}
function Wait-Quiet {
    $clock=[Diagnostics.Stopwatch]::StartNew();$quiet=0
    while($clock.Elapsed.TotalSeconds -lt 120) {
        if(@(Get-Process -Name 'WinTool-by-PcNinja' -ErrorAction SilentlyContinue).Count -eq 0) {$quiet++} else {$quiet=0}
        if($quiet -ge 3) {return}
        Start-Sleep -Seconds 1
    }
    throw 'WinTool process remains. Nothing was killed; stop and return evidence.'
}
function Assert-NoTasks {
    if(@(Get-ScheduledTask | Where-Object {$_.TaskPath -eq '\PcNinja\' -and $_.TaskName -like 'WinTool*'}).Count) {throw 'Unexpected own task. No task was started or removed.'}
}
function Assert-Exit($process,[int]$expected,[string]$label) {
    if(-not $process.WaitForExit(15000)) {throw ($label+': process still running; nothing was killed.')}
    $steps.Add([pscustomobject]@{Name=$label;ExitCode=$process.ExitCode;Expected=$expected})
    if($process.ExitCode -ne $expected) {throw ($label+': unexpected exit '+$process.ExitCode)}
}
function Start-Product([string]$arguments='',[bool]$restart=$false) {
    $info=New-Object Diagnostics.ProcessStartInfo($universal)
    $info.UseShellExecute=$false;$info.Arguments=$arguments
    if($restart) {$info.EnvironmentVariables['WINTOOL_WAIT_FOR_PID']='2147483647'}
    return [Diagnostics.Process]::Start($info)
}
function Observe-Product([string]$label,[string]$arguments,[int]$exit,[string]$instruction) {
    Write-Host $instruction
    $p=Start-Product $arguments
    Require-Yes 'After observing exactly that behavior, close its windows normally.'
    Assert-Exit $p $exit $label
    Wait-Quiet
}
function Observe-Installed([string]$label) {
    Write-Host 'Expect the installed window with NO Install/Portable chooser and NO MSI. Leave it open. Normal UAC is allowed.'
    $p=Start-Product
    Require-Yes 'Did that installed window open without a chooser/installer? Leave it open.'
    $processes=@(Get-CimInstance Win32_Process -Filter "Name='WinTool-by-PcNinja.exe'" | Select-Object ProcessId,ParentProcessId,ExecutablePath,CommandLine)
    Save-Json $processes (Join-Path $ev ($label+'-processes.json'))
    if(Is-Admin) {
        if(@($processes | Where-Object {$_.ExecutablePath -ieq $installed}).Count -ne 1) {throw 'Installed path not observed.'}
    }
    $second=Start-Product
    Assert-Exit $second 0 ($label+'-activate')
    Require-Yes 'Did the second launch activate the SAME window without a chooser/installer?'
    Read-Host 'Close the installed window normally, then press Enter' | Out-Null
    Assert-Exit $p 0 $label
    Wait-Quiet
}
function Run-Msi([string]$action,[string]$path,[string]$label) {
    $arguments=$action+' "'+$path+'" /qn /norestart /L*v "'+(Join-Path $ev ($label+'.log'))+'"'
    if($action -eq '/i') {$arguments+=' WINTOOL_ENABLE_SCHEDULE=0'}
    $p=Start-Process msiexec.exe -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    $steps.Add([pscustomobject]@{Name=$label;ExitCode=$p.ExitCode})
    if($p.ExitCode -ne 0) {throw ($label+' needs review (including restart-required 3010): '+$p.ExitCode)}
    Wait-Quiet
}
function With-Lease([scriptblock]$action) {
    $lease=[WinTool.Core.Operations.OperationLock]::TryAcquire('Issue29 disposable VM fixture',[TimeSpan]::Zero)
    if($null -eq $lease) {throw 'Operation busy. No fixture change attempted.'}
    try {& $action} finally {$lease.Dispose()}
}
function Set-LabDirectory([string]$path,[Security.Principal.SecurityIdentifier]$writer=$null) {
    if(Test-Path -LiteralPath $path) {throw 'Fixture directory already exists; not adopted or repaired.'}
    New-Item -ItemType Directory -Path $path | Out-Null
    $acl=New-Object Security.AccessControl.DirectorySecurity
    $acl.SetOwner($admins);$acl.SetAccessRuleProtection($true,$false)
    foreach($sid in @($admins,$system)) {$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow')))}
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($users,'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow')))
    if($null -ne $writer) {$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($writer,'Modify','ContainerInherit,ObjectInherit','None','Allow')))}
    Set-Acl -LiteralPath $path -AclObject $acl
}
function Set-AdminOwner([string]$path) {$acl=Get-Acl -LiteralPath $path;$acl.SetOwner($admins);Set-Acl -LiteralPath $path -AclObject $acl}
function Fixture-Exe([string]$name) {return (Join-Path $session.Root ($name+'\WinTool-by-PcNinja.exe'))}
function Fixture-Variant([string]$name) {
    if($name -in @('UnsafeOlder','Changing')) {return 'OLDER'}
    if($name -in @('UnsafeNewer','ProtectedNewer')) {return 'NEWER'}
    if($name -in @('UnsafeSame','Failure')) {return 'SAME'}
    throw 'Unknown fixture name.'
}
function Set-RegisteredFolder([string]$folder) {
    With-Lease {
        $machine=[Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine','Registry64')
        try {
            $key=$machine.OpenSubKey('SOFTWARE\PcNinja\WinTool',$true)
            if($null -eq $key) {throw 'Expected existing product registry key; none was created.'}
            try {$key.SetValue('InstallFolder',$folder,[Microsoft.Win32.RegistryValueKind]$session.OriginalKind)} finally {$key.Dispose()}
        } finally {$machine.Dispose()}
    }
}
function Restore-RegisteredFolder {
    $current=[WinTool.Core.Updates.InstalledCopy]::ReadInstallFolder()
    $allowed=@($session.OriginalFolder)+@($session.FixtureNames | ForEach-Object {Join-Path $session.Root $_})
    if($current -notin $allowed) {throw 'Registry changed by another actor; refused to overwrite it. Restore snapshot.'}
    Set-RegisteredFolder $session.OriginalFolder
    if([WinTool.Core.Updates.InstalledCopy]::ReadInstallFolder() -ine $session.OriginalFolder) {throw 'Registry restoration not confirmed.'}
}
function Snapshot-Boundary {
    Assert-NoTasks
    $data=Join-Path $env:ProgramData 'PcNinja-WinTool'
    $files=@(if(Test-Path -LiteralPath $data) {Get-ChildItem -LiteralPath $data -Recurse -File | Where-Object {$_.FullName -notlike ($data+'\Logs\*')} | Sort-Object FullName | ForEach-Object {
        $_.FullName.Substring($data.Length)+' '+(Get-FileHash -LiteralPath $_.FullName).Hash
    }})
    return [pscustomobject]@{DataFiles=$files;InstallHash=(Get-FileHash -LiteralPath $installed).Hash;
        ExtractedMsis=@(Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Directory -Filter 'WinTool-Install-*' | Sort-Object Name | Select-Object -ExpandProperty Name)}
}
function Assert-Boundary($before) {
    $after=Snapshot-Boundary
    if((ConvertTo-Json $before -Compress -Depth 5) -cne (ConvertTo-Json $after -Compress -Depth 5)) {throw 'Non-log data, installed file, tasks or extracted-MSI folders changed.'}
}
function Collect-UserEvidence([string]$source,[string]$destination) {
    # The standard account owns these inputs. Never recurse/copy through links.
    New-Item -ItemType Directory -Path $destination | Out-Null
    $queue=New-Object 'Collections.Generic.Queue[string]';$queue.Enqueue($source)
    $count=0;$directories=0
    while($queue.Count) {
        $directory=$queue.Dequeue();$directories++
        if($directories -gt 10) {throw 'Too many user-evidence directories.'}
        foreach($file in [WinTool.Core.Security.ProtectedMachineData]::PlainFiles($directory)) {
            $count++;if($count -gt 50) {throw 'Too many user-evidence files.'}
            $text=[WinTool.Core.Security.ProtectedMachineData]::ReadUntrustedFile($file)
            $name=$count.ToString('D3')+'-'+[IO.Path]::GetFileName($file)
            $text | Set-Content -LiteralPath (Join-Path $destination $name) -Encoding UTF8
        }
        foreach($child in [IO.Directory]::GetDirectories($directory)) {$queue.Enqueue($child)}
    }
}
function Fixture-Case([string]$name,[string]$arguments,[int]$exit,[string]$instruction,[bool]$restart=$false,[bool]$positive=$false) {
    Wait-Quiet
    $exe=Fixture-Exe $name
    Assert-Hash $exe (Get-FileHash -LiteralPath (Join-Path $kit ('Fixtures\'+(Fixture-Variant $name)+'.exe'))).Hash
    if(Test-Path -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($exe)) 'executed.marker')) {throw 'Pre-existing fixture marker.'}
    Set-RegisteredFolder ([IO.Path]::GetDirectoryName($exe))
    try {
        $before=Snapshot-Boundary
        $p=Start-Product $arguments $restart
        if(-not $positive) {Write-Host $instruction;Require-Yes 'Observe that exact message/choice, dismiss it; NO Install/Update/Portable selection.'}
        Assert-Exit $p $exit ($name+' '+$arguments+' restart='+$restart)
        Wait-Quiet
        $marker=Test-Path -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($exe)) 'executed.marker')
        if($marker -ne $positive) {throw 'Unexpected fixture execution (or expected redirect missing).'}
        Assert-Boundary $before
    } finally {Restore-RegisteredFolder}
}

if(-not $ConfirmDisposableVM -or -not $ConfirmReviewedKit) {throw 'Disposable VM and exact-head fixture-kit PASS are required.'}
if($PSVersionTable.PSEdition -ne 'Desktop' -or [IntPtr]::Size -ne 8) {throw 'Use 64-bit Windows PowerShell 5.1.'}
if([IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') -ine 'C:\WinTool-Issue29') {throw 'Extract the kit into C:\WinTool-Issue29.'}
if(($Phase -eq 'StandardUser') -eq (Is-Admin)) {throw 'Prepare/Finish/Recover require elevation; StandardUser requires a non-elevated real standard account.'}
$kit=$PSScriptRoot
foreach($line in Get-Content -LiteralPath (Join-Path $kit 'SHA256SUMS.txt')) {
    if($line -notmatch '^([a-fA-F0-9]{64})  ([a-zA-Z0-9_./-]+)$') {throw 'Invalid manifest line.'}
    Assert-Hash (Resolve-KitPath $kit $Matches[2]) $Matches[1]
}
$universal=Join-Path $kit 'Candidate\WinTool-by-PcNinja.exe'
$msi=Join-Path $kit 'Candidate\WinTool-by-PcNinja.msi'
Assert-Hash $universal '0bcac1446f395673456968d191be7e4405793d790dae0177fbdd1a186f2289d5'
Assert-Hash $msi 'd591f4d869831136e657264c7e8386b819f0c8659ffa6b35a1ad84f6cb0c515f'
$candidate=Get-Content -LiteralPath (Join-Path $kit 'Candidate.json') -Raw | ConvertFrom-Json
if($candidate.ProductHead -cne '16e663a1c550baf5a5cea207814806f303994d3b' -or $candidate.Version -cne '1.0.4-rc.1') {throw 'Wrong product pin.'}
Add-Type -Path (Join-Path $kit 'Library\WinTool.Core.dll')
$admins=New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')
$system=New-Object Security.Principal.SecurityIdentifier('S-1-5-18')
$users=New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545')
$installed=Join-Path $env:ProgramFiles 'PcNinja\WinTool\WinTool-by-PcNinja.exe'
$steps=New-Object 'Collections.Generic.List[object]'
$session=$null;$failure=$null;$result='REVIEW';$restoreError=$null
$oldEnv=@{};foreach($name in @('WINTOOL_DATA_ROOT','WINTOOL_UPDATE_FEED','WINTOOL_WAIT_FOR_PID','WINTOOL_NO_ELEVATION')) {
    $oldEnv[$name]=[Environment]::GetEnvironmentVariable($name,'Process');[Environment]::SetEnvironmentVariable($name,$null,'Process')
}
$ev=Join-Path $kit ('Evidence\'+$Phase+'-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
try {
    if(Is-Admin) {New-Item -ItemType Directory -Path $ev -Force | Out-Null}
    $os=Get-CimInstance Win32_OperatingSystem
    if($os.ProductType -ne 1 -or [int]$os.BuildNumber -lt 18362) {throw 'Windows 10 1903+ or Windows 11 client required.'}
    if($Phase -eq 'Prepare') {
        if([string]::IsNullOrWhiteSpace($SnapshotLabel) -or [string]::IsNullOrWhiteSpace($StandardUserName)) {throw 'Provide the actual clean snapshot and existing standard local account name.'}
        foreach($path in @($installed,(Join-Path $kit 'Session.json'),'HKLM:\SOFTWARE\PcNinja\WinTool','HKLM:\SOFTWARE\WOW6432Node\PcNinja\WinTool',(Join-Path $env:ProgramData 'PcNinja-WinTool'),(Join-Path $env:ProgramData 'PcNinja\WinTool'))) {
            if(Test-Path -LiteralPath $path) {throw 'Existing fixture/product/data; restore clean snapshot. Nothing removed.'}
        }
        $registrations=@(foreach($root in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall')) {
            Get-ChildItem -LiteralPath $root | ForEach-Object {$entry=Get-ItemProperty -LiteralPath $_.PSPath;if($entry.PSObject.Properties['DisplayName'] -and $entry.DisplayName -match 'WinTool by PcNinja|WinUpdate Tool') {$entry.DisplayName}}
        })
        if($registrations.Count) {throw 'Existing WinTool/WinUpdate uninstall registration.'}
        Assert-NoTasks
        if(@(Get-Process -Name 'WinTool-by-PcNinja' -ErrorAction SilentlyContinue).Count) {throw 'Existing WinTool process.'}
        $user=Get-LocalUser -Name $StandardUserName
        if(-not $user.Enabled) {throw 'The standard account is disabled.'}
        if(@(Get-LocalGroupMember -SID $admins | Where-Object {$_.SID.Value -eq $user.SID.Value}).Count) {throw 'The chosen account is a local administrator.'}
        $runId=[Guid]::NewGuid().ToString('N')
        $session=[pscustomobject]@{RunId=$runId;Root=(Join-Path $env:ProgramFiles ('PcNinja-Issue29-'+$runId));UserSid=$user.SID.Value;Snapshot=$SnapshotLabel;OriginalFolder=$null;OriginalKind=$null;Status='Preparing';FixtureNames=@('UnsafeOlder','UnsafeSame','UnsafeNewer','ProtectedNewer','Failure','Changing')}
        With-Lease {Set-LabDirectory $session.Root;Set-LabDirectory (Join-Path $session.Root 'UserEvidence') $user.SID}
        New-Item -ItemType Directory -Path $ev -Force | Out-Null
        Observe-Product 'clean-cancel' '' 0 'Expect the original Install / Portable / Cancel question. Choose CANCEL.'
        Observe-Product 'explicit-portable' '--portable' 0 'Expect a normal portable main window, with NO chooser. Close it; do not scan/install/schedule.'
        foreach($arguments in @('--portable --json','--portable-chosen --json')) {$p=Start-Product $arguments;Assert-Exit $p 2 $arguments}
        Assert-Hash (Join-Path $kit 'Baseline\WinTool-by-PcNinja.msi') '02f2059e28827bb4577a33b298ff501ffdf04ddcd6a5f43e323155d2c19dd75e'
        Run-Msi '/i' (Join-Path $kit 'Baseline\WinTool-by-PcNinja.msi') 'baseline-install'
        Observe-Product 'older-no' '' 0 'Expect Update 1.0.3 to 1.0.4-rc.1. Choose NO, confirm the installed 1.0.3 window opens, then close it. No installer.'
        Observe-Product 'older-yes' '' 0 'Expect the SAME update question. Choose YES. Finish the one installer wizard with SCHEDULING DISABLED. Close any app opened at Finish. Do not scan or install Windows updates.'
        Assert-Hash $installed $candidate.PlainSha256
        Assert-NoTasks
        $session.OriginalFolder=[WinTool.Core.Updates.InstalledCopy]::ReadInstallFolder()
        $machine=[Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine','Registry64')
        try {$key=$machine.OpenSubKey('SOFTWARE\PcNinja\WinTool');try {$session.OriginalKind=$key.GetValueKind('InstallFolder').ToString()} finally {$key.Dispose()}} finally {$machine.Dispose()}
        if($session.OriginalFolder -ine [IO.Path]::GetDirectoryName($installed) -or -not [WinTool.Core.Security.ProtectedMachineData]::IsProtectedExecutable($installed)) {throw 'Expected protected normal installation missing.'}
        Observe-Installed 'admin-redirect'
        With-Lease {
            foreach($name in $session.FixtureNames) {
                $folder=Join-Path $session.Root $name
                if($name -like 'Unsafe*') {Set-LabDirectory $folder $users} else {Set-LabDirectory $folder}
                $variant=Fixture-Variant $name
                $exe=Fixture-Exe $name
                Copy-Item -LiteralPath (Join-Path $kit ('Fixtures\'+$variant+'.exe')) -Destination $exe
                Set-AdminOwner $exe
                if($name -eq 'Failure') {
                    $acl=Get-Acl -LiteralPath $exe
                    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier('S-1-1-0')),'ExecuteFile','Deny')))
                    Set-Acl -LiteralPath $exe -AclObject $acl
                }
                $safe=[WinTool.Core.Security.ProtectedMachineData]::IsProtectedExecutable($exe)
                if($safe -ne ($name -notlike 'Unsafe*')) {throw ('Fixture protection precondition failed: '+$name)}
            }
            $session.Status='STANDARD_USER_PENDING'
            Save-Json $session (Join-Path $session.Root 'Session.json');Set-AdminOwner (Join-Path $session.Root 'Session.json')
            Save-Json ([pscustomobject]@{Root=$session.Root}) (Join-Path $kit 'Session.json')
        }
        $result='PREPARE_PASS_STANDARD_USER_PENDING'
    } else {
        $pointer=Get-Content -LiteralPath (Join-Path $kit 'Session.json') -Raw | ConvertFrom-Json
        Assert-RootPath $pointer.Root $env:ProgramFiles
        $statePath=Join-Path $pointer.Root 'Session.json'
        if(-not [WinTool.Core.Security.ProtectedMachineData]::IsProtectedExecutable($statePath)) {throw 'Untrusted session file.'}
        $session=Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if($session.Root -ine $pointer.Root -or $session.RunId -cne ([IO.Path]::GetFileName($pointer.Root).Substring(16))) {throw 'Session identity mismatch.'}
        foreach($fixture in $session.FixtureNames) {
            Assert-Hash (Fixture-Exe $fixture) (Get-FileHash -LiteralPath (Join-Path $kit ('Fixtures\'+(Fixture-Variant $fixture)+'.exe'))).Hash
        }
        if($Phase -eq 'StandardUser') {
            $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
            if($sid -cne $session.UserSid -or $session.Status -cne 'STANDARD_USER_PENDING') {throw 'Wrong account or phase.'}
            $ev=Join-Path $session.Root ('UserEvidence\'+[Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $ev | Out-Null
            Assert-Hash $installed $candidate.PlainSha256
            Observe-Installed 'standard-redirect'
            Observe-Product 'standard-uac-cancel' '' 0 'At the installed copy UAC credential prompt choose CANCEL. Confirm no alternate installer or portable main window opens. The launching parent may exit zero; this is an observation.'
            $unsafe=$false;$protected=$false
            try {$file=[IO.File]::Open((Fixture-Exe 'UnsafeSame'),'Open','ReadWrite','ReadWrite');$file.Dispose();$unsafe=$true} catch [UnauthorizedAccessException] {}
            try {$file=[IO.File]::Open((Fixture-Exe 'ProtectedNewer'),'Open','ReadWrite','ReadWrite');$file.Dispose();$protected=$true} catch [UnauthorizedAccessException] {}
            $proof=[pscustomobject]@{RunId=$session.RunId;UserSid=$sid;IsAdministrator=(Is-Admin);UnsafeWritable=$unsafe;ProtectedWritable=$protected;Result='PASS'}
            Assert-UserProof $proof $session.RunId $session.UserSid
            Save-Json $proof (Join-Path $session.Root 'UserEvidence\standard-user.json')
            $result='STANDARD_USER_PASS'
        } elseif($Phase -eq 'Recover') {
            New-Item -ItemType Directory -Path $ev -Force | Out-Null
            Restore-RegisteredFolder
            $result='REGISTRY_RESTORED_ONLY'
        } else {
            New-Item -ItemType Directory -Path $ev -Force | Out-Null
            if($session.Status -cne 'STANDARD_USER_PENDING') {throw 'Finish already ran or phase is invalid. Restore snapshot; do not repeat.'}
            $proof=[WinTool.Core.Security.ProtectedMachineData]::ReadUntrustedFile((Join-Path $session.Root 'UserEvidence\standard-user.json')) | ConvertFrom-Json
            Assert-UserProof $proof $session.RunId $session.UserSid
            Assert-Hash $installed $candidate.PlainSha256
            if([WinTool.Core.Updates.InstalledCopy]::ReadInstallFolder() -ine $session.OriginalFolder) {throw 'Registry not at its expected installed value; use Recover or restore snapshot.'}
            Fixture-Case 'UnsafeOlder' '' 0 'Expect Update / Portable / Cancel with unsafe path warning. Choose CANCEL.'
            Fixture-Case 'UnsafeSame' '' 0 'Expect only Portable (OK) / Cancel with unsafe path warning. Choose CANCEL.'
            Fixture-Case 'UnsafeNewer' '' 6 'Expect older-copy block only. Dismiss. The known text may refer to a protected installed copy; do not open that fixture.'
            foreach($flag in @('--portable','--portable-chosen')) {Fixture-Case 'ProtectedNewer' $flag 6 'Expect older-copy block; dismiss it.'}
            Fixture-Case 'ProtectedNewer' '' 6 'Expect older-copy block on self-update restart intent; dismiss it.' $true
            Fixture-Case 'ProtectedNewer' '' 0 '' $false $true
            Fixture-Case 'Failure' '' 6 'Expect installed-launch failure with path/reason. Dismiss. No installer or portable main window.'
            $changing=Fixture-Exe 'Changing'
            Set-RegisteredFolder ([IO.Path]::GetDirectoryName($changing))
            try {
                $before=Snapshot-Boundary
                $p=Start-Product
                Require-Yes 'Leave the Update question OPEN. Do not answer it yet.'
                With-Lease {
                    $acl=Get-Acl -LiteralPath $changing
                    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($users,'Modify','Allow')))
                    Set-Acl -LiteralPath $changing -AclObject $acl
                }
                if([WinTool.Core.Security.ProtectedMachineData]::IsProtectedExecutable($changing)) {throw 'Trust-change fixture still protected.'}
                Require-Yes 'NOW choose NO. Expect changed/protection error, dismiss it. No installed fixture, MSI or portable main window.'
                Assert-Exit $p 6 'changed-trust'
                Wait-Quiet
                if(Test-Path -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($changing)) 'executed.marker')) {throw 'Changed target was executed.'}
                Assert-Boundary $before
            } finally {Restore-RegisteredFolder}
            $session.Status='FIXTURES_PASS'
            With-Lease {Save-Json $session $statePath;Set-AdminOwner $statePath}
            Run-Msi '/x' $msi 'candidate-uninstall'
            if(Test-Path -LiteralPath $installed) {throw 'Installed EXE remains after uninstall.'}
            Assert-NoTasks
            $result='SCOPED_LAUNCH_PASS'
        }
    }
} catch {$failure=$_.Exception.ToString();Write-Warning $_.Exception.Message}
finally {
    # Only restore a fixture override; never recreate the registration after successful uninstall.
    if($null -ne $session -and -not [string]::IsNullOrWhiteSpace($session.OriginalFolder) -and (Is-Admin)) {
        try {
            $now=[WinTool.Core.Updates.InstalledCopy]::ReadInstallFolder()
            if($now -in @($session.FixtureNames | ForEach-Object {Join-Path $session.Root $_})) {Restore-RegisteredFolder}
        } catch {$restoreError=$_.Exception.ToString();$result='RESTORE_REQUIRED';Write-Warning 'Registry restoration needs review. Use Recover before any product launch, or restore snapshot.'}
    }
    foreach($name in $oldEnv.Keys) {[Environment]::SetEnvironmentVariable($name,$oldEnv[$name],'Process')}
    if(Test-Path -LiteralPath $ev) {
        try {
            # Persist the phase result before optional collection/ZIP work, including failures.
            Save-Outcome
            if(Is-Admin) {
                $logs=Join-Path $env:ProgramData 'PcNinja-WinTool\Logs'
                if(Test-Path -LiteralPath $logs) {Copy-Item -LiteralPath $logs -Destination (Join-Path $ev 'ProductLogs') -Recurse}
                if($null -ne $session) {Copy-Item -LiteralPath (Join-Path $session.Root 'Session.json') -Destination (Join-Path $ev 'session.json') -ErrorAction SilentlyContinue}
                if($null -ne $session -and (Test-Path -LiteralPath (Join-Path $session.Root 'UserEvidence'))) {Collect-UserEvidence (Join-Path $session.Root 'UserEvidence') (Join-Path $ev 'UserEvidence')}
            } else {
                $userLogs=Join-Path $env:LOCALAPPDATA 'PcNinja\WinTool\Logs'
                if(Test-Path -LiteralPath $userLogs) {Copy-Item -LiteralPath $userLogs -Destination (Join-Path $ev 'UserLogs') -Recurse}
            }
            if(Is-Admin) {Compress-Archive -LiteralPath $ev -DestinationPath ($ev+'.zip');Write-Host ('Return privately: '+$ev+'.zip')}
        } catch {
            $result='EVIDENCE_REVIEW';$failure=$_.Exception.ToString()
            try {Save-Outcome} catch {Write-Warning 'Could not save the evidence-review summary.'}
            Write-Warning ('Evidence archive unavailable. Preserve raw folder: '+$ev+'; '+$failure)
        }
    }
    Write-Host ($Phase+': '+$result+'. No merge/tag/release. No Windows Update/Office/Setup operation or task start was requested.')
}
if($null -ne $failure -or $null -ne $restoreError) {exit 1}
