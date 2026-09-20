---
name: licensing-integrate
description: Integrate Node.aec licensing into an existing Autodesk Revit plugin (.NET 8 for Revit 2025/2026+ or .NET Framework 4.8 for Revit 2020-2024). Trigger whenever asked to "integrate node.aec licensing", "add license check", "protect revit command", "configure licensing client", "setup nodeaec licensing", "activate license in plugin", or when modifying licensing logic in a Revit add-in.
---

# Licensing Integrate

This skill guides the end-to-end integration of Node.aec licensing into an Autodesk Revit add-in. It enforces a strict pre-implementation interview (Grilling Phase), fixed architectural invariants, and drop-in code recipes.

---

## 🛑 Phase 0: Mandatory Grilling Protocol

> [!IMPORTANT]
> **NEVER generate code or alter the user's project before executing this interview.**
> Present each question with context and your explicit recommended default, and wait for the user's alignment.

### Non-Negotiable Invariants (Do NOT Ask the User):
1. **API Endpoint**: ALWAYS production (`https://api.nodeaec.com.br`). Never ask about localhost or dev environments.
2. **Ribbon Tab**: The plugin MUST ALWAYS reside in the **`Node.aec`** tab. If the user's plugin currently has its own ribbon tab or scattered commands, migrate all panels and commands to the `Node.aec` tab. Never create a separate tab.
3. **Offline Tolerance**: Ed25519 token signatures allow up to 30 days of offline validation. Always specify `allowOffline: true` on daily checks.
4. **Key Security**: Never embed private keys. The client ships only the public SPKI PEM key (`DefaultPublicKeyPem`).

---

### Grilling Questions

#### 1. Revit Version & .NET Target Framework
- **Question**: *"Which Autodesk Revit versions does this plugin target?"*
- **Context**: Revit 2025 and 2026+ require **.NET 8** (`net8.0-windows`). Revit 2020–2024 require **.NET Framework 4.8** (`net48`).
- **Options**:
  - `A` (Recommended for modern plugins): .NET 8 exclusive (`<TargetFramework>net8.0-windows</TargetFramework>`).
  - `B`: .NET Framework 4.8 exclusive (`<TargetFramework>net48</TargetFramework>`).
  - `C`: Multi-targeting (`<TargetFrameworks>net8.0-windows;net48</TargetFrameworks>`).

#### 2. RevitAPI Assembly Resolution
- **Question**: *"How are RevitAPI and RevitAPIUI references resolved in your repository?"*
- **Options**:
  - `A`: Local installation paths (e.g. `C:\Program Files\Autodesk\Revit 2026\RevitAPI.dll` with `<Private>false</Private>`).
  - `B` (Recommended for teams/CI): NuGet packages (e.g. `Revit_All_Main_Versions_API_x64` or `Autodesk.Revit.SDK`).
  - `C`: Shared build environment variable (e.g. `$(RevitInstallDir)`).

#### 3. Product Slug on Node.aec
- **Question**: *"What is the registered product slug on Node.aec (`https://nodeaec.com.br/products/{slug}`)? If not yet registered, do you want automatic catalog resolution?"*
- **Recommendation**: Use the exact slug. If the product isn't listed yet, use automatic catalog fallback (`GET /products`) which dynamically resolves the product name and web link.

#### 4. Commercial Command Gating Policy
- **Question**: *"How should commercial commands behave when no valid license is active?"*
- **Options**:
  - `A` (Recommended): **Hard Gate** — Show an informative `TaskDialog` with a direct button to "Open License Manager..." and cancel command execution.
  - `B`: **Trial / Grace Mode** — Allow execution with limitations (e.g. element count limit, watermark, or remaining trial days).
  - `C`: **Feature Flagging** — Gate only premium automation tools while keeping basic utilities accessible.

#### 5. Local Storage Scope (DPAPI)
- **Question**: *"Will this plugin be installed on personal/dedicated workstations or shared multi-user lab machines?"*
- **Options**:
  - `A` (Recommended for individual seats): `%APPDATA%\NodeAec\Licenses\` with DPAPI `DataProtectionScope.CurrentUser`.
  - `B` (For shared workstations): `%PROGRAMDATA%\NodeAec\Licenses\` with DPAPI `DataProtectionScope.LocalMachine`.

---

## 📋 Integration Recipes

### Recipe 1: Project File Dependencies (`.csproj`)

Add Windows DPAPI and ensure dependency DLLs are staged:

```xml
<ItemGroup>
  <!-- DPAPI local token encryption -->
  <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" />

  <!-- CRITICAL: When using Revit NuGet packages with CopyLocalLockFileAssemblies,
       you MUST set PrivateAssets="all" and ExcludeAssets="runtime" to prevent RevitAPI*.dll from leaking into the output -->
  <!-- Example:
  <PackageReference Include="Revit_All_Main_Versions_API_x64" Version="2026.0.0" PrivateAssets="all" ExcludeAssets="runtime" />
  -->
</ItemGroup>

<PropertyGroup>
  <!-- Ensures ProtectedData.dll is copied to the addin output folder -->
  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
</PropertyGroup>
```

### Recipe 2: Core Files to Copy
Copy from `github.com/nodeaec/revit-plugins` (`NodeAec.Licensing.Sample/src/NodeAec.Licensing.Sample/`):
1. `Client/NodeAecLicenseClient.cs` → `Client/` in target project.
2. `Config/LicenseConfig.cs` → `Config/` in target project.
3. `Commands/ManageLicenseCommand.cs` → `Commands/` in target project.
4. `UI/LicenseManagerWindow.cs` → `UI/` in target project.
5. `Resources/` → Official 16px and 32px Node.aec PNG icons.

### Recipe 3: Application Lifecycle (`App.cs`)

```csharp
using System;
using System.Linq;
using Autodesk.Revit.UI;
using NodeAec.Licensing.Client;
using NodeAec.Licensing.Sample.Config;

public class App : IExternalApplication
{
    private NodeAecLicenseClient? _licenseClient;

    public Result OnStartup(UIControlledApplication application)
    {
        // 1. Invariant: Always use the canonical "Node.aec" tab
        const string tabName = "Node.aec";
        try { application.CreateRibbonTab(tabName); } catch { }

        // Defensive acquisition: prevent ArgumentException if panel or button already exists
        var panel = application.GetRibbonPanels(tabName)
            .FirstOrDefault(p => string.Equals(p.Name, "Licenciamento", StringComparison.OrdinalIgnoreCase))
            ?? application.CreateRibbonPanel(tabName, "Licenciamento");

        const string buttonId = "NodeAec_ManageLicense";
        if (!panel.GetItems().Any(i => i.Name == buttonId))
        {
            var btnManage = new PushButtonData(
                buttonId,
                "Gerenciador\nde Licença",
                typeof(App).Assembly.Location,
                "YourNamespace.Commands.ManageLicenseCommand"
            )
            {
                ToolTip = "Gerencia sua licença Node.aec (ativação, status e postos)."
            };
            panel.AddItem(btnManage);
        }

        // 2. Non-blocking background heartbeat
        _licenseClient = LicenseConfig.CreateClient();
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await _licenseClient.ValidateLicenseAsync(allowOffline: true);
            }
            catch { }
        });

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        _licenseClient?.Dispose();
        return Result.Succeeded;
    }
}
```

### Recipe 4: Protecting an `IExternalCommand`

> [!NOTE]
> **STA Thread Safety**: The `Execute` method of `IExternalCommand` runs on Revit's main UI STA thread. WPF windows and `TaskDialog` must be shown from an STA thread. If validating licenses in background tasks, marshal any dialog display to the Revit UI thread.

```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Licensing.Sample.Config;
using NodeAec.Licensing.Sample.UI;

[Transaction(TransactionMode.Manual)]
public class CommercialCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // Fast offline-first check
        using var client = LicenseConfig.CreateClient();
        var check = client.ValidateLicenseAsync(allowOffline: true).GetAwaiter().GetResult();

        if (!check.IsValid)
        {
            var dialog = new TaskDialog("Node.aec // Licença Necessária")
            {
                MainInstruction = "Licença ativa necessária para executar este recurso.",
                MainContent = check.ErrorMessage ?? "Ative o produto para utilizar as ferramentas completas.",
                CommonButtons = TaskDialogCommonButtons.Close
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir Gerenciador de Licença...");

            if (dialog.Show() == TaskDialogResult.CommandLink1)
            {
                LicenseManagerWindow.Open(commandData.Application);
            }

            return Result.Cancelled;
        }

        // --- Execute your commercial logic here ---
        return Result.Succeeded;
    }
}
```

---

## ✅ Validation Checklist

- [ ] Project builds with **0 errors**.
- [ ] `System.Security.Cryptography.ProtectedData.dll` is present in the add-in output folder.
- [ ] No `RevitAPI*.dll` assemblies copied into output.
- [ ] API URL is set to production (`https://api.nodeaec.com.br`).
- [ ] All plugin commands and panels are consolidated under the **`Node.aec`** tab.
- [ ] Executing a protected command without license prompts the activation modal.
- [ ] Activating with a valid key displays the real product name and `↗ Ver no site` link.
- [ ] Offline operation functions properly with cached Ed25519 lease.
