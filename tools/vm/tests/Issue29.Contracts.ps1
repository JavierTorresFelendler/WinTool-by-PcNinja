$ErrorActionPreference='Stop'
Set-StrictMode -Version 2
$helper=Join-Path $PSScriptRoot '..\Issue29-Launch.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($helper,[ref]$tokens,[ref]$errors)
if(@($errors).Count) {throw ($errors.Message -join '; ')}
# Load only pure declarations from the real helper. Its script body never runs.
foreach($name in @('Resolve-KitPath','Assert-RootPath','Assert-UserProof','Fixture-Variant')) {
    $definition=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
    if($null -eq $definition) {throw ('Missing contract function '+$name)}
    . ([scriptblock]::Create($definition.Extent.Text))
}
$count=0
function Reject([scriptblock]$action) {
    $failed=$false;try {& $action | Out-Null} catch {$failed=$true}
    if(-not $failed) {throw 'Unsafe contract accepted.'}
    $script:count++
}
if((Resolve-KitPath 'C:\Lab' 'Candidate/WinTool.exe') -cne 'C:\Lab\Candidate\WinTool.exe') {throw 'Valid relative path rejected.'};$count++
foreach($path in @('..\escape.exe','..\LabOther\escape.exe','C:\outside.exe','\\server\share\x.exe','C:relative.exe')) {Reject {Resolve-KitPath 'C:\Lab' $path}}
Assert-RootPath 'C:\Program Files\PcNinja-Issue29-0123456789abcdef0123456789abcdef' 'C:\Program Files';$count++
foreach($path in @('C:\Program Files\PcNinja-Issue29-bad','C:\Other\PcNinja-Issue29-0123456789abcdef0123456789abcdef','C:\Program Files\Other\PcNinja-Issue29-0123456789abcdef0123456789abcdef')) {Reject {Assert-RootPath $path 'C:\Program Files'}}
$proof=[pscustomobject]@{RunId='run';UserSid='sid';IsAdministrator=$false;UnsafeWritable=$true;ProtectedWritable=$false;Result='PASS'}
Assert-UserProof $proof 'run' 'sid';$count++
foreach($field in @('RunId','UserSid','IsAdministrator','UnsafeWritable','ProtectedWritable','Result')) {
    $saved=$proof.$field
    $proof.$field=if($saved -is [bool]) {-not $saved} else {'wrong'}
    Reject {Assert-UserProof $proof 'run' 'sid'}
    $proof.$field=$saved
}
foreach($pair in @(@('UnsafeOlder','OLDER'),@('UnsafeSame','SAME'),@('UnsafeNewer','NEWER'),@('ProtectedNewer','NEWER'),@('Failure','SAME'),@('Changing','OLDER'))) {
    if((Fixture-Variant $pair[0]) -cne $pair[1]) {throw 'Wrong fixture version selection.'};$count++
}
Reject {Fixture-Variant 'Unknown'}
$build=Join-Path $PSScriptRoot '..\Build-Issue29Kit.ps1'
$tokens=$null;$errors=$null;$null=[Management.Automation.Language.Parser]::ParseFile($build,[ref]$tokens,[ref]$errors)
if(@($errors).Count) {throw 'Kit factory does not parse.'};$count++
# Compile and read PE resource metadata; NEVER invoke these fixture executables.
$temp=Join-Path ([IO.Path]::GetTempPath()) ('Issue29-CompileContract-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source=Join-Path $PSScriptRoot '..\fixtures\Issue29-InstalledStub.cs'
foreach($variant in @('OLDER','SAME','NEWER')) {
    $exe=Join-Path $temp ($variant+'.exe')
    & $compiler /nologo /target:winexe /optimize+ ('/define:'+$variant) ('/out:'+$exe) $source
    if($LASTEXITCODE -ne 0) {throw 'Fixture metadata contract compilation failed.'}
    $version=(Get-Item -LiteralPath $exe).VersionInfo
    $expected=if($variant -eq 'OLDER') {'1.0.3.9000'} elseif($variant -eq 'NEWER') {'1.0.5.9000'} else {'1.0.4.3001'}
    $product=if($variant -eq 'OLDER') {'1.0.3'} elseif($variant -eq 'NEWER') {'1.0.5'} else {'1.0.4-rc.1'}
    if($version.FileVersion -cne $expected -or $version.ProductVersion -cne $product -or $version.ProductName -notlike '*TEST FIXTURE*') {throw 'Mislabelled fixture PE metadata.'};$count++
}
if(@(Get-ChildItem -LiteralPath $temp -Filter 'executed.marker' -File).Count) {throw 'Fixture was executed during metadata tests.'}
Write-Host ('Issue29 helper contracts PASS: '+$count+' cases. No registry, ACL, product Main or fixture entry point invoked. Temporary PE fixtures: '+$temp)
