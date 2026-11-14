[CmdletBinding()]
param(
    [ValidateSet('Smoke','Security')][string]$Phase='Smoke',
    [switch]$ConfirmDisposableVM,
    [switch]$ConfirmReviewedCandidate,
    [Parameter(Mandatory=$true)][string]$SnapshotLabel,
    [string]$DownloadedExe
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version 2

function Assert-StableVersion($value, $candidate) {
    if ($value.Version -ne '1.0.3' -or $value.FileVersion -ne '1.0.3.9000' -or
        $value.MsiVersion -ne '1.0.3999' -or $value.IsTest -ne $false -or
        $value.InformationalVersion -ne ('1.0.3+'+$candidate.Head)) {
        throw 'Stable runtime identity does not match the reviewed build.'
    }
}
function Save-Json($value, [string]$name) {
    ConvertTo-Json -InputObject $value -Depth 9 | Set-Content -LiteralPath (Join-Path $ev $name) -Encoding UTF8
}

if (-not $ConfirmDisposableVM -or -not $ConfirmReviewedCandidate) { throw 'Disposable rollback VM and exact-head FABLE PASS required.' }
if ($PSVersionTable.PSEdition -ne 'Desktop' -or [IntPtr]::Size -ne 8) { throw 'Use 64-bit Windows PowerShell 5.1.' }
if ([string]::IsNullOrWhiteSpace($SnapshotLabel)) { throw 'Supply actual rollback snapshot name.' }
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Elevated administrator prompt required on the VM.' }
$kit=$PSScriptRoot
$ev=Join-Path $kit ('Evidence\Stable-'+$Phase+'-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $ev -Force | Out-Null
$steps=New-Object 'Collections.Generic.List[object]'
$result='REVIEW';$failure=$null;$rebootRequested=$false
$oldFeed=$env:WINTOOL_UPDATE_FEED;$oldData=$env:WINTOOL_DATA_ROOT
try {
    # Load only reviewed function declarations, never the RC helper's executable body.
    $library=Join-Path $kit 'Final-RC.ps1'
    if ((Get-FileHash -LiteralPath $library).Hash -ne '6895351755d1e1c13368a189aadf7a62255fb8d21d23d32b60bf96168417f8ab') { throw 'Reviewed helper library hash mismatch.' }
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile($library,[ref]$tokens,[ref]$errors)
    if (@($errors).Count) { throw 'Helper library parse failure.' }
    foreach ($name in @('Assert-Hash','Run-Json','Wait-Workers','Run-Msi','Assert-NoTasks','Observe-Gui')) {
        $definition=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
        if (-not $definition) { throw ('Missing reviewed function '+$name) }
        . ([scriptblock]::Create($definition.Extent.Text))
    }
    foreach ($line in Get-Content -LiteralPath (Join-Path $kit 'SHA256SUMS.txt')) {
        if ($line -notmatch '^([a-fA-F0-9]{64})  ([a-zA-Z0-9_.\/-]+)$') { throw 'Invalid manifest line.' }
        $expected=$Matches[1];$relative=$Matches[2]
        $full=[IO.Path]::GetFullPath((Join-Path $kit $relative))
        if (-not $full.StartsWith([IO.Path]::GetFullPath($kit).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path escaped kit.' }
        Assert-Hash $full $expected
    }
    $candidate=Get-Content -LiteralPath (Join-Path $kit 'Candidate.json') -Raw | ConvertFrom-Json
    if ($candidate.Version -ne '1.0.3' -or $candidate.Head -notmatch '^[0-9a-f]{40}$') { throw 'Wrong stable metadata.' }
    $os=Get-CimInstance Win32_OperatingSystem
    if ($os.ProductType -ne 1 -or [int]$os.BuildNumber -lt 18362) { throw 'Windows10 1903+ or Windows11 client required.' }
    Save-Json ([pscustomobject]@{Caption=$os.Caption;Build=$os.BuildNumber;Snapshot=$SnapshotLabel;Candidate=$candidate;Phase=$Phase}) 'identity.json'
    # Stable smoke uses normal paths and no test override, even in its parent process.
    $env:WINTOOL_UPDATE_FEED=$null;$env:WINTOOL_DATA_ROOT=$null
    $universal=Join-Path $kit 'Candidate\WinTool-by-PcNinja.exe'
    $msi=Join-Path $kit 'Candidate\WinTool-by-PcNinja.msi'
    Assert-Hash $universal $candidate.UniversalSha256
    Assert-Hash $msi $candidate.MsiSha256
    if ($Phase -eq 'Smoke') {
        $installed=Join-Path $env:ProgramFiles 'PcNinja\WinTool\WinTool-by-PcNinja.exe'
        foreach ($path in @($installed,(Join-Path $env:ProgramData 'PcNinja-WinTool'),(Join-Path $env:ProgramData 'PcNinja\WinTool'),'HKLM:\SOFTWARE\PcNinja\WinTool','HKLM:\SOFTWARE\WOW6432Node\PcNinja\WinTool')) {
            if (Test-Path -LiteralPath $path) { throw 'Existing fixture. Restore a clean snapshot; no cleanup attempted.' }
        }
        $registrations=@(foreach ($root in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall')) {
            if (Test-Path -LiteralPath $root) {Get-ChildItem -LiteralPath $root | ForEach-Object {
                $entry=Get-ItemProperty -LiteralPath $_.PSPath
                if ($entry.PSObject.Properties['DisplayName'] -and $entry.DisplayName -match 'WinTool by PcNinja|WinUpdate Tool') { [pscustomobject]@{Name=$entry.DisplayName;Key=$_.PSChildName} }
            }}
        })
        Save-Json $registrations 'preflight-registrations.json'
        if ($registrations.Count) { throw 'Existing WinTool/WinUpdate product registration. Use a clean snapshot.' }
        Assert-NoTasks
        if (@(Get-Process -Name 'WinTool-by-PcNinja' -ErrorAction SilentlyContinue).Count) { throw 'Existing WinTool process; stop.' }
        $version=Run-Json $universal 'version --json' 'portable-version'
        Assert-StableVersion $version $candidate
        Observe-Gui $universal 'stable-portable'
        Assert-NoTasks
        Run-Msi '/i' $msi 'stable-install'
        Assert-Hash $installed $candidate.PlainSha256
        $version=Run-Json $installed 'version --json' 'installed-version'
        Assert-StableVersion $version $candidate
        Observe-Gui $installed 'stable-installed'
        Assert-NoTasks
        Run-Msi '/x' $msi 'stable-uninstall'
        if (Test-Path -LiteralPath $installed) { throw 'Installed EXE remained after uninstall.' }
        Assert-NoTasks
        $result='SCOPED_SMOKE_PASS'
    } else {
        # One security visit only. Preserve observations; never claim a cloud verdict.
        if ([string]::IsNullOrWhiteSpace($DownloadedExe)) { throw 'Security phase requires the browser-downloaded and Explorer-extracted universal EXE path.' }
        $download=[IO.Path]::GetFullPath($DownloadedExe)
        Assert-Hash $download $candidate.UniversalSha256
        $zone=Get-Content -LiteralPath $download -Stream Zone.Identifier -ErrorAction Stop | Out-String
        Save-Json ([pscustomobject]@{Path=$download;Zone=$zone}) 'download-origin.json'
        if ($zone -notmatch '(?m)^ZoneId=[34]\s*$') { throw 'Internet-zone marking missing. Do not add/unblock it; use an actual browser download and Explorer extraction.' }
        $status=Get-MpComputerStatus
        Save-Json ($status | Select-Object AMServiceEnabled,AntivirusEnabled,RealTimeProtectionEnabled,AntivirusSignatureVersion,AntivirusSignatureLastUpdated,AMEngineVersion,AMProductVersion) 'defender-before.json'
        Save-Json (Get-MpPreference | Select-Object MAPSReporting,SubmitSamplesConsent,DisableBlockAtFirstSeen,CloudBlockLevel,ExclusionPath,ExclusionProcess,ExclusionExtension) 'defender-preferences.json'
        if (-not $status.AntivirusEnabled -or -not $status.RealTimeProtectionEnabled) { throw 'Defender not active. Preserve evidence; do not change security settings.' }
        $scanStart=Get-Date
        Save-Json ([pscustomobject]@{StartedUtc=$scanStart.ToUniversalTime();ScanPath=(Join-Path $kit 'Candidate')}) 'scan-request.json'
        Start-MpScan -ScanType CustomScan -ScanPath (Join-Path $kit 'Candidate')
        $eventError=$null;$events=@()
        try {$events=@(Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-Windows Defender/Operational';StartTime=$scanStart.AddSeconds(-2);Id=1000,1001,1002,1116,1117} -ErrorAction Stop |
            ForEach-Object {[pscustomobject]@{Id=$_.Id;RecordId=$_.RecordId;TimeUtc=$_.TimeCreated.ToUniversalTime();Xml=$_.ToXml()}})} catch {$eventError=$_.Exception.Message}
        Save-Json ([pscustomobject]@{Events=$events;QueryError=$eventError}) 'defender-events.json'
        Save-Json @(Get-MpThreatDetection) 'defender-detections.json'
        $smartSettings=@(foreach ($key in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer','HKLM:\SOFTWARE\Policies\Microsoft\Windows\System')) {
            if (Test-Path -LiteralPath $key) {
                $settings=Get-ItemProperty -LiteralPath $key
                foreach ($property in @('SmartScreenEnabled','EnableSmartScreen','ShellSmartScreenLevel')) {
                    if ($settings.PSObject.Properties[$property]) { [pscustomobject]@{Key=$key;Name=$property;Value=$settings.$property} }
                }
            }
        })
        Save-Json $smartSettings 'smartscreen-settings.json'
        Write-Host 'Explorer will select the hash-checked internet download. Double-click it yourself; record any SmartScreen warning BEFORE proceeding. Choose RUN PORTABLE if the app opens, then close it. Do not install or scan. If blocked, do not bypass or disable protection.'
        Start-Process explorer.exe -ArgumentList ('/select,"'+$download+'"')
        $observation=Read-Host 'Type NORMAL, UNRECOGNIZED, BLOCKED, or UNAVAILABLE for the actual SmartScreen observation'
        if ($observation -cnotin @('NORMAL','UNRECOGNIZED','BLOCKED','UNAVAILABLE')) { throw 'Unrecognised observation; no automatic acceptance.' }
        Save-Json ([pscustomobject]@{Observation=$observation;OwnerAttestation=$true;FileSha256=$candidate.UniversalSha256;CloudVerdictEstablished=$false}) 'smartscreen-observation.json'
        Read-Host 'Close WinTool if it opened, preserve any warning screenshot, then press Enter' | Out-Null
        Wait-Workers
        $result='SECURITY_EVIDENCE_COLLECTED_REVIEW_REQUIRED'
    }
} catch {$failure=$_.Exception.Message;Write-Warning $failure}
finally {
    $env:WINTOOL_UPDATE_FEED=$oldFeed;$env:WINTOOL_DATA_ROOT=$oldData
    Save-Json ([pscustomobject]@{Result=$result;Error=$failure;Phase=$Phase;Steps=@($steps.ToArray());RebootRequested=$rebootRequested;HelperInvokedWindowsUpdate=$false;PublicFeedTested=$false;CloudVerdictEstablished=$false}) 'summary.json'
    $zip=$ev+'.zip'
    try {Compress-Archive -LiteralPath $ev -DestinationPath $zip;if (-not (Test-Path -LiteralPath $zip -PathType Leaf)) {throw 'Archive missing.'};Write-Host ('Return privately: '+$zip)}
    catch {Write-Warning ('Archive failed; preserve folder: '+$ev)}
    Write-Host ('Stable preparation: '+$result+'. No merge/tag/release. Preserve evidence before restoring or formatting.')
}
if ($result -eq 'REVIEW') {exit 1}
