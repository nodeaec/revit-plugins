---
name: licensing-integrate
description: Integrate Node.aec licensing into an existing Autodesk Revit plugin (.NET 8 for Revit 2025/2026+ or .NET Framework 4.8 for Revit 2020-2024). Trigger whenever asked to "integrate node.aec licensing", "add license check", "protect revit command", "configure licensing client", "setup node.aec licensing", "activate license in plugin", or when modifying licensing logic in a Revit add-in.
---

# Licensing Integrate (Hub & Micro-Gate Architecture)

This skill guides the end-to-end integration of Node.aec licensing into an Autodesk Revit add-in using the **Hub & Micro-Gate (NodeAecGate)** pattern. It enforces a strict pre-implementation interview (Grilling Phase), fixed architectural invariants, and drop-in code recipes.

---

## 🛑 Phase 0: Mandatory Grilling Protocol

> [!IMPORTANT]
> **NEVER generate code or alter the user's project before executing this interview.**
> Present each question with context and your explicit recommended default, and wait for the user's alignment.

### Non-Negotiable Invariants (Do NOT Ask the User):
1. **Hub & Micro-Gate Architecture**: Partner plugins DO NOT implement HTTP clients, cloud auth, or license manager UIs. All account SSO, seat management, lease sync, and manual key activations are handled by the central **Node.aec Connector**.
2. **Instant Local Validation (< 1ms, Zero Network)**: Plugins validate authorization locally using `NodeAecGate.Validate(slug)`. Never perform HTTP requests on Revit commands or application startup.
3. **Ribbon Tab**: The plugin MUST ALWAYS place its panels and buttons in the canonical **`Node.aec`** tab (`TabName = "Node.aec"`). If the plugin currently has its own ribbon tab or scattered commands, migrate all panels and commands to the `Node.aec` tab. Never create a separate tab.
4. **Offline Tolerance**: Leases stored in `%APPDATA%\NodeAec\entitlements.lease` allow up to 30 days of seamless offline operation.

---

### Grilling Questions

#### 1. Revit Version & .NET Target Framework
- **Question**: *"Which Autodesk Revit versions does this plugin target?"*
- **Context**: Revit 2025 and 2026+ require **.NET 8** (`net8.0-windows`). Revit 2020–2024 require **.NET Framework 4.8** (`net48`).
- **Options**:
  - `A` (Recommended for modern plugins): .NET 8 exclusive (`<TargetFramework>net8.0-windows</TargetFramework>`).
  - `B`: .NET Framework 4.8 exclusive (`<TargetFramework>net48</TargetFramework>`).
  - `C`: Multi-targeting (`<TargetFrameworks>net8.0-windows;net48</TargetFrameworks>`).

#### 2. Product Slug on Node.aec
- **Question**: *"What is the registered product slug on Node.aec (`https://nodeaec.com.br/products/{slug}`)? If not yet registered, what slug should we use?"*
- **Recommendation**: Use the exact kebab-case slug (e.g. `"revit-automator"`, `"parametric-doors"`).

#### 3. Ribbon Panel Name
- **Question**: *"What panel name should be used for your tools under the canonical 'Node.aec' Ribbon tab?"*
- **Recommendation**: Use a concise thematic name (e.g. `"Automação"`, `"Modelagem"`, `"Ferramentas"`).

#### 4. Commercial Command Gating Policy
- **Question**: *"How should commercial commands behave when no valid license is active on this workstation?"*
- **Options**:
  - `A` (Recommended): **Hard Gate** — Show an informative `TaskDialog` with a direct command link to "Abrir Node.aec Connector..." and cancel command execution (`Result.Cancelled`).
  - `B`: **Trial / Grace Mode** — Allow execution with limitations (e.g. element count limit, watermark, or remaining trial days).

---

## 📋 Integration Recipes

### Recipe 1: Project File Dependencies (`.csproj`)

Add Windows DPAPI and ensure dependency DLLs are staged:

```xml
<ItemGroup>
  <!-- DPAPI local token decryption -->
  <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" />
</ItemGroup>

<PropertyGroup>
  <!-- Copies NuGet dependency DLLs to the add-in folder, never RevitAPI*.dll -->
  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
</PropertyGroup>
```

> [!NOTE]
> `System.Security.Cryptography.ProtectedData.dll` is a staged payload item **only for
> `net48`** (Revit 2023/2024), where the NuGet package is its only source. On
> `net8.0-windows`/`net10.0-windows` (Revit 2025+) the assembly ships inside the host's
> `Microsoft.WindowsDesktop.App` runtime (verified in the 8.0.31 and 10.0.12 packs), so it
> must **not** be copied into the add-in folder. Encrypting with `ProtectedData` is
> unaffected on every target.

### Recipe 2: Core Files to Copy
Copy from `github.com/nodeaec/revit-plugins` (`NodeAec.Connector/src/NodeAec.Connector/`):
1. `Gate/NodeAecGate.cs` → Micro-SDK validation class.
2. `Hardware/HardwareId.cs` → Machine SHA-256 fingerprint helper.
3. Support types those two depend on: `Cryptography/` (Ed25519 lease verification),
   `Storage/` (DPAPI lease read) and `Models/` (lease claims).

The consuming side (calling `NodeAecGate.Validate(slug)` from a partner command) is shown in
`NodeAec.Connector/README.md`, section *Como Integrar Plugins Parceiros com o `NodeAecGate`*.

### Recipe 3: Application Lifecycle (`App.cs`)

```csharp
using System;
using System.Linq;
using Autodesk.Revit.UI;

public class App : IExternalApplication
{
    public const string TabName = "Node.aec";
    public const string PanelName = "Minhas Ferramentas";

    public Result OnStartup(UIControlledApplication application)
    {
        // 1. Invariant: Always use the canonical "Node.aec" tab
        try { application.CreateRibbonTab(TabName); } catch { }

        // Defensive acquisition: prevent ArgumentException if panel already exists
        var panel = application.GetRibbonPanels(TabName)
            .FirstOrDefault(p => string.Equals(p.Name, PanelName, StringComparison.OrdinalIgnoreCase))
            ?? application.CreateRibbonPanel(TabName, PanelName);

        var btn = new PushButtonData(
            "MeuPlugin_Comando",
            "Executar\nComando",
            typeof(App).Assembly.Location,
            "SeuNamespace.Commands.MeuComandoComercial"
        )
        {
            ToolTip = "Executa a ferramenta comercial protegida via NodeAecGate."
        };
        panel.AddItem(btn);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}
```

### Recipe 4: Protecting an `IExternalCommand`

```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SeuNamespace.Gate;

[Transaction(TransactionMode.Manual)]
public class MeuComandoComercial : IExternalCommand
{
    private const string ProductSlug = "meu-produto-slug";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // 1. Instant local check (< 1ms, zero network)
        var check = NodeAecGate.Validate(ProductSlug);
        if (!check.IsLicensed)
        {
            var dialog = new TaskDialog("Node.aec // Licença Necessária")
            {
                MainInstruction = "Licença ativa necessária para executar esta ferramenta.",
                MainContent = $"{check.Message}\n\nAbra o Node.aec Connector na Ribbon para entrar com sua conta ou ativar sua licença.",
                CommonButtons = TaskDialogCommonButtons.Close
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir Node.aec Connector...");

            if (dialog.Show() == TaskDialogResult.CommandLink1)
            {
                NodeAecGate.OpenConnector();
            }

            return Result.Cancelled;
        }

        // --- 2. Execute commercial logic ---
        TaskDialog.Show("Sucesso", $"Executando com licença {check.LicenseType}.");
        return Result.Succeeded;
    }
}
```

---

## ✅ Validation Checklist

- [ ] Project builds with **0 errors**.
- [ ] `System.Security.Cryptography.ProtectedData.dll` is present in the add-in output folder **only when targeting `net48`** (Revit 2023/2024); on `net8.0-windows`/`net10.0-windows` it is provided by `Microsoft.WindowsDesktop.App` and must be absent.
- [ ] No `RevitAPI*.dll` assemblies copied into output.
- [ ] No HTTP clients or login forms implemented inside the partner plugin.
- [ ] All plugin commands and panels are consolidated under the **`Node.aec`** tab.
- [ ] Executing a protected command without license prompts the TaskDialog directing to the **Node.aec Connector**.
- [ ] Offline operation functions properly with the local master lease.
