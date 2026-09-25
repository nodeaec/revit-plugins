---
name: revit-build-validate
description: Build and validate Autodesk Revit C# solutions and add-ins (.NET 8 / Revit 2025-2026+ or .NET 4.8 / Revit 2020-2024). Trigger whenever compiling, building, running tests, or validating Revit C# projects, or when asked to "build the plugin", "compile revit addin", "validate build", "check for compilation errors", or "verify dependencies".
---

# Revit Build & Validate

This skill standardizes the build and verification process for Autodesk Revit plugins. It ensures clean compilation, prevents dependency pollution, and confirms runtime isolation.

---

## 🛠️ Build Commands

Execute the build using the .NET CLI:

```powershell
# Standard Release compilation
dotnet build <SolutionOrProject>.sln -c Release

# Or targeting a specific project
dotnet build src\NodeAec.Licensing.Sample\NodeAec.Licensing.Sample.csproj -c Release
```

---

## 🔍 Acceptance Criteria & Invariants

A build is valid ONLY when ALL of the following pass:

### 1. Zero Compilation Errors
- The build must exit with **0 Error(s)**.
- Any syntax, type mismatch, or missing symbol errors must be resolved immediately.

### 2. Revit API Dependency Isolation
- Revit assemblies (`RevitAPI.dll`, `RevitAPIUI.dll`, `AdWindows.dll`) are provided by Revit at runtime.
- For local path references, they MUST have `<Private>false</Private>`.
- For NuGet `<PackageReference>` (e.g. `Revit_All_Main_Versions_API_x64`), you MUST specify `PrivateAssets="all"` and `ExcludeAssets="runtime"`. Otherwise, when `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>` is enabled, NuGet will copy Revit API DLLs into `bin/Release/`.
- **CRITICAL**: Revit API DLLs must **NEVER** appear in the output directory (`bin/Release/...`) or staged zip. Packaging Revit API DLLs causes silent crashes and type loading errors in Revit.

### 3. Runtime Dependency Presence
- All non-Revit dependencies MUST be present in the output folder. The one exception is `System.Security.Cryptography.ProtectedData.dll`: it belongs in the output **only for `net48`** (Revit 2023/2024), where the NuGet package is its only source. On `net8.0-windows`/`net10.0-windows` it is framework-provided by `Microsoft.WindowsDesktop.App` and is correctly absent from `bin/`.
- Ensure the project file contains:
  ```xml
  <PropertyGroup>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>
  ```

### 4. Assembly Resolution Hook in `App.cs`
- Add-in assemblies loaded by Revit may fail to resolve peer dependencies in the add-in folder unless hooked in the static constructor:
  ```csharp
  static App()
  {
      string dir = Path.GetDirectoryName(typeof(App).Assembly.Location)
          ?? AppDomain.CurrentDomain.BaseDirectory;

  #if NET8_0_OR_GREATER
      System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) =>
      {
          string candidate = Path.Combine(dir, $"{name.Name}.dll");
          return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
      };
  #else
      AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
      {
          string candidate = Path.Combine(dir, $"{new AssemblyName(args.Name).Name}.dll");
          return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
      };
  #endif
  }
  ```

---

## ⚠️ Common Warnings & Diagnosis

- **MSB3277 (Architecture / Reference Mismatch)**:
  - *Symptom*: Warning that `RevitAPI.dll` depends on different versions of RevitUI components.
  - *Cause*: Standard Autodesk assembly binding redirection in Revit 2025/2026. Safe to ignore if `<Private>false</Private>` is maintained.
- **CS8600 / CS8602 / CS8604 (Nullable References)**:
  - *Action*: Inspect nullable variables, add safe null checks (`?`, `??`, `string.IsNullOrWhiteSpace`) rather than suppressing warnings with `!`.

---

## ✅ Validation Checklist

- [ ] `dotnet build` executes with **0 errors**.
- [ ] No `RevitAPI*.dll` or `AdWindows.dll` exists in the `bin/Release/...` directory.
- [ ] `System.Security.Cryptography.ProtectedData.dll` is present in `bin/<RevitYear>/<Configuration>/<tfm>/` **for `net48` (Revit 2023/2024)**, and absent there for `net8.0-windows`/`net10.0-windows` (provided by `Microsoft.WindowsDesktop.App`).
- [ ] The assembly resolution hook is defined in the `App` static constructor.
- [ ] Output DLL can be located and staged by packaging scripts.
