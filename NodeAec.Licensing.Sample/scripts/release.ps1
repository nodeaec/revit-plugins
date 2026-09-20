<#
.SYNOPSIS
  Builds, stages, zips, and optionally installs the Revit 2026 sample add-in.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/release.ps1 -Version 1.0.0
  powershell -ExecutionPolicy Bypass -File scripts/release.ps1 -Version 1.0.0 -Install
#>
param(
  [string]$Version = "1.0.0",
  [string]$RevitYear = "2026",
  [string]$Configuration = "Release",
  [switch]$Install,
  [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$SampleRoot = Split-Path $PSScriptRoot -Parent
$Sln = Join-Path $SampleRoot "NodeAec.Licensing.Sample.sln"
$OutDir = Join-Path $SampleRoot "src\NodeAec.Licensing.Sample\bin\$Configuration\net8.0-windows"
$DllName = "NodeAec.Licensing.Sample.dll"
$AddinTemplate = Join-Path $SampleRoot "src\NodeAec.Licensing.Sample\NodeAec.Licensing.Sample.addin"
$ReleaseDir = Join-Path $SampleRoot "release"
$StageDir = Join-Path $ReleaseDir "stage\NodeAec.Licensing.Sample"
$ZipPath = Join-Path $ReleaseDir "NodeAec.Licensing.Sample-$Version-R$RevitYear.zip"

if (-not $SkipBuild) {
  Write-Host "==> dotnet build $Sln -c $Configuration"
  & dotnet build $Sln -c $Configuration
  if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }
}

$dll = Join-Path $OutDir $DllName
if (-not (Test-Path $dll)) { throw "Build output not found: $dll" }

# Stage: clean + copy runtime payload (plugin DLL + all NuGet dependency
# DLLs such as System.Security.Cryptography.ProtectedData.dll, never RevitAPI).
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
  throw "Missing DPAPI dependency in build output: System.Security.Cryptography.ProtectedData.dll was not copied to $OutDir. Ensure <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies> is set in the .csproj."
}
Copy-Item (Join-Path $SampleRoot "README.md") (Join-Path $StageDir "README.md") -Force

# Stage the .addin with an absolute Assembly path for the target Revit year.
$installDir = "C:\ProgramData\Autodesk\Revit\Addins\$RevitYear\NodeAec.Licensing.Sample"
[xml]$addin = Get-Content $AddinTemplate
$addin.RevitAddIns.AddIn.Assembly = "$installDir\$DllName"
$addin.Save((Join-Path $StageDir "NodeAec.Licensing.Sample.addin"))

# Zip + checksum.
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
New-Item $ReleaseDir -ItemType Directory -Force | Out-Null
Compress-Archive -Path "$StageDir\*" -DestinationPath $ZipPath -Force
$hash = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $ZipPath -Leaf)" | Out-File "$ZipPath.sha256" -Encoding ascii
Write-Host "==> release: $ZipPath"
Write-Host "    sha256: $hash"

if ($Install) {
  $addinsDir = "$env:ProgramData\Autodesk\Revit\Addins\$RevitYear"
  $targetDir = Join-Path $addinsDir "NodeAec.Licensing.Sample"
  Write-Host "==> install to $targetDir (requires admin)"
  New-Item $targetDir -ItemType Directory -Force | Out-Null
  Copy-Item "$StageDir\*.dll" $targetDir -Force
  if (Test-Path (Join-Path $StageDir "Resources")) {
    Copy-Item (Join-Path $StageDir "Resources") $targetDir -Recurse -Force
  }
  Get-ChildItem $StageDir -Filter *.png -ErrorAction SilentlyContinue |
    Copy-Item -Destination $targetDir -Force
  Copy-Item (Join-Path $StageDir "NodeAec.Licensing.Sample.addin") $addinsDir -Force
  Write-Host "==> installed. Restart Revit $RevitYear."
}
