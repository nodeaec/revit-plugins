<#
.SYNOPSIS
  Builds, stages, zips, optionally compiles the Inno Setup installer (.exe),
  and optionally installs the Node.aec Connector Revit add-in.

.NOTES
  Setup.exe generation requires Inno Setup 6 (ISCC.exe on PATH-adjacent
  standard location). Without it, only the .zip is produced - no failure.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/release.ps1 -Version 0.1.1
  powershell -ExecutionPolicy Bypass -File scripts/release.ps1 -Version 0.1.1 -Install
#>
param(
  [string]$Version = "0.1.1",
  [string]$RevitYear = "2026",
  [string]$Configuration = "Release",
  [switch]$Install,
  [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$ConnectorRoot = Split-Path $PSScriptRoot -Parent
$Sln = Join-Path $ConnectorRoot "NodeAec.Connector.sln"
$Project = Join-Path $ConnectorRoot "src\NodeAec.Connector\NodeAec.Connector.csproj"
$DllName = "NodeAec.Connector.dll"
$AddinTemplate = Join-Path $ConnectorRoot "src\NodeAec.Connector\NodeAec.Connector.addin"
$ReleaseDir = Join-Path $ConnectorRoot "release"
$StageDir = Join-Path $ReleaseDir "stage\NodeAec.Connector"
$ZipPath = Join-Path $ReleaseDir "NodeAec.Connector-$Version-R$RevitYear.zip"

# Resolve o TFM lendo Directory.Build.props via MSBuild, em vez de repetir a matriz de
# anos aqui — bin\<ano>\<config>\<tfm> é a única fonte de verdade e muda com RevitYear.
$TargetFramework = (& dotnet msbuild $Project -getProperty:TargetFramework -p:RevitYear=$RevitYear -nologo -v:quiet |
  Select-Object -Last 1).ToString().Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($TargetFramework)) {
  throw "Não foi possível resolver TargetFramework para RevitYear=$RevitYear (exit $LASTEXITCODE)."
}
$OutDir = Join-Path $ConnectorRoot "src\NodeAec.Connector\bin\$RevitYear\$Configuration\$TargetFramework"

if (-not $SkipBuild) {
  Write-Host "==> dotnet build $Sln -c $Configuration -p:RevitYear=$RevitYear ($TargetFramework)"
  & dotnet build $Sln -c $Configuration -p:RevitYear=$RevitYear
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

# System.Security.Cryptography.ProtectedData tem destino diferente por família de runtime:
#  * net48 (Revit 2023/2024): vem de pacote NuGet e DEVE ser copiada para o add-in;
#  * net8.0-windows/net10.0-windows (Revit 2025+): o assembly faz parte do runtime
#    Microsoft.WindowsDesktop.App do host, é framework-provided e por isso nem aparece
#    no diretório de saída (copiá-lo seria redundante).
if ($TargetFramework -eq "net48") {
  $dpapiDll = Join-Path $StageDir "System.Security.Cryptography.ProtectedData.dll"
  if (-not (Test-Path $dpapiDll)) {
    throw "Missing DPAPI dependency in build output: System.Security.Cryptography.ProtectedData.dll was not copied to $OutDir."
  }
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

# Optional: Inno Setup .exe installer (double-click friendly for end users).
# Requires Inno Setup 6 (https://jrsoftware.org/isdl.php). When ISCC.exe is
# not found the .zip above remains the only artifact - no failure.
$setupName = "NodeAec.Connector-$Version-Setup.exe"
$iscc = @(
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
  "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if ($iscc) {
  $versionNum = if ($Version -match '^\d+\.\d+$') { "$Version.0" } else { $Version }
  $iss = Join-Path $PSScriptRoot "installer.iss"
  Write-Host "==> ISCC $iss"
  & $iscc "/DAppVersion=$Version" "/DAppVersionNum=$versionNum" "/DRevitYear=$RevitYear" "/DPayloadStage=$StageDir" "/O$ReleaseDir" $iss
  if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

  $setupPath = Join-Path $ReleaseDir $setupName
  if (-not (Test-Path $setupPath)) { throw "Setup output not found: $setupPath" }
  $setupHash = (Get-FileHash $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
  "$setupHash  $setupName" | Out-File "$setupPath.sha256" -Encoding ascii
  Write-Host "==> setup: $setupPath"
  Write-Host "    sha256: $setupHash"
}
else {
  Write-Warning "Inno Setup 6 (ISCC.exe) not found - only the .zip was generated. Install from https://jrsoftware.org/isdl.php to also build $setupName."
}

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
