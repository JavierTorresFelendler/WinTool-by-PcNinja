[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CandidateRoot,
    [Parameter(Mandatory=$true)][string]$BaselineMsi,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2
if ($PSVersionTable.PSEdition -ne 'Desktop' -or [IntPtr]::Size -ne 8) { throw 'Use 64-bit Windows PowerShell 5.1 to build the kit.' }
$output=[IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Output must be a fresh directory. Nothing was removed.' }
$pins=@{
    'Universal\WinTool-by-PcNinja.exe'='0bcac1446f395673456968d191be7e4405793d790dae0177fbdd1a186f2289d5'
    'Plain\WinTool-by-PcNinja.exe'='2e01d91419942cfa41dac81c45062e55741a735e2f9aefeaa1435474af0e275d'
    'MSI\WinTool-by-PcNinja.msi'='d591f4d869831136e657264c7e8386b819f0c8659ffa6b35a1ad84f6cb0c515f'
}
foreach($relative in $pins.Keys) {
    if((Get-FileHash -LiteralPath (Join-Path $CandidateRoot $relative)).Hash -ne $pins[$relative]) {throw 'Candidate hash mismatch.'}
}
if((Get-FileHash -LiteralPath $BaselineMsi).Hash -ne '02f2059e28827bb4577a33b298ff501ffdf04ddcd6a5f43e323155d2c19dd75e') {throw 'Shipped 1.0.3 MSI hash mismatch.'}
foreach($directory in @('Candidate','Baseline','Library','Fixtures')) {New-Item -ItemType Directory -Path (Join-Path $output $directory) -Force | Out-Null}
Copy-Item -LiteralPath (Join-Path $CandidateRoot 'Universal\WinTool-by-PcNinja.exe') -Destination (Join-Path $output 'Candidate\WinTool-by-PcNinja.exe')
Copy-Item -LiteralPath (Join-Path $CandidateRoot 'MSI\WinTool-by-PcNinja.msi') -Destination (Join-Path $output 'Candidate\WinTool-by-PcNinja.msi')
Copy-Item -LiteralPath $BaselineMsi -Destination (Join-Path $output 'Baseline\WinTool-by-PcNinja.msi')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Issue29-Launch.ps1') -Destination $output
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\..\docs\testing\ISSUE-29-KIT.md') -Destination (Join-Path $output 'README.txt')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures\Issue29-InstalledStub.cs') -Destination (Join-Path $output 'Fixtures\InstalledStub.cs')
# Reflection-only: read embedded bytes; never load executable code or invoke Main.
$app=[Reflection.Assembly]::ReflectionOnlyLoadFrom([IO.Path]::GetFullPath((Join-Path $CandidateRoot 'Universal\WinTool-by-PcNinja.exe')))
$stream=$app.GetManifestResourceStream('WinTool.Embedded.WinTool.Core.dll')
if($null -eq $stream) {throw 'Core resource missing.'}
$target=[IO.File]::Create((Join-Path $output 'Library\WinTool.Core.dll'))
try {$stream.CopyTo($target)} finally {$target.Dispose();$stream.Dispose()}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
foreach($variant in @('OLDER','SAME','NEWER')) {
    $exe=Join-Path $output ('Fixtures\'+$variant+'.exe')
    & $compiler /nologo /target:winexe /optimize+ ('/define:'+$variant) ('/out:'+$exe) (Join-Path $output 'Fixtures\InstalledStub.cs')
    if($LASTEXITCODE -ne 0) {throw 'Fixture compile failed.'}
    $expected=if($variant -eq 'OLDER') {'1.0.3.9000'} elseif($variant -eq 'NEWER') {'1.0.5.9000'} else {'1.0.4.3001'}
    if((Get-Item -LiteralPath $exe).VersionInfo.FileVersion -ne $expected) {throw 'Fixture version mismatch.'}
}
$head=(& git -C (Join-Path $PSScriptRoot '..\..') rev-parse HEAD)
if($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {throw 'Cannot pin helper head.'}
[pscustomobject]@{ProductHead='16e663a1c550baf5a5cea207814806f303994d3b';HelperHead=$head;Run=43761429507;Version='1.0.4-rc.1';PlainSha256=$pins['Plain\WinTool-by-PcNinja.exe']} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'Candidate.json') -Encoding UTF8
$lines=@(Get-ChildItem -LiteralPath $output -Recurse -File | Sort-Object FullName | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()+'  '+$_.FullName.Substring($output.Length+1).Replace('\','/')
})
$lines | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ASCII
Write-Host ('Kit built without executing any EXE, installer, registry or ACL fixture: '+$output)
