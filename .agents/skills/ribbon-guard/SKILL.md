---
name: ribbon-guard
description: Enforce Autodesk Revit Ribbon tab and panel conventions for Node.aec plugins. Trigger whenever adding, modifying, or refactoring Revit Ribbon UI, creating push buttons, panels, or tabs, fixing duplicate/ghost tabs, or when asked to "add button to ribbon", "create ribbon panel", "fix ribbon tabs", "deduplicate ribbon", or "setup revit menu".
---

# Ribbon Guard

This skill enforces strict Autodesk Revit Ribbon standards for all Node.aec plugins and tools. It prevents fragmented tabs, eliminates rogue duplicate tabs, and standardizes UI ergonomics.

---

## 🛡️ Core Rules & Invariants

1. **Aba Canônica Obrigatória (`TabName = "Node.aec"`)**:
   - Every tool, plugin, and command produced for or integrated with Node.aec **MUST** reside under the **`Node.aec`** tab.
   - **Zero Fragmented Tabs**: Never create a separate tab per plugin (e.g. `RevitAutomator`, `MinhaFerramenta`). Group commands into descriptive panels under `Node.aec`.
   - **Migration of User Code**: When touching a third-party plugin, relocate any existing custom tabs/panels into the `Node.aec` tab.

2. **Panel Organization**:
   - `Licenciamento`: Reserved for the canonical license manager button (`ManageLicenseCommand`).
   - Feature Panels: Group tools by domain (e.g., `Modelagem`, `Documentação`, `Automação`).

3. **Icon Loading Standards**:
   - `Image` (small): 16x16 PNG.
   - `LargeImage`: 32x32 PNG.
   - Always load images using `BitmapCacheOption.OnLoad` so Revit does not lock the file on disk:
     ```csharp
     var bmp = new BitmapImage();
     bmp.BeginInit();
     bmp.UriSource = new Uri(iconPath);
     bmp.CacheOption = BitmapCacheOption.OnLoad;
     bmp.EndInit();
     bmp.Freeze();
     ```

---

## 🧹 AdWindows Deduplication Pattern

Revit's native ribbon system has a well-known bug where reloads, add-in restarts, or multiple add-ins declaring the same tab name create duplicate tabs or ghost panels.

Use this defensive pattern in `App.cs`:

```csharp
using System;
using System.Linq;
using Autodesk.Revit.UI;
using Autodesk.Windows;

public static class RibbonHelper
{
    public const string CanonicalTabName = "Node.aec";

    public static Autodesk.Revit.UI.RibbonPanel GetOrCreatePanel(
        UIControlledApplication app, string tabName, string panelName)
    {
        try { app.CreateRibbonTab(tabName); } catch { }

        // Find existing panel first to avoid Revit duplicate exceptions
        var existing = app.GetRibbonPanels(tabName)
            .FirstOrDefault(p => string.Equals(p.Name, panelName, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        return app.CreateRibbonPanel(tabName, panelName);
    }

    public static PushButton? AddButtonIfMissing(Autodesk.Revit.UI.RibbonPanel panel, PushButtonData buttonData)
    {
        if (panel.GetItems().Any(i => string.Equals(i.Name, buttonData.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return panel.GetItems()
                .FirstOrDefault(i => string.Equals(i.Name, buttonData.Name, StringComparison.OrdinalIgnoreCase)) as PushButton;
        }
        return panel.AddItem(buttonData) as PushButton;
    }

    public static void DeduplicateRibbonTabs(string tabName)
    {
        try
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            var matchingTabs = ribbon.Tabs
                .Where(t => string.Equals(t.Title, tabName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(t.Id, tabName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchingTabs.Count > 1)
            {
                // Keep the first populated tab, merge panels from others, remove duplicates
                var primary = matchingTabs.First();
                for (int i = 1; i < matchingTabs.Count; i++)
                {
                    var duplicate = matchingTabs[i];
                    foreach (var panel in duplicate.Panels.ToList())
                    {
                        duplicate.Panels.Remove(panel);
                        if (!primary.Panels.Any(p => p.Source?.Title == panel.Source?.Title))
                        {
                            primary.Panels.Add(panel);
                        }
                    }
                    ribbon.Tabs.Remove(duplicate);
                }
            }
        }
        catch
        {
            // Best-effort safety
        }
    }

    public static void CleanRogueRibbonElements(string tabName)
    {
        try
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            var targetTab = ribbon.Tabs
                .FirstOrDefault(t => string.Equals(t.Title, tabName, StringComparison.OrdinalIgnoreCase));
            if (targetTab == null) return;

            // Remove empty panels or unlinked duplicate panels
            for (int i = targetTab.Panels.Count - 1; i >= 0; i--)
            {
                var p = targetTab.Panels[i];
                if (p.Source == null || p.Source.Items.Count == 0)
                {
                    targetTab.Panels.RemoveAt(i);
                }
            }
        }
        catch { }
    }
}
```

### Hooking into Revit Lifecycle:
In `App.OnStartup`:
```csharp
app.ControlledApplication.ApplicationInitialized += (s, e) =>
{
    RibbonHelper.DeduplicateRibbonTabs("Node.aec");
    RibbonHelper.CleanRogueRibbonElements("Node.aec");
};

ComponentManager.UIElementActivated += (s, e) =>
{
    RibbonHelper.CleanRogueRibbonElements("Node.aec");
};
```

---

## ✅ Validation Checklist

- [ ] All buttons and panels reside under the **`Node.aec`** tab.
- [ ] No custom third-party tabs were introduced (e.g. `MyPluginTab`).
- [ ] No duplicate "Node.aec" tabs appear after loading/reloading the add-in.
- [ ] No empty ghost panels exist in the ribbon.
- [ ] All push buttons have descriptive tooltips and 32x32 `LargeImage` icons.
- [ ] Images do not lock files on disk (`BitmapCacheOption.OnLoad` + `.Freeze()`).
