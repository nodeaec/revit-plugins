using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
#if NET8_0_OR_GREATER
using System.Runtime.Loader;
#endif
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Autodesk.Windows;
using NodeAec.Connector.Client;
using NodeAec.Connector.Commands;
using NodeAec.Connector.Storage;
using NodeAec.Connector.UI;

namespace NodeAec.Connector;

/// <summary>
/// Ponto de entrada do plugin Node.aec Connector para Autodesk Revit.
/// Configura a aba canônica 'Node.aec', painel 'Conector' (botão grande "Minha Conta"
/// + botões pequenos empilhados "Meus Plugins" e "Explorar Catálogo"), deduplicação
/// de abas via AdWindows e heartbeat em segundo plano.
/// </summary>
public class App : IExternalApplication
{
    public const string TabName = "Node.aec";
    public const string PanelName = "Conector";

    static App()
    {
#if NET8_0_OR_GREATER
        AssemblyLoadContext.Default.Resolving += (context, name) =>
            LoadFromAddInFolder(name);
#endif
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
        // M3: guarda de topo do add-in. Uma exceção escapando do OnStartup faz o Revit
        // reportar "falha ao carregar o add-in" com uma ribbon meio montada e sem causa
        // registrada; aqui a falha vira Result.Failed limpo, com o tipo da exceção (nunca
        // o conteúdo da mensagem — pode conter caminhos/PII) no log local.
        try
        {
            return Initialize(application);
        }
        catch (Exception ex)
        {
            Diagnostics.ConnectorLog.Write("ERROR", $"Falha ao montar a ribbon Node.aec: {ex.GetType().Name}.");
            return Result.Failed;
        }
    }

    /// <summary>
    /// Corpo do <see cref="OnStartup"/>: aba canônica, deduplicação, painel "Conector",
    /// botões, hooks AdWindows e heartbeat. Qualquer exceção não tratada propaga para o
    /// guarda de topo de <c>OnStartup</c>, que a converte em <c>Result.Failed</c>.
    /// </summary>
    private static Result Initialize(UIControlledApplication application)
    {
        // 1. Cria a aba canônica "Node.aec" caso não exista. Só a exceção de "nome já em
        //    uso"/nome inválido é tratada como estado esperado (reload do add-in); qualquer
        //    outra falha propaga para o guarda de topo. Um catch sem filtro — como antes —
        //    mascarava qualquer erro real e a causa reaparecia depois, sem rastro, como
        //    ArgumentException do CreateRibbonPanel.
        try
        {
            application.CreateRibbonTab(TabName);
        }
        catch (Exception ex) when (
            ex is Autodesk.Revit.Exceptions.ArgumentException || ex is ArgumentException)
        {
            Diagnostics.ConnectorLog.Write("INFO", $"Aba '{TabName}' já existia ou nome rejeitado: {ex.GetType().Name}.");
        }

        // 2. Limpa elementos estranhos e remove abas duplicadas
        CleanRogueRibbonElements();
        DeduplicateRibbonTabs(TabName);

        string assemblyPath = typeof(App).Assembly.Location;
        string addInDir = Path.GetDirectoryName(assemblyPath) ?? AppDomain.CurrentDomain.BaseDirectory;

        // 3. Obtém ou cria o painel de governança "Conector"
        Autodesk.Revit.UI.RibbonPanel connectorPanel = GetOrCreatePanel(application, TabName, PanelName);

        // 4. Botão principal (grande): "Minha Conta"
        var btnManageData = new PushButtonData(
            "NodeAec_ManageConnector",
            "Minha\nConta",
            assemblyPath,
            typeof(ManageConnectorCommand).FullName ?? string.Empty)
        {
            ToolTip = "Gerencie sua conta Node.aec: entrar, sair e atualizar suas licenças."
        };
        LoadButtonIcons(btnManageData, addInDir);
        AddButtonIfMissing(connectorPanel, btnManageData);

        // 5-6. Botões secundários (pequenos, empilhados): "Meus Plugins" + "Explorar Catálogo".
        // "Meus Plugins" fica desabilitado até o login (RequiresLoginAvailability).
        var btnPluginsData = new PushButtonData(
            "NodeAec_ManagePlugins",
            "Meus\nPlugins",
            assemblyPath,
            typeof(ManagePluginsCommand).FullName ?? string.Empty)
        {
            ToolTip = "Veja os plugins vinculados à sua conta, com link para cada produto.",
            AvailabilityClassName = typeof(RequiresLoginAvailability).FullName ?? string.Empty
        };
        btnPluginsData.Image = UiTheme.PluginsIcon(large: false);
        btnPluginsData.LargeImage = UiTheme.PluginsIcon(large: true);

        var btnCatalogData = new PushButtonData(
            "NodeAec_ExploreCatalog",
            "Explorar\nCatálogo",
            assemblyPath,
            typeof(ExploreCatalogCommand).FullName ?? string.Empty)
        {
            ToolTip = "Explorar plugins, famílias e templates no marketplace Node.aec."
        };
        btnCatalogData.Image = UiTheme.CatalogIcon(large: false);
        btnCatalogData.LargeImage = UiTheme.CatalogIcon(large: true);
        AddStackedButtonsIfMissing(connectorPanel, btnPluginsData, btnCatalogData);

        // 7. Hooks defensivos de ciclo de vida do Revit Ribbon. Handlers nomeados e
        // estáticos com "-=" antes do "+=": se o add-in recarregar no mesmo processo
        // (Add-In Manager), os delegates não se acumulam (L10).
        try
        {
            application.ControlledApplication.ApplicationInitialized -= OnApplicationInitialized;
            application.ControlledApplication.ApplicationInitialized += OnApplicationInitialized;

            ComponentManager.UIElementActivated -= OnUiElementActivated;
            ComponentManager.UIElementActivated += OnUiElementActivated;
        }
        catch
        {
        }

        // 8. Heartbeat periódico em segundo plano (não bloqueante): dispara já na subida e
        // renova o lease a cada 6 h — era one-shot, então uma máquina que iniciava offline
        // nunca renovava na sessão inteira. Falha/offline espera simplesmente o próximo
        // disparo. A troca atômica evita timers empilhados se o add-in recarregar no
        // mesmo processo (o timer anterior é descartado).
        var heartbeatTimer = new Timer(_ => RunHeartbeat(), null, TimeSpan.Zero, HeartbeatPeriod);
        Interlocked.Exchange(ref _heartbeatTimer, heartbeatTimer)?.Dispose();

        return Result.Succeeded;
    }

    /// <summary>Cadência do heartbeat de lease (4 disparos/dia — tráfego irrisório).</summary>
    private static readonly TimeSpan HeartbeatPeriod = TimeSpan.FromHours(6);

    private static Timer? _heartbeatTimer;

    /// <summary>
    /// Dispara uma rodada de heartbeat em background (fire-and-forget). Só lê armazenamento
    /// local e fala com a API — nenhuma API do Revit na threadpool. Qualquer exceção vira
    /// WARN com o tipo do erro (nunca mensagem crua).
    /// </summary>
    private static void RunHeartbeat()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                string? token = LeaseStorage.LoadMasterLease();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    var client = new ConnectorApiClient();
                    var heartbeat = await client.ValidateHeartbeatAsync(token).ConfigureAwait(false);

                    // M7: a decisão de log (texto fixo nos ramos de chave/cache — nunca
                    // mensagem crua do servidor) vive em HeartbeatLog, coberta por teste.
                    string? warning = Diagnostics.HeartbeatLog.WarningMessage(heartbeat);
                    if (warning != null)
                    {
                        Diagnostics.ConnectorLog.Write("WARN", warning);
                    }
                }
            }
            catch (Exception ex)
            {
                // Silencioso se offline — mas deixa a causa rastreável no log local.
                Diagnostics.ConnectorLog.Write("WARN", $"Heartbeat de lease interrompido: {ex.GetType().Name}.");
            }
        });
    }

    /// <summary>Reação ao evento <c>ApplicationInitialized</c>: deduplica a aba e remove painéis fantasmas.</summary>
    private static void OnApplicationInitialized(object? sender, EventArgs e)
    {
        DeduplicateRibbonTabs(TabName);
        CleanRogueRibbonElements();
    }

    /// <summary>Reação à ativação de qualquer elemento da Ribbon: mantém a aba única.</summary>
    private static void OnUiElementActivated(object? sender, EventArgs e)
    {
        DeduplicateRibbonTabs(TabName);
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        return Result.Succeeded;
    }

    private static void LoadButtonIcons(PushButtonData button, string addInDir)
    {
        try
        {
            string icon32Path = Path.Combine(addInDir, "Resources", "nodeaec-32.png");
            if (!File.Exists(icon32Path)) icon32Path = Path.Combine(addInDir, "nodeaec-32.png");

            if (File.Exists(icon32Path))
            {
                var bmp32 = new BitmapImage();
                bmp32.BeginInit();
                bmp32.UriSource = new Uri(icon32Path, UriKind.Absolute);
                bmp32.CacheOption = BitmapCacheOption.OnLoad;
                bmp32.EndInit();
                bmp32.Freeze();
                button.LargeImage = bmp32;
            }

            string icon16Path = Path.Combine(addInDir, "Resources", "nodeaec-16.png");
            if (!File.Exists(icon16Path)) icon16Path = Path.Combine(addInDir, "nodeaec-16.png");

            if (File.Exists(icon16Path))
            {
                var bmp16 = new BitmapImage();
                bmp16.BeginInit();
                bmp16.UriSource = new Uri(icon16Path, UriKind.Absolute);
                bmp16.CacheOption = BitmapCacheOption.OnLoad;
                bmp16.EndInit();
                bmp16.Freeze();
                button.Image = bmp16;
            }
        }
        catch
        {
        }
    }

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
        }

        return app.CreateRibbonPanel(tab, panelName);
    }

    private static PushButton? AddButtonIfMissing(Autodesk.Revit.UI.RibbonPanel panel, PushButtonData buttonData)
    {
        try
        {
            var items = panel.GetItems().ToList();
            if (RibbonDecisions.ContainsName(items.Select(i => i.Name), buttonData.Name))
            {
                return items.FirstOrDefault(i => string.Equals(i.Name, buttonData.Name, StringComparison.OrdinalIgnoreCase)) as PushButton;
            }
            return panel.AddItem(buttonData) as PushButton;
        }
        catch
        {
            return null;
        }
    }

    private static void AddStackedButtonsIfMissing(Autodesk.Revit.UI.RibbonPanel panel, PushButtonData first, PushButtonData second)
    {
        try
        {
            var existing = panel.GetItems().Select(i => i.Name).ToList();

            // M7: a decisão (empilhar / avulso / nada) é pura e testada em RibbonDecisions;
            // aqui ficam somente os comandos de UI.
            switch (RibbonDecisions.PlanStackedInsertion(existing, first.Name, second.Name))
            {
                case StackedInsertion.None:
                    return;
                case StackedInsertion.StackBoth:
                    panel.AddStackedItems(first, second);
                    return;
                case StackedInsertion.AddFirstOnly:
                    AddButtonIfMissing(panel, first);
                    return;
                case StackedInsertion.AddSecondOnly:
                    AddButtonIfMissing(panel, second);
                    return;
            }
        }
        catch
        {
        }
    }

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
        }
    }

    /// <summary>
    /// Remove elementos legados da Ribbon: abas "License"/"Licensing" e o botão
    /// "Conectar Conta" (aposentado em favor de "Minha Conta" + "Meus Plugins").
    /// </summary>
    public static void CleanRogueRibbonElements()
    {
        try
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

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

            // Remove o botão legado "Conectar Conta" de qualquer painel onde persista.
            foreach (var tab in ribbon.Tabs)
            {
                foreach (var panel in tab.Panels)
                {
                    try
                    {
                        // Captura a fonte uma única vez: `panel.Source` é anulável e dereferenciá-lo
                        // dentro do laço repetia a checagem (CS8602) e abria espaço para uma corrida
                        // caso a fonte fosse trocada entre as remoções.
                        var items = panel.Source?.Items;
                        if (items == null) continue;

                        var legacyItems = items
                            .Where(item => string.Equals(item.Id, "NodeAec_LoginConnector", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        foreach (var legacy in legacyItems)
                        {
                            items.Remove(legacy);
                        }
                    }
                    catch { }
                }
            }
        }
        catch
        {
        }
    }
}
