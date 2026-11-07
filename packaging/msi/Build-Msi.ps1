<#
  Builds WinTool-by-PcNinja.msi with Hebrew and Spanish embedded as language transforms.
  The base MSI is English (direct msiexec, IT deployment). WinTool starts the installer with
  TRANSFORMS=:1037 (Hebrew) or :3082 (Spanish) to match its own language.
  All three builds share one ProductCode, so a transform changes only texts and the language.
#>
param(
  [Parameter(Mandatory)] [string] $ExePath,
  [Parameter(Mandatory)] [string] $AssetsDir,
  [Parameter(Mandatory)] [string] $ProductVersion,
  [Parameter(Mandatory)] [string] $OutputMsi
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$work = Join-Path ([IO.Path]::GetTempPath()) ("wintool-msi-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
$productCode = '{' + [guid]::NewGuid().ToString().ToUpperInvariant() + '}'

$cultures = @(
  @{ Culture = 'en-US'; Lcid = 1033 },
  @{ Culture = 'he-IL'; Lcid = 1037 },
  @{ Culture = 'es-ES'; Lcid = 3082 }
)
Push-Location $here
try {
  foreach ($c in $cultures) {
    $c.Msi = Join-Path $work ("WinTool-" + $c.Culture + ".msi")
    $log = & wix build -arch x64 -culture $c.Culture -loc ("WinTool." + $c.Culture + ".wxl") `
      -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext `
      -d "ExePath=$ExePath" -d "AssetsDir=$AssetsDir" -d "ProductVersion=$ProductVersion" -d "ProductCode=$productCode" `
      -o $c.Msi Package.wxs 2>&1
    $code = $LASTEXITCODE
    $log | ForEach-Object { Write-Host $_ }
    if ($code -ne 0) {
      $log | Where-Object { "$_" -match 'error' } | Select-Object -First 20 | ForEach-Object { Write-Host ("::error title=wix " + $c.Culture + "::" + ("$_" -replace '%', '%25')) }
      exit $code
    }
  }
} finally { Pop-Location }

# Windows Installer automation (late bound): make each transform and embed it as a sub-storage named by its LCID.
$installer = New-Object -ComObject WindowsInstaller.Installer
function Call($target, [string] $name, [object[]] $arguments) { $target.GetType().InvokeMember($name, 'InvokeMethod', $null, $target, $arguments) }
function GetProp($target, [string] $name, [object[]] $arguments) { $target.GetType().InvokeMember($name, 'GetProperty', $null, $target, $arguments) }
function SetProp($target, [string] $name, [object[]] $arguments) { [void]$target.GetType().InvokeMember($name, 'SetProperty', $null, $target, $arguments) }

$base = $cultures[0].Msi
$baseDb = Call $installer 'OpenDatabase' @($base, 1)          # transact
foreach ($c in $cultures | Select-Object -Skip 1) {
  $mst = Join-Path $work ([string]$c.Lcid + ".mst")
  $langDb = Call $installer 'OpenDatabase' @($c.Msi, 0)       # read-only
  [void](Call $langDb 'GenerateTransform' @($baseDb, $mst))
  [void](Call $langDb 'CreateTransformSummaryInfo' @($baseDb, $mst, 0, 0))
  [System.Runtime.InteropServices.Marshal]::ReleaseComObject($langDb) | Out-Null
  $view = Call $baseDb 'OpenView' @('SELECT `Name`,`Data` FROM `_Storages`')
  $record = Call $installer 'CreateRecord' @(2)
  SetProp $record 'StringData' @(1, [string]$c.Lcid)
  [void](Call $record 'SetStream' @(2, $mst))
  [void](Call $view 'Execute' @($record))
  [void](Call $view 'Modify' @(3, $record))                    # msiViewModifyAssign
  [void](Call $view 'Close' @())
  Write-Host ("Embedded transform " + $c.Lcid + " (" + $c.Culture + "), " + (Get-Item $mst).Length + " bytes")
}
# Template: the languages this package supports.
$summary = GetProp $baseDb 'SummaryInformation' @(1)
SetProp $summary 'Property' @(7, 'x64;1033,1037,3082')
[void](Call $summary 'Persist' @())
[void](Call $baseDb 'Commit' @())
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($baseDb) | Out-Null

# Check: both transforms are inside.
$check = Call $installer 'OpenDatabase' @($base, 0)
foreach ($lcid in 1037, 3082) {
  $view = Call $check 'OpenView' @("SELECT ``Name`` FROM ``_Storages`` WHERE ``Name``='$lcid'")
  [void](Call $view 'Execute' @())
  if ($null -eq (Call $view 'Fetch' @())) { Write-Host "::error title=msi::transform $lcid missing"; exit 1 }
  [void](Call $view 'Close' @())
}
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($check) | Out-Null

New-Item -ItemType Directory -Force (Split-Path $OutputMsi) | Out-Null
Copy-Item $base $OutputMsi -Force
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
Write-Host ("Built " + $OutputMsi + " with languages 1033 (base), 1037, 3082")
