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

# Make each transform and embed it in the base MSI as a sub-storage named by its LCID (msi.dll, MsiTransforms.cs).
Add-Type -Path (Join-Path $here 'MsiTransforms.cs')
$base = $cultures[0].Msi
# The English base becomes code page neutral (its text is plain ASCII); otherwise Windows Installer
# keeps 1252 when a transform is applied and Hebrew (1255) turns into '?'.
[MsiTransforms]::SetCodepage([string]$base, '0', (Join-Path $work 'neutral'))
foreach ($c in $cultures) { Write-Host ($c.Culture + " codepage " + [MsiTransforms]::Codepage([string]$c.Msi, (Join-Path $work ("cp-" + $c.Culture)))) }
foreach ($c in $cultures | Select-Object -Skip 1) {
  $mst = Join-Path $work ([string]$c.Lcid + ".mst")
  [MsiTransforms]::Generate([string]$c.Msi, [string]$base, [string]$mst)
  [MsiTransforms]::Embed([string]$base, [string]$c.Lcid, [string]$mst)
  Write-Host ("Embedded transform " + $c.Lcid + " (" + $c.Culture + "), " + (Get-Item $mst).Length + " bytes")
}
# Template: the languages this package supports.
[MsiTransforms]::SetTemplate([string]$base, 'x64;1033,1037,3082')
foreach ($lcid in '1037', '3082') {
  if (-not [MsiTransforms]::HasStorage([string]$base, $lcid)) { Write-Host "::error title=msi::transform $lcid missing"; exit 1 }
}

New-Item -ItemType Directory -Force (Split-Path $OutputMsi) | Out-Null
Copy-Item $base $OutputMsi -Force
# Diagnostics only (not released): the Hebrew build on its own, beside the work folder.
$diag = Join-Path ([IO.Path]::GetTempPath()) 'wintool-msi-diag'
New-Item -ItemType Directory -Force $diag | Out-Null
Copy-Item $cultures[1].Msi (Join-Path $diag 'WinTool-he-IL.msi') -Force
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
Write-Host ("Built " + $OutputMsi + " with languages 1033 (base), 1037, 3082")
