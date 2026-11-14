[CmdletBinding()]
param(
    [ValidateSet('Upgrade','Smoke')][string]$Mode='Upgrade',
    [switch]$ConfirmDisposableVM,
    [switch]$ConfirmReviewedCandidate,
    [Parameter(Mandatory=$true)][string]$SnapshotLabel
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

function Assert-Offer($value, [string]$version, [bool]$installed) {
    if ($value.Result -ne 'Available' -or $value.Current -ne '1.0.2' -or
        $value.Available -ne $version -or $value.Installed -ne $installed) {
        throw 'The shipped predecessor did not offer the exact RC in the expected mode.'
    }
}
function Assert-UpgradeResult($value, [string]$version, [bool]$installed) {
    if ($installed) {
        if ($value.Result -ne 'InstallerReady' -or $value.Version -ne $version) {
            throw 'The predecessor did not return the expected verified MSI.'
        }
    } elseif ($value.Result -ne 'Updated' -or $value.From -ne '1.0.2' -or $value.To -ne $version) {
        throw 'The predecessor did not complete the exact portable replacement.'
    }
}
function Assert-Hash([string]$path, [string]$expected) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected) {
        throw ('Missing file or SHA-256 mismatch: ' + $path)
    }
}
function Assert-CandidateVersion($value, $candidate) {
    if ($value.Version -ne $candidate.Version -or $value.FileVersion -ne '1.0.3.3008' -or
        $value.IsTest -ne $true -or $value.InformationalVersion -ne ($candidate.Version+'+'+$candidate.Head)) {
        throw 'Candidate runtime version, build commit or test-version policy mismatch.'
    }
}
function Run-Json([string]$exe, [string]$arguments, [string]$label) {
    $out = Join-Path $ev ($label + '.json')
    $err = Join-Path $ev ($label + '-stderr.txt')
    $p = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput $out -RedirectStandardError $err
    $steps.Add([pscustomobject]@{Name=$label;ExitCode=$p.ExitCode})
    if ($p.ExitCode -ne 0) { throw ($label + ' exit ' + $p.ExitCode) }
    return (Get-Content -LiteralPath $out -Raw | ConvertFrom-Json)
}
function Wait-Workers {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $quiet = 0
    while ($clock.Elapsed.TotalSeconds -lt 120) {
        $workers = @(Get-Process -Name 'WinTool-by-PcNinja' -ErrorAction SilentlyContinue)
        if ($workers.Count -eq 0) { $quiet++ } else { $quiet=0 }
        if ($quiet -ge 3) { return }
        Start-Sleep -Seconds 2
    }
    throw 'Installer worker still present after 120 seconds. No process was stopped.'
}
function Run-Msi([string]$action, [string]$path, [string]$label) {
    $log = Join-Path $ev ($label + '.log')
    $arguments = $action + ' "' + $path + '" /qn /norestart /L*v "' + $log + '"'
    if ($action -eq '/i') { $arguments += ' WINTOOL_ENABLE_SCHEDULE=0' }
    $p = Start-Process msiexec.exe -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    $steps.Add([pscustomobject]@{Name=$label;ExitCode=$p.ExitCode})
    if ($p.ExitCode -notin @(0,3010)) { throw ($label + ' exit ' + $p.ExitCode) }
    if ($p.ExitCode -eq 3010) { $script:rebootRequested=$true }
    Wait-Workers
}
function Assert-NoTasks {
    $own = @(Get-ScheduledTask | Where-Object { $_.TaskPath -eq '\PcNinja\' -and $_.TaskName -like 'WinTool*' })
    if ($own.Count -ne 0) { throw 'Unexpected WinTool task. Stop; do not start or remove it.' }
}
function Observe-Gui([string]$exe, [string]$label) {
    Write-Host ('Opening ' + $label + '. If offered, choose RUN PORTABLE. Do not scan, install Windows updates, save a schedule, or choose Install in the portable chooser. Close the GUI normally.')
    $p = Start-Process -FilePath $exe -PassThru -Wait
    $steps.Add([pscustomobject]@{Name=$label;ExitCode=$p.ExitCode})
    if ($p.ExitCode -ne 0) { throw ($label + ' GUI exit ' + $p.ExitCode) }
    if ((Read-Host 'Did the expected window open and display normally? Type YES') -cne 'YES') {
        throw 'GUI observation not confirmed. Exit zero alone is not acceptance.'
    }
}

if (-not $ConfirmDisposableVM -or -not $ConfirmReviewedCandidate) { throw 'Disposable snapshot VM and exact-head FABLE PASS required.' }
if ($PSVersionTable.PSEdition -ne 'Desktop' -or [IntPtr]::Size -ne 8) { throw 'Use 64-bit Windows PowerShell 5.1.' }
if ([string]::IsNullOrWhiteSpace($SnapshotLabel)) { throw 'Supply the actual rollback snapshot name.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Use an elevated administrator prompt on the VM.' }
$kit = $PSScriptRoot
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6)
$ev = Join-Path $kit ('Evidence\FinalRC-' + $stamp)
New-Item -ItemType Directory -Path $ev -Force | Out-Null
$steps = New-Object 'Collections.Generic.List[object]'
$result='REVIEW'; $failure=$null; $rebootRequested=$false
$oldFeed=$env:WINTOOL_UPDATE_FEED; $oldData=$env:WINTOOL_DATA_ROOT
try {
    # Verify the entire kit before launching any product or installer.
    foreach ($line in Get-Content -LiteralPath (Join-Path $kit 'SHA256SUMS.txt')) {
        if ($line -notmatch '^([a-fA-F0-9]{64})  ([a-zA-Z0-9_.\/-]+)$') { throw 'Invalid kit manifest line.' }
        $expected=$Matches[1]; $relative=$Matches[2]
        $full=[IO.Path]::GetFullPath((Join-Path $kit $relative))
        if (-not $full.StartsWith([IO.Path]::GetFullPath($kit).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path escaped kit.' }
        Assert-Hash $full $expected
    }
    $candidate=Get-Content -LiteralPath (Join-Path $kit 'Candidate.json') -Raw | ConvertFrom-Json
    if ($candidate.Version -ne '1.0.3-rc.8') { throw 'Wrong candidate metadata.' }
    $os=Get-CimInstance Win32_OperatingSystem
    if ($os.ProductType -ne 1 -or [int]$os.BuildNumber -lt 18362) { throw 'Windows 10 1903+ or Windows 11 client VM required.' }
    if ($Mode -eq 'Smoke' -and [int]$os.BuildNumber -ge 22000) { throw 'The separate Smoke mode is specifically the Windows 10 compatibility gate.' }
    [pscustomobject]@{Build=$os.BuildNumber;Caption=$os.Caption;Snapshot=$SnapshotLabel;Candidate=$candidate} |
        ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $ev 'identity.json') -Encoding UTF8
    $installed=Join-Path $env:ProgramFiles 'PcNinja\WinTool\WinTool-by-PcNinja.exe'
    foreach ($path in @($installed, (Join-Path $env:ProgramData 'PcNinja-WinTool'), (Join-Path $env:ProgramData 'PcNinja\WinTool'), 'HKLM:\SOFTWARE\PcNinja\WinTool', 'HKLM:\SOFTWARE\WOW6432Node\PcNinja\WinTool')) {
        if (Test-Path -LiteralPath $path) { throw 'Existing WinTool fixture detected. Restore a clean snapshot; nothing was removed.' }
    }
    Assert-NoTasks
    if (@(Get-Process -Name 'WinTool-by-PcNinja' -ErrorAction SilentlyContinue).Count) { throw 'Close existing WinTool processes first.' }
    # Registry reads only; never use Win32_Product, which can trigger repair.
    $registrations=@(foreach ($root in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall')) {
        if (Test-Path -LiteralPath $root) {
            Get-ChildItem -LiteralPath $root | ForEach-Object {
                $entry=Get-ItemProperty -LiteralPath $_.PSPath
                if ($entry.PSObject.Properties['DisplayName'] -and $entry.DisplayName -match 'WinTool by PcNinja|WinUpdate Tool') {
                    [pscustomobject]@{Name=$entry.DisplayName;Key=$_.PSChildName}
                }
            }
        }
    })
    ConvertTo-Json -InputObject $registrations | Set-Content -LiteralPath (Join-Path $ev 'preflight-registrations.json') -Encoding UTF8
    if ($registrations.Count) { throw 'Existing WinTool/WinUpdate registration. Restore a clean pre-product snapshot; no uninstall was attempted.' }
    $work=Join-Path $kit ('Work-' + $stamp)
    $feed=Join-Path $work 'Feed'; $portable=Join-Path $work 'Portable'
    New-Item -ItemType Directory -Path $feed,$portable | Out-Null
    $assets=@(); $sums=@()
    foreach ($name in @('WinTool-by-PcNinja.exe','WinTool-by-PcNinja.msi')) {
        Copy-Item -LiteralPath (Join-Path $kit ('Candidate\'+$name)) -Destination $feed
        $asset=Join-Path $feed $name
        $assets+=@{name=$name;browser_download_url=$asset}
        $sums+=(Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$name
    }
    $sumPath=Join-Path $feed 'SHA256SUMS.txt'; $sums | Set-Content -LiteralPath $sumPath -Encoding ASCII
    $assets+=@{name='SHA256SUMS.txt';browser_download_url=$sumPath}
    $release=@{tag_name=('v'+$candidate.Version);draft=$false;prerelease=$true;assets=$assets}
    $feedPath=Join-Path $feed 'releases.json'
    ConvertTo-Json -InputObject @($release) -Depth 6 | Set-Content -LiteralPath $feedPath -Encoding UTF8
    $env:WINTOOL_UPDATE_FEED=$feedPath; $env:WINTOOL_DATA_ROOT=$null
    $exe=Join-Path $portable 'WinTool-by-PcNinja.exe'
    if ($Mode -eq 'Upgrade') {
        Copy-Item -LiteralPath (Join-Path $kit 'Baseline-1.0.2\WinTool-by-PcNinja.exe') -Destination $portable
        $offer=Run-Json $exe 'app-update check --test-versions --json' 'portable-offer'
        Assert-Offer $offer $candidate.Version $false
        $upgrade=Run-Json $exe 'app-update install --yes --test-versions --json' 'portable-update'
        Assert-UpgradeResult $upgrade $candidate.Version $false
        Assert-Hash (Join-Path $portable 'WinTool-by-PcNinja-v1.0.2.9000.exe') $candidate.BaselineExeSha256
    } else {
        Copy-Item -LiteralPath (Join-Path $kit 'Candidate\WinTool-by-PcNinja.exe') -Destination $portable
    }
    Assert-Hash $exe $candidate.UniversalSha256
    $version=Run-Json $exe 'version --json' 'portable-version'
    Assert-CandidateVersion $version $candidate
    Observe-Gui $exe 'candidate-portable'
    Assert-NoTasks
    if ($Mode -eq 'Upgrade') {
        Run-Msi '/i' (Join-Path $kit 'Baseline-1.0.2\WinTool-by-PcNinja.msi') 'baseline-install'
        $offer=Run-Json $installed 'app-update check --test-versions --json' 'installed-offer'
        Assert-Offer $offer $candidate.Version $true
        $upgrade=Run-Json $installed 'app-update install --yes --test-versions --json' 'installed-download'
        Assert-UpgradeResult $upgrade $candidate.Version $true
        Assert-Hash $upgrade.Installer $candidate.MsiSha256
        # Run only the hash-checked MSI path, never the returned command string.
        Run-Msi '/i' $upgrade.Installer 'rc-msi-upgrade'
    } else {
        Run-Msi '/i' (Join-Path $kit 'Candidate\WinTool-by-PcNinja.msi') 'rc-clean-install'
    }
    Assert-Hash $installed $candidate.PlainSha256
    $version=Run-Json $installed 'version --json' 'installed-version'
    Assert-CandidateVersion $version $candidate
    Observe-Gui $installed 'candidate-installed'
    Assert-NoTasks
    Run-Msi '/x' (Join-Path $kit 'Candidate\WinTool-by-PcNinja.msi') 'rc-uninstall'
    if (Test-Path -LiteralPath $installed) { throw 'Installed EXE remained after uninstall.' }
    Assert-NoTasks
    $result='SCOPED_PASS'
} catch {
    $failure=$_.Exception.Message; Write-Warning $failure
} finally {
    $env:WINTOOL_UPDATE_FEED=$oldFeed; $env:WINTOOL_DATA_ROOT=$oldData
    [pscustomobject]@{Result=$result;Mode=$Mode;Error=$failure;Steps=@($steps.ToArray());RebootRequested=$rebootRequested;NoWindowsUpdateOperation=$true;PublicFeedTested=$false} |
        ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $ev 'summary.json') -Encoding UTF8
    $zip=$ev+'.zip'
    try { Compress-Archive -LiteralPath $ev -DestinationPath $zip; if (-not (Test-Path -LiteralPath $zip -PathType Leaf)) { throw 'Archive missing.' }; Write-Host ('Return privately: '+$zip) }
    catch { Write-Warning ('Archive failed; preserve folder: '+$ev) }
    Write-Host ('Final RC: '+$result+'. No merge/tag/release. Restore the snapshot after preserving evidence.')
}
if ($result -ne 'SCOPED_PASS') { exit 1 }
