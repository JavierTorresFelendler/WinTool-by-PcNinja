$ErrorActionPreference='Stop'
$helper=Join-Path $PSScriptRoot '..\Final-RC.ps1'
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($helper,[ref]$tokens,[ref]$errors)
if (@($errors).Count) { throw ($errors.Message -join '; ') }
foreach ($name in @('Assert-Offer','Assert-UpgradeResult','Assert-CandidateVersion')) {
    $definition=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
    if (-not $definition) { throw ('Missing guard '+$name) }
    . ([scriptblock]::Create($definition.Extent.Text))
}
$offer=[pscustomobject]@{Result='Available';Current='1.0.2';Available='1.0.3-rc.8';Installed=$false}
Assert-Offer $offer '1.0.3-rc.8' $false
foreach ($field in @('Result','Current','Available','Installed')) {
    $saved=$offer.$field
    $offer.$field=if ($field -eq 'Installed') {$true} else {'wrong'}
    $rejected=$false
    try { Assert-Offer $offer '1.0.3-rc.8' $false } catch { $rejected=$true }
    if (-not $rejected) { throw ('Incorrect offer passed: '+$field) }
    $offer.$field=$saved
}
Assert-UpgradeResult ([pscustomobject]@{Result='Updated';From='1.0.2';To='1.0.3-rc.8'}) '1.0.3-rc.8' $false
Assert-UpgradeResult ([pscustomobject]@{Result='InstallerReady';Version='1.0.3-rc.8'}) '1.0.3-rc.8' $true
foreach ($installed in @($false,$true)) {
    $rejected=$false
    try { Assert-UpgradeResult ([pscustomobject]@{Result='UpToDate';Version='1.0.3-rc.8';From='1.0.2';To='1.0.3-rc.8'}) '1.0.3-rc.8' $installed } catch { $rejected=$true }
    if (-not $rejected) { throw 'UpToDate incorrectly accepted as an upgrade.' }
}
$candidate=[pscustomobject]@{Version='1.0.3-rc.8';Head=('a'*40)}
$runtime=[pscustomobject]@{Version='1.0.3-rc.8';FileVersion='1.0.3.3008';IsTest=$true;InformationalVersion=('1.0.3-rc.8+'+('a'*40))}
Assert-CandidateVersion $runtime $candidate
foreach ($field in @('Version','FileVersion','IsTest','InformationalVersion')) {
    $saved=$runtime.$field
    $runtime.$field=if ($field -eq 'IsTest') {$false} else {'wrong'}
    $rejected=$false
    try { Assert-CandidateVersion $runtime $candidate } catch { $rejected=$true }
    if (-not $rejected) { throw ('Incorrect candidate passed: '+$field) }
    $runtime.$field=$saved
}
Write-Host 'Final RC helper: 14 client-only guard contracts passed; no product, installer, task or WUA invocation.'
