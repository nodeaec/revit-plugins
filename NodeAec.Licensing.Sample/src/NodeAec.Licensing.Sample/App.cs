using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Autodesk.Windows;
using NodeAec.Licensing.Sample.Commands;

namespace NodeAec.Licensing.Sample;

/// <summary>
/// Ponto de entrada do plugin de exemplo Node.aec para Autodesk Revit.
/// Demonstra a arquitetura simplificada Hub & Micro-Gate:
/// - O plugin registra seus comandos na aba canônica "Node.aec", em seu próprio painel ("Exemplo").
/// - A governança de conta, SSO, leases e Ribbon principal pertencem ao Node.aec Connector.
/// - O plugin valida autorização em comandos via micro-SDK NodeAecGate (< 1ms, zero rede).
/// </summary>
public class App : IExternalApplication
{
    public const string TabName = "Node.aec";
    public const string PanelName = "Exemplo";

    public Result OnStartup(UIControlledApplication application)
    {
        // 1. Cria a aba "Node.aec" caso ainda não exista
        try
        {
            application.CreateRibbonTab(TabName);
        }
        catch
        {
            // Aba já criada pelo Connector ou por outro add-in
        }

        // 2. Garante deduplicação de abas no AdWindows
        DeduplicateRibbonTabs(TabName);

        string assemblyPath = typeof(App).Assembly.Location;
        string addInDir = Path.GetDirectoryName(assemblyPath) ?? AppDomain.CurrentDomain.BaseDirectory;

        // 3. Cria o painel do plugin dentro da aba "Node.aec"
        Autodesk.Revit.UI.RibbonPanel samplePanel = GetOrCreatePanel(application, TabName, PanelName);

        // 4. Adiciona o botão da funcionalidade comercial
        var btnSample = new PushButtonData(
            "NodeAec_SampleFeature",
            "Executar\nFerramenta",
            assemblyPath,
            typeof(SampleFeatureCommand).FullName ?? string.Empty)
        {
            ToolTip = "Executa a ferramenta de automação de exemplo protegida pelo micro-SDK NodeAecGate."
        };

        LoadButtonIcons(btnSample, addInDir);

        if (!samplePanel.GetItems().Any(i => string.Equals(i.Name, btnSample.Name, StringComparison.OrdinalIgnoreCase)))
        {
            samplePanel.AddItem(btnSample);
        }

        // 5. Deduplicação defensiva final
        DeduplicateRibbonTabs(TabName);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }

    private static void LoadButtonIcons(PushButtonData button, string addInDir)
    {
        try
        {
            string icon32Path = Path.Combine(addInDir, "Resources", "nodeaec-32.png");
            if (!File.Exists(icon32Path)) icon32Path = Path.Combine(addInDir, "nodeaec-32.png");
            if (File.Exists(icon32Path)) button.LargeImage = new BitmapImage(new Uri(icon32Path, UriKind.Absolute));

            string icon16Path = Path.Combine(addInDir, "Resources", "nodeaec-16.png");
            if (!File.Exists(icon16Path)) icon16Path = Path.Combine(addInDir, "nodeaec-16.png");
            if (File.Exists(icon16Path)) button.Image = new BitmapImage(new Uri(icon16Path, UriKind.Absolute));
        }
        catch
        {
            // Best effort
        }
    }

    private static Autodesk.Revit.UI.RibbonPanel GetOrCreatePanel(UIControlledApplication app, string tab, string panelName)
    {
        try
        {
            var existing = app.GetRibbonPanels(tab);
            var found = existing.FirstOrDefault(p => string.Equals(p.Name, panelName, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
        }
        catch
        {
            // Ignora exceções na consulta
        }

        return app.CreateRibbonPanel(tab, panelName);
    }

    public static void DeduplicateRibbonTabs(string targetTitle)
    {
        try
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            var matchingTabs = ribbon.Tabs
                .Where(t => string.Equals(t.Title, targetTitle, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(t.Id, targetTitle, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchingTabs.Count > 1)
            {
                var primaryTab = matchingTabs[0];

                for (int i = 1; i < matchingTabs.Count; i++)
                {
                    var duplicateTab = matchingTabs[i];
                    foreach (var panel in duplicateTab.Panels.ToList())
                    {
                        duplicateTab.Panels.Remove(panel);
                        bool alreadyInPrimary = primaryTab.Panels.Any(p =>
                            string.Equals(p.Source?.Title, panel.Source?.Title, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(p.Source?.Id, panel.Source?.Id, StringComparison.OrdinalIgnoreCase));

                        if (!alreadyInPrimary) primaryTab.Panels.Add(panel);
                    }
                    duplicateTab.IsVisible = false;
                    ribbon.Tabs.Remove(duplicateTab);
                }
            }
        }
        catch
        {
            // Best effort
        }
    }
}
