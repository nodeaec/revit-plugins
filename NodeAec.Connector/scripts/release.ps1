<#
.SYNOPSIS
  Builds, stages, zips, and optionally installs the Node.aec Connector Revit add-in.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/release.ps1 -Version 0.1
  powershell -ExecutionPolicy Bypass -File scripts/release.ps1 -Version 0.1 -Install
#>
param(
  [string]$Version = "0.1",
  [string]$RevitYear = "2026",
  [string]$Configuration = "Release",
  [switch]$Install,
  [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$ConnectorRoot = Split-Path $PSScriptRoot -Parent
$Sln = Join-Path $ConnectorRoot "NodeAec.Connector.sln"
$OutDir = Join-Path $ConnectorRoot "src\NodeAec.Connector\bin\$Configuration\net8.0-windows"
$DllName = "NodeAec.Connector.dll"
$AddinTemplate = Join-Path $ConnectorRoot "src\NodeAec.Connector\NodeAec.Connector.addin"
$ReleaseDir = Join-Path $ConnectorRoot "release"
$StageDir = Join-Path $ReleaseDir "stage\NodeAec.Connector"
$ZipPath = Join-Path $ReleaseDir "NodeAec.Connector-$Version-R$RevitYear.zip"

if (-not $SkipBuild) {
  Write-Host "==> dotnet build $Sln -c $Configuration"
  & dotnet build $Sln -c $Configuration
  if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
}

$dll = Join-Path $OutDir $DllName
if (-not (Test-Path $dll)) { throw "Build output not found: $dll" }

# Stage: clean + copy runtime payload
if (Test-Path $StageDir) { Remove-Item $StageDir -Recurse -Force }
New-Item $StageDir -ItemType Directory -Force | Out-Null
Get-ChildItem $OutDir -Filter *.dll |
  Where-Object { $_.Name -notlike "RevitAPI*" -and $_.Name -ne "AdWindows.dll" -and $_.Name -notlike "UIFramework*" } |
  Copy-Item -Destination $StageDir -Force

# Stage resource icons
if (Test-Path (Join-Path $OutDir "Resources")) {
  Copy-Item (Join-Path $OutDir "Resources") $StageDir -Recurse -Force
}
Get-ChildItem $OutDir -Filter *.png -ErrorAction SilentlyContinue |
  Copy-Item -Destination $StageDir -Force

$dpapiDll = Join-Path $StageDir "System.Security.Cryptography.ProtectedData.dll"
if (-not (Test-Path $dpapiDll)) {
  throw "Missing DPAPI dependency in build output: System.Security.Cryptography.ProtectedData.dll was not copied to $OutDir."
}
if (Test-Path (Join-Path $ConnectorRoot "README.md")) {
  Copy-Item (Join-Path $ConnectorRoot "README.md") (Join-Path $StageDir "README.md") -Force
}

# Stage the .addin with absolute path
$installDir = "C:\ProgramData\Autodesk\Revit\Addins\$RevitYear\NodeAec.Connector"
[xml]$addin = Get-Content $AddinTemplate
$addin.RevitAddIns.AddIn.Assembly = "$installDir\$DllName"
$addin.Save((Join-Path $StageDir "NodeAec.Connector.addin"))

# Zip + checksum
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
New-Item $ReleaseDir -ItemType Directory -Force | Out-Null
Compress-Archive -Path "$StageDir\*" -DestinationPath $ZipPath -Force
$hash = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $ZipPath -Leaf)" | Out-File "$ZipPath.sha256" -Encoding ascii
Write-Host "==> release: $ZipPath"
Write-Host "    sha256: $hash"

if ($Install) {
  $addinsDir = "$env:ProgramData\Autodesk\Revit\Addins\$RevitYear"
  $targetDir = Join-Path $addinsDir "NodeAec.Connector"
  Write-Host "==> install to $targetDir"
  New-Item $targetDir -ItemType Directory -Force | Out-Null
  Copy-Item "$StageDir\*.dll" $targetDir -Force
  if (Test-Path (Join-Path $StageDir "Resources")) {
    Copy-Item (Join-Path $StageDir "Resources") $targetDir -Recurse -Force
  }
  Get-ChildItem $StageDir -Filter *.png -ErrorAction SilentlyContinue |
    Copy-Item -Destination $targetDir -Force
  Copy-Item (Join-Path $StageDir "NodeAec.Connector.addin") $addinsDir -Force
  Write-Host "==> installed Node.aec Connector. Restart Revit $RevitYear."
}
