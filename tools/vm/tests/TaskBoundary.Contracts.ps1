$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -ne 'Desktop' -or [IntPtr]::Size -ne 8){throw 'Run under64bit Windows PowerShell5.1.'}
Import-Module CimCmdlets
$helper=Join-Path $PSScriptRoot '..\Final-SystemBoundary.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($helper,[ref]$tokens,[ref]$errors)
if($errors.Count){throw ($errors.Message -join '; ')}
$definition=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'AssertTask'},$true)
if(-not $definition){throw 'Missing production helper assertion.'}
. ([scriptblock]::Create($definition.Extent.Text))

# Real client-only CIM objects reproduce the PS5 singleton shape, without WMI
# queries, task enumeration/registration, installers, store, gate or WUA calls.
$fixture=New-Object Microsoft.Management.Infrastructure.CimInstance 'TaskFixture'
foreach($entry in @(@{Name='TaskName';Value='WinTool by PcNinja'},@{Name='TaskPath';Value='\PcNinja\'},@{Name='State';Value='Disabled'})){
    $fixture.CimInstanceProperties.Add([Microsoft.Management.Infrastructure.CimProperty]::Create($entry.Name,$entry.Value,[Microsoft.Management.Infrastructure.CimFlags]::None))
}
$principal=New-Object Microsoft.Management.Infrastructure.CimInstance 'PrincipalFixture'
$principal.CimInstanceProperties.Add([Microsoft.Management.Infrastructure.CimProperty]::Create('UserId','SYSTEM',[Microsoft.Management.Infrastructure.CimFlags]::None))
$action=New-Object Microsoft.Management.Infrastructure.CimInstance 'ActionFixture'
$action.CimInstanceProperties.Add([Microsoft.Management.Infrastructure.CimProperty]::Create('Execute','C:\fixture.exe',[Microsoft.Management.Infrastructure.CimFlags]::None))
$action.CimInstanceProperties.Add([Microsoft.Management.Infrastructure.CimProperty]::Create('Arguments','update run --scheduled',[Microsoft.Management.Infrastructure.CimFlags]::None))
$fixture.CimInstanceProperties.Add([Microsoft.Management.Infrastructure.CimProperty]::Create('Principal',$principal,[Microsoft.Management.Infrastructure.CimFlags]::None))
$fixture.CimInstanceProperties.Add([Microsoft.Management.Infrastructure.CimProperty]::Create('Actions',[Microsoft.Management.Infrastructure.CimInstance[]]@($action),[Microsoft.Management.Infrastructure.CimFlags]::None))
$ev=Join-Path ([IO.Path]::GetTempPath()) ('WinToolHelperContract-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $ev | Out-Null
$script:number=1
function OwnTasks {for($i=0;$i -lt $script:number;$i++){$fixture}}
function ExpectRejection([string]$label,[string]$failedCheck){
    $failed=$false
    try{AssertTask 'C:\fixture.exe' $label}catch{$failed=$true}
    if(-not $failed){throw ('Invalid task passed: '+$label)}
    $report=Get-Content -LiteralPath (Join-Path $ev ($label+'-task-check.json')) -Raw | ConvertFrom-Json
    if($report.Checks.$failedCheck -ne $false){throw ('Wrong/missing failure diagnostic: '+$label)}
}
AssertTask 'C:\fixture.exe' 'one'
foreach($n in @(0,2)){$script:number=$n;ExpectRejection ('count'+$n) 'OneOwnTask'}
$script:number=1
foreach($case in @(@{Name='path';Field='Execute';Value='C:\wrong.exe';Check='CorrectPath'},@{Name='args';Field='Arguments';Value='wrong';Check='CorrectArguments'})){
    $saved=$action.CimInstanceProperties[$case.Field].Value
    $action.CimInstanceProperties[$case.Field].Value=$case.Value
    $fixture.CimInstanceProperties['Actions'].Value=[Microsoft.Management.Infrastructure.CimInstance[]]@($action)
    ExpectRejection $case.Name $case.Check
    $action.CimInstanceProperties[$case.Field].Value=$saved
    $fixture.CimInstanceProperties['Actions'].Value=[Microsoft.Management.Infrastructure.CimInstance[]]@($action)
}
$principal.CimInstanceProperties['UserId'].Value='OtherUser';$fixture.CimInstanceProperties['Principal'].Value=$principal
ExpectRejection 'user' 'SystemPrincipal'
Write-Output 'PASS: actual helper handles PS5 CIM0/1/2 tasks, rejects wrong path/args/principal, saves failure diagnostics. Six contract cases.'
Write-Output 'Client-only objects and temporary JSON only; no system task, registry, installer, store, gate or native update operation.'
