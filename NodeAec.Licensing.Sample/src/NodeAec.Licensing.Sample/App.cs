using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Autodesk.Windows;
using NodeAec.Licensing.Client;
using NodeAec.Licensing.Sample.Config;

namespace NodeAec.Licensing.Sample;

/// <summary>
/// Ponto de entrada do plugin Node.aec para Autodesk Revit.
/// Exemplo mínimo e canônico:
/// Aba: Node.aec > Painel: Licenciamento > Botão: Gerenciador de Licença (com ícone Node.aec).
/// Repositório de referência: https://github.com/nodeaec/revit-plugins
/// </summary>
public class App : IExternalApplication
{
    private NodeAecLicenseClient? _licenseClient;
    public const string TabName = "Node.aec";
    public const string PanelName = "Licenciamento";

    /// <summary>
    /// Resolução de assemblies de dependências para Revit 2026 (.NET 8).
    /// </summary>
    static App()
    {
        AssemblyLoadContext.Default.Resolving += (context, name) =>
            LoadFromAddInFolder(name);
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            LoadFromAddInFolder(new AssemblyName(args.Name));
    }

    private static Assembly? LoadFromAddInFolder(AssemblyName name)
    {
        try
        {
            string dir = Path.GetDirectoryName(typeof(App).Assembly.Location)
                ?? AppDomain.CurrentDomain.BaseDirectory;
            string candidate = Path.Combine(dir, $"{name.Name}.dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }
        catch
        {
            return null;
        }
    }

    public Result OnStartup(UIControlledApplication application)
    {
        // 1. Cria a aba "Node.aec" caso não exista
        try
        {
            application.CreateRibbonTab(TabName);
        }
        catch
        {
            // Aba já existente
        }

        // 2. Limpa abas e painéis indesejados no AdWindows
        CleanRogueRibbonElements();
        DeduplicateRibbonTabs(TabName);

        string assemblyPath = typeof(App).Assembly.Location;
        string addInDir = Path.GetDirectoryName(assemblyPath) ?? AppDomain.CurrentDomain.BaseDirectory;

        // 3. Painel canônico único: "Licenciamento"
        Autodesk.Revit.UI.RibbonPanel licensePanel = GetOrCreatePanel(application, TabName, PanelName);

        // 4. Botão único: "Gerenciador de Licença" com ícone oficial da Node.aec
        var btnManage = new PushButtonData(
            "NodeAec_ManageLicense",
            "Gerenciador\nde Licença",
            assemblyPath,
            typeof(Commands.ManageLicenseCommand).FullName ?? string.Empty)
        {
            ToolTip = "Abre o Gerenciador de Licença Node.aec (ativação, status e liberação de vagas)."
        };

        // Carrega os ícones oficial da Node.aec
        LoadButtonIcons(btnManage, addInDir);

        if (!licensePanel.GetItems().Any(i => string.Equals(i.Name, btnManage.Name, StringComparison.OrdinalIgnoreCase)))
        {
            licensePanel.AddItem(btnManage);
        }

        // Deduplica e limpa novamente após adição
        CleanRogueRibbonElements();
        DeduplicateRibbonTabs(TabName);

        // 5. Registra ganchos de ciclo de vida para garantir que elementos fantasmas não reapareçam
        try
        {
            application.ControlledApplication.ApplicationInitialized += (s, e) =>
            {
                CleanRogueRibbonElements();
                DeduplicateRibbonTabs(TabName);
            };

            ComponentManager.UIElementActivated += (s, e) =>
            {
                CleanRogueRibbonElements();
            };
        }
        catch
        {
            // Silencioso se hooks de UI não estiverem disponíveis no momento
        }

        // 6. Validação em segundo plano sem bloqueio da inicialização
        try
        {
            _licenseClient = LicenseConfig.CreateClient();
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await _licenseClient.ValidateLicenseAsync(allowOffline: true);
                }
                catch
                {
                    // Falha silenciosa em background
                }
            });
        }
        catch
        {
            // Silencioso
        }

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        _licenseClient?.Dispose();
        return Result.Succeeded;
    }

    /// <summary>
    /// Configura ícones 32x32 e 16x16 no botão do Revit.
    /// </summary>
    private static void LoadButtonIcons(PushButtonData button, string addInDir)
    {
        try
        {
            string icon32Path = Path.Combine(addInDir, "Resources", "nodeaec-32.png");
            if (!File.Exists(icon32Path))
            {
                icon32Path = Path.Combine(addInDir, "nodeaec-32.png");
            }

            if (File.Exists(icon32Path))
            {
                button.LargeImage = new BitmapImage(new Uri(icon32Path, UriKind.Absolute));
            }

            string icon16Path = Path.Combine(addInDir, "Resources", "nodeaec-16.png");
            if (!File.Exists(icon16Path))
            {
                icon16Path = Path.Combine(addInDir, "nodeaec-16.png");
            }

            if (File.Exists(icon16Path))
            {
                button.Image = new BitmapImage(new Uri(icon16Path, UriKind.Absolute));
            }
        }
        catch
        {
            // Não interrompe o carregamento se houver falha de renderização do ícone
        }
    }

    /// <summary>
    /// Reutiliza ou cria o painel na aba especificada para evitar duplicações.
    /// </summary>
    private static Autodesk.Revit.UI.RibbonPanel GetOrCreatePanel(UIControlledApplication app, string tab, string panelName)
    {
        try
        {
            List<Autodesk.Revit.UI.RibbonPanel> existing = app.GetRibbonPanels(tab);
            var found = existing.FirstOrDefault(p => string.Equals(p.Name, panelName, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
        }
        catch
        {
            // Ignora falhas de consulta
        }

        return app.CreateRibbonPanel(tab, panelName);
    }

    /// <summary>
    /// Remove abas duplicadas da ribbon através do AdWindows (Autodesk.Windows).
    /// </summary>
    public static void DeduplicateRibbonTabs(string targetTitle)
    {
        try
        {
            Autodesk.Windows.RibbonControl ribbon = ComponentManager.Ribbon;
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

                    // Move os painéis se houver algum no tab duplicado
                    foreach (var panel in duplicateTab.Panels.ToList())
                    {
                        duplicateTab.Panels.Remove(panel);
                        bool alreadyInPrimary = primaryTab.Panels.Any(p =>
                            string.Equals(p.Source?.Title, panel.Source?.Title, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(p.Source?.Id, panel.Source?.Id, StringComparison.OrdinalIgnoreCase));

                        if (!alreadyInPrimary)
                        {
                            primaryTab.Panels.Add(panel);
                        }
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

    /// <summary>
    /// Remove abas e painéis indesejados ou legados (ex: "License", "Licensing", botões "License Test").
    /// Garante que apenas o layout canônico Node.aec > Licenciamento > Gerenciador de Licença permaneça visível.
    /// </summary>
    public static void CleanRogueRibbonElements()
    {
        try
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            // 1. Remove abas legadas/estranhas como "License" ou "Licensing"
            var rogueTabs = ribbon.Tabs.Where(t =>
            {
                string title = t.Title?.Trim() ?? string.Empty;
                string id = t.Id?.Trim() ?? string.Empty;
                return string.Equals(title, "License", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(title, "Licensing", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, "License", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, "Licensing", StringComparison.OrdinalIgnoreCase);
            }).ToList();

            foreach (var rogueTab in rogueTabs)
            {
                try
                {
                    rogueTab.IsVisible = false;
                    ribbon.Tabs.Remove(rogueTab);
                }
                catch { }
            }

            // 2. Remove painéis ou botões estranhos em qualquer aba (especialmente na aba Node.aec)
            foreach (var tab in ribbon.Tabs)
            {
                var panelsToRemove = new List<Autodesk.Windows.RibbonPanel>();

                foreach (var panel in tab.Panels)
                {
                    string panelTitle = panel.Source?.Title?.Trim() ?? string.Empty;
                    string panelId = panel.Source?.Id?.Trim() ?? string.Empty;

                    // Ignora o painel oficial "Licenciamento"
                    if (string.Equals(panelTitle, PanelName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(panelId, PanelName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Se for um painel com nome "License", "Licensing" ou similar
                    bool isRogueTitle =
                        string.Equals(panelTitle, "License", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(panelTitle, "Licensing", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(panelId, "License", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(panelId, "Licensing", StringComparison.OrdinalIgnoreCase);

                    // Se contiver botão ou comando "License Test", "LicenseTest" ou "revit-automator"
                    bool containsRogueItems = false;
                    if (panel.Source?.Items != null)
                    {
                        containsRogueItems = panel.Source.Items.Any(item =>
                        {
                            string itemText = (item.Text ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
                            string itemId = item.Id ?? string.Empty;
                            return itemText.IndexOf("License Test", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   itemId.IndexOf("LicenseTest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   itemId.IndexOf("revit-automator", StringComparison.OrdinalIgnoreCase) >= 0;
                        });
                    }

                    if (isRogueTitle || containsRogueItems)
                    {
                        panelsToRemove.Add(panel);
                    }
                }

                foreach (var panel in panelsToRemove)
                {
                    try
                    {
                        panel.IsVisible = false;
                        tab.Panels.Remove(panel);
                    }
                    catch { }
                }
            }
        }
        catch
        {
            // Best effort
        }
    }
}
