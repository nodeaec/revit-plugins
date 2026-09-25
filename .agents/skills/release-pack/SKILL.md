---
name: release-pack
description: Package, stage, hash, and install Autodesk Revit plugins for distribution or local testing. Trigger whenever creating a release, packaging the plugin, generating zip archives, deploying to Revit, or when asked to "package the plugin", "create release", "run release.ps1", "install addin into revit", "deploy plugin to revit", or "generate release zip".
---

# Release Pack

This skill guides packaging and deploying Autodesk Revit plugins into standardized `.zip` release distributions and installing them locally into Revit's Addins environment.

---

## 🚀 Packaging Workflow (`scripts/release.ps1`)

The repository provides an automated PowerShell release script at `scripts/release.ps1`.

### 1. Basic Release Packaging (Generates `.zip` + SHA-256)

```powershell
Set-Location plugin
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.1.1
```

Output:
- Staged folder: `release\stage\NodeAec.Connector\`
- Archive: `release\NodeAec.Connector-0.1.1-R2026.zip`
- Console output: Displays the computed SHA-256 hash.

### 2. Packaging + Automatic Local Installation

```powershell
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.1.1 -Install
```

This builds, packages, and deploys the add-in to Revit's discovery directories:
- **Manifest**: `C:\ProgramData\Autodesk\Revit\Addins\2026\NodeAec.Connector.addin` (must be at the root of `Addins\<Year>\` for Revit discovery).
- **Runtime Payload**: `C:\ProgramData\Autodesk\Revit\Addins\2026\NodeAec.Connector\` (assemblies, dependency DLLs, resources).

---

## 🔍 Staging Invariants (What Goes Into the Package)

A compliant release package must contain:

1. **Add-in Manifest (`.addin`)**:
   - Must contain the correct absolute `Assembly` path pointing to the installed DLL.
   - Contains a unique `AddInId` GUID and `FullClassName` matching `App`.
2. **Plugin DLL**:
   - `NodeAec.Connector.dll` (or target plugin DLL).
3. **Runtime Dependencies**:
   - `System.Security.Cryptography.ProtectedData.dll` — **`net48` targets only** (Revit
     2023/2024). On `net8.0-windows`/`net10.0-windows` (Revit 2025+) it is provided by the
     host's `Microsoft.WindowsDesktop.App` runtime and must NOT be staged.
4. **Resources & Assets**:
   - `Resources/` containing PNG icons.
   - `README.md` (human documentation included with the release).
5. **FORBIDDEN Binaries**:
   - `RevitAPI.dll`, `RevitAPIUI.dll`, `AdWindows.dll`, `UIFramework*.dll` must NEVER be included. The script enforces:
     `Where-Object { $_.Name -notlike "RevitAPI*" -and $_.Name -ne "AdWindows.dll" -and $_.Name -notlike "UIFramework*" }`

---

## 🛠️ Script Parameter Reference

| Parameter | Type | Default | Purpose |
|---|---|---|---|
| `-Version` | String | `"1.0.0"` | SemVer release version for the archive name |
| `-RevitYear` | String | `"2026"` | Target Autodesk Revit year |
| `-Configuration` | String | `"Release"` | Build configuration (`Release` or `Debug`) |
| `-Install` | Switch | `false` | When set, deploys files into `%ProgramData%\Autodesk\Revit\Addins\<Year>\` |
| `-SkipBuild` | Switch | `false` | Skips `dotnet build` if already compiled |

---

## ✅ Validation Checklist

- [ ] `release.ps1` runs without terminating errors.
- [ ] Staging directory (`release/stage/...`) is clean and free of leftover build artifacts.
- [ ] No `RevitAPI*.dll` assemblies are present in the stage or `.zip` file.
- [ ] `System.Security.Cryptography.ProtectedData.dll` is in the stage and `.zip` **for `net48` (Revit 2023/2024)**, and absent from them for `net8.0-windows`/`net10.0-windows`.
- [ ] The generated `.zip` has a valid SHA-256 hash printed.
- [ ] When `-Install` is used, the `.addin` and assemblies exist in `C:\ProgramData\Autodesk\Revit\Addins\<Year>\`.
- [ ] Launching Revit recognizes the newly installed add-in without load warnings.
