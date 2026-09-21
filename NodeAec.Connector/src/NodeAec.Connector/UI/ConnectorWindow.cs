using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Windows;
using Microsoft.Win32;
using NodeAec.Connector.Auth;
using NodeAec.Connector.Client;
using NodeAec.Connector.Config;
using NodeAec.Connector.Hardware;
using NodeAec.Connector.Models;
using NodeAec.Connector.Storage;

namespace NodeAec.Connector.UI;

/// <summary>
/// Janela gráfica WPF unificada do Node.aec Connector para Autodesk Revit.
/// Apresenta o estado da conta, produtos autorizados, sincronização via browser SSO
/// e ativação de chaves avulsas/offline.
/// </summary>
public class ConnectorWindow : Window
{
    private readonly TextBlock _txtAccountStatus;
    private readonly Button _btnLogin;
    private readonly Button _btnLogout;
    private readonly TextBlock _txtLeaseSummary;
    private readonly TextBlock _txtMachineId;
    private readonly StackPanel _entitlementsListPanel;
    private readonly TextBox _txtManualKey;
    private readonly Button _btnActivateKey;
    private readonly Button _btnImportLease;
    private readonly Button _btnSync;
    private readonly TextBlock _txtFeedback;

    public ConnectorWindow()
    {
        Title = "Node.aec Connector — Hub de Licenças & Governança";
        Width = 620;
        Height = 740;
        MinWidth = 580;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)); // Slate 900
        Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252));
        FontFamily = new FontFamily("Segoe UI, -apple-system, sans-serif");

        try
        {
            var icon = LoadAppIcon();
            if (icon != null) Icon = icon;

            if (ComponentManager.ApplicationWindow != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = ComponentManager.ApplicationWindow;
            }
        }
        catch
        {
        }

        var mainScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(24)
        };

        var root = new StackPanel();
        mainScroll.Content = root;
        Content = mainScroll;

        // 1. Header
        root.Children.Add(BuildHeader());

        // 2. Account & SSO Card
        var accountCard = BuildCard("Conta & Autenticação", out var accountContent);
        var accountGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        accountGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        accountGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        accountGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _txtAccountStatus = new TextBlock
        {
            Text = "Verificando sessão...",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_txtAccountStatus, 0);
        accountGrid.Children.Add(_txtAccountStatus);

        _btnLogin = CreateButton("Entrar com Node.aec", Color.FromRgb(14, 165, 233), Brushes.White);
        _btnLogin.Margin = new Thickness(8, 0, 0, 0);
        _btnLogin.Click += async (s, e) => await HandleBrowserLoginAsync();
        Grid.SetColumn(_btnLogin, 1);
        accountGrid.Children.Add(_btnLogin);

        _btnLogout = CreateButton("Sair", Color.FromRgb(51, 65, 85), new SolidColorBrush(Color.FromRgb(203, 213, 225)));
        _btnLogout.Margin = new Thickness(8, 0, 0, 0);
        _btnLogout.Click += (s, e) => HandleLogout();
        Grid.SetColumn(_btnLogout, 2);
        accountGrid.Children.Add(_btnLogout);

        accountContent.Children.Add(accountGrid);
        root.Children.Add(accountCard);

        // 3. Lease & Hardware Status Card
        var leaseCard = BuildCard("Status da Estação & Concessão", out var leaseContent);
        _txtLeaseSummary = new TextBlock
        {
            Text = "Carregando concessão de licenças...",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Margin = new Thickness(0, 4, 0, 4)
        };
        leaseContent.Children.Add(_txtLeaseSummary);

        _txtMachineId = new TextBlock
        {
            Text = $"Machine ID: {HardwareId.GetMachineId()}",
            FontSize = 11,
            FontFamily = new FontFamily("Consolas, Courier New"),
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            Margin = new Thickness(0, 0, 0, 8)
        };
        leaseContent.Children.Add(_txtMachineId);

        var syncRow = new StackPanel { Orientation = Orientation.Horizontal };
        _btnSync = CreateButton("Sincronizar Licenças", Color.FromRgb(16, 185, 129), Brushes.White);
        _btnSync.Click += async (s, e) => await HandleSyncAsync();
        syncRow.Children.Add(_btnSync);
        leaseContent.Children.Add(syncRow);

        root.Children.Add(leaseCard);

        // 4. Entitlements List Card
        var entCard = BuildCard("Plugins & Soluções Concedidas", out var entContent);
        _entitlementsListPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        entContent.Children.Add(_entitlementsListPanel);
        root.Children.Add(entCard);

        // 5. Manual Key / Air-Gapped Card
        var keyCard = BuildCard("Ativação Manual / Modo Offline (Air-Gapped)", out var keyContent);
        var keyRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _txtManualKey = new TextBox
        {
            FontSize = 13,
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        Grid.SetColumn(_txtManualKey, 0);
        keyRow.Children.Add(_txtManualKey);

        _btnActivateKey = CreateButton("Ativar Chave", Color.FromRgb(30, 41, 59), new SolidColorBrush(Color.FromRgb(56, 189, 248)));
        _btnActivateKey.BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));
        _btnActivateKey.BorderThickness = new Thickness(1);
        _btnActivateKey.Margin = new Thickness(8, 0, 0, 0);
        _btnActivateKey.Click += async (s, e) => await HandleActivateKeyAsync();
        Grid.SetColumn(_btnActivateKey, 1);
        keyRow.Children.Add(_btnActivateKey);

        _btnImportLease = CreateButton("Importar .lease", Color.FromRgb(30, 41, 59), new SolidColorBrush(Color.FromRgb(148, 163, 184)));
        _btnImportLease.Margin = new Thickness(8, 0, 0, 0);
        _btnImportLease.Click += (s, e) => HandleImportLeaseFile();
        Grid.SetColumn(_btnImportLease, 2);
        keyRow.Children.Add(_btnImportLease);

        keyContent.Children.Add(keyRow);
        root.Children.Add(keyCard);

        // 6. Feedback message
        _txtFeedback = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 12, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(_txtFeedback);

        // 7. Footer
        root.Children.Add(BuildFooter());

        // Carrega o estado inicial da máquina
        RefreshUiFromStorage();
    }

    private FrameworkElement BuildHeader()
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel();
        var titleText = new TextBlock
        {
            Text = "Node.aec Connector",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)) // Sky 400
        };
        var subtitle = new TextBlock
        {
            Text = "Governança unificada de licenças e ferramentas BIM para Autodesk Revit",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Margin = new Thickness(0, 4, 0, 0)
        };
        titlePanel.Children.Add(titleText);
        titlePanel.Children.Add(subtitle);
        Grid.SetColumn(titlePanel, 0);
        header.Children.Add(titlePanel);

        var btnCatalog = new Button
        {
            Content = "↗ Abrir Catálogo",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        btnCatalog.Click += (s, e) => OpenCatalog();
        Grid.SetColumn(btnCatalog, 1);
        header.Children.Add(btnCatalog);

        return header;
    }

    private FrameworkElement BuildCard(string title, out StackPanel contentPanel)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)), // Slate 800
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)), // Slate 700
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 16)
        };

        var stack = new StackPanel();
        var lblTitle = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            Margin = new Thickness(0, 0, 0, 6)
        };
        stack.Children.Add(lblTitle);

        contentPanel = new StackPanel();
        stack.Children.Add(contentPanel);
        border.Child = stack;
        return border;
    }

    private FrameworkElement BuildFooter()
    {
        var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var docInfo = new TextBlock
        {
            Text = $"Versão {ConnectorConfig.Version} // nodeaec.com.br",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(docInfo, 0);
        footer.Children.Add(docInfo);

        var btnClose = CreateButton("Fechar", Color.FromRgb(51, 65, 85), Brushes.White);
        btnClose.Click += (s, e) => Close();
        Grid.SetColumn(btnClose, 1);
        footer.Children.Add(btnClose);

        return footer;
    }

    private Button CreateButton(string content, Color bgColor, Brush fgColor)
    {
        return new Button
        {
            Content = content,
            Background = new SolidColorBrush(bgColor),
            Foreground = fgColor,
            Padding = new Thickness(14, 8, 14, 8),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            FocusVisualStyle = null
        };
    }

    public void RefreshUiFromStorage()
    {
        var session = LeaseStorage.LoadSession();
        if (session.HasValue && !string.IsNullOrWhiteSpace(session.Value.Email))
        {
            _txtAccountStatus.Text = $"Conectado como {session.Value.Email}";
            _btnLogin.Visibility = Visibility.Collapsed;
            _btnLogout.Visibility = Visibility.Visible;
        }
        else
        {
            _txtAccountStatus.Text = "Não Conectado (Offline / Chaves Avulsas)";
            _btnLogin.Visibility = Visibility.Visible;
            _btnLogout.Visibility = Visibility.Collapsed;
        }

        string? jwtToken = LeaseStorage.LoadMasterLease();
        if (string.IsNullOrWhiteSpace(jwtToken))
        {
            _txtLeaseSummary.Text = "Nenhuma concessão ativa nesta máquina.";
            _entitlementsListPanel.Children.Clear();
            _entitlementsListPanel.Children.Add(new TextBlock
            {
                Text = "Nenhum produto autorizado no momento. Faça login ou insira uma chave para sincronizar.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                Margin = new Thickness(0, 4, 0, 4)
            });
            return;
        }

        var payload = LeaseStorage.ParseJwtPayload(jwtToken);
        if (payload == null)
        {
            _txtLeaseSummary.Text = "Token de concessão corrompido ou formato inválido.";
            return;
        }

        var exp = payload.ExpiresAt;
        var diff = exp - DateTimeOffset.UtcNow;
        if (payload.IsExpired)
        {
            _txtLeaseSummary.Text = $"Concessão offline expirada em {exp:dd/MM/yyyy}. Conecte-se para renovar.";
            _txtLeaseSummary.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)); // Red 400
        }
        else
        {
            _txtLeaseSummary.Text = $"Concessão ativa até {exp:dd/MM/yyyy HH:mm} (restam {(int)diff.TotalDays} dias de tolerância offline).";
            _txtLeaseSummary.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128)); // Green 400
        }

        _entitlementsListPanel.Children.Clear();
        var entitlements = payload.Entitlements ?? new List<EntitlementItem>();

        if (entitlements.Count == 0)
        {
            _entitlementsListPanel.Children.Add(new TextBlock
            {
                Text = "Esta conta não possui plugins licenciados no momento.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                Margin = new Thickness(0, 4, 0, 4)
            });
            return;
        }

        foreach (var ent in entitlements)
        {
            _entitlementsListPanel.Children.Add(BuildEntitlementCard(ent));
        }
    }

    private FrameworkElement BuildEntitlementCard(EntitlementItem item)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var infoPanel = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };

        var nameBlock = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(item.Name) ? item.Slug : item.Name,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249))
        };
        titleRow.Children.Add(nameBlock);

        var typeBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(8, 0, 0, 0)
        };
        typeBadge.Child = new TextBlock
        {
            Text = item.Type.ToUpperInvariant(),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
        };
        titleRow.Children.Add(typeBadge);
        infoPanel.Children.Add(titleRow);

        string expInfo = item.ExpiresAt.HasValue
            ? $"Expira em: {item.ExpiresAt.Value:dd/MM/yyyy}"
            : "Licença Vitalícia";
        string slugInfo = $"slug: {item.Slug}";
        if (!string.IsNullOrEmpty(item.LicenseKey))
        {
            slugInfo += $" // {item.LicenseKey}";
        }

        var detailsBlock = new TextBlock
        {
            Text = $"{slugInfo} • {expInfo}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Margin = new Thickness(0, 4, 0, 0)
        };
        infoPanel.Children.Add(detailsBlock);
        Grid.SetColumn(infoPanel, 0);
        grid.Children.Add(infoPanel);

        // Status Badge
        bool active = item.IsActive();
        var statusBadge = new Border
        {
            Background = active
                ? new SolidColorBrush(Color.FromArgb(40, 74, 222, 128))
                : new SolidColorBrush(Color.FromArgb(40, 248, 113, 113)),
            BorderBrush = active
                ? new SolidColorBrush(Color.FromRgb(74, 222, 128))
                : new SolidColorBrush(Color.FromRgb(248, 113, 113)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        statusBadge.Child = new TextBlock
        {
            Text = active ? "ATIVO" : item.Status.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = active
                ? new SolidColorBrush(Color.FromRgb(74, 222, 128))
                : new SolidColorBrush(Color.FromRgb(248, 113, 113))
        };
        Grid.SetColumn(statusBadge, 1);
        grid.Children.Add(statusBadge);

        card.Child = grid;
        return card;
    }

    private async Task HandleBrowserLoginAsync()
    {
        SetFeedback("Abrindo navegador para login seguro via Node.aec...", Color.FromRgb(56, 189, 248));
        _btnLogin.IsEnabled = false;

        try
        {
            var authService = new DesktopAuthService();
            string userToken = await authService.LoginViaBrowserAsync().ConfigureAwait(true);

            SetFeedback("Autenticado! Sincronizando licenças da sua conta...", Color.FromRgb(56, 189, 248));
            using var client = new ConnectorApiClient();
            var syncResult = await client.SyncMasterEntitlementsAsync(userToken).ConfigureAwait(true);

            if (syncResult.Success)
            {
                var payload = LeaseStorage.ParseJwtPayload(syncResult.LeaseToken ?? string.Empty);
                LeaseStorage.SaveSession(payload?.Sub, userToken);
                SetFeedback($"Sucesso! {syncResult.GrantedCount} produto(s) licenciados nesta estação.", Color.FromRgb(74, 222, 128));
            }
            else
            {
                SetFeedback($"Falha na sincronização: {syncResult.Message}", Color.FromRgb(248, 113, 113));
            }
        }
        catch (Exception ex)
        {
            SetFeedback($"Erro durante o login: {ex.Message}", Color.FromRgb(248, 113, 113));
        }
        finally
        {
            _btnLogin.IsEnabled = true;
            RefreshUiFromStorage();
        }
    }

    private async Task HandleSyncAsync()
    {
        SetFeedback("Sincronizando concessões com a nuvem Node.aec...", Color.FromRgb(56, 189, 248));
        _btnSync.IsEnabled = false;

        try
        {
            var session = LeaseStorage.LoadSession();
            using var client = new ConnectorApiClient();

            SyncResult result;
            if (session.HasValue && !string.IsNullOrWhiteSpace(session.Value.Token))
            {
                result = await client.SyncMasterEntitlementsAsync(session.Value.Token).ConfigureAwait(true);
            }
            else
            {
                result = await client.ValidateHeartbeatAsync().ConfigureAwait(true);
            }

            if (result.Success)
            {
                SetFeedback("Sincronização concluída com sucesso.", Color.FromRgb(74, 222, 128));
            }
            else
            {
                SetFeedback($"Falha ao sincronizar: {result.Message}", Color.FromRgb(248, 113, 113));
            }
        }
        catch (Exception ex)
        {
            SetFeedback($"Erro de rede: {ex.Message}", Color.FromRgb(248, 113, 113));
        }
        finally
        {
            _btnSync.IsEnabled = true;
            RefreshUiFromStorage();
        }
    }

    private async Task HandleActivateKeyAsync()
    {
        string key = _txtManualKey.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            SetFeedback("Informe uma chave válida no formato NAEC-XXXX-XXXX-XXXX-XXXX.", Color.FromRgb(248, 113, 113));
            return;
        }

        SetFeedback("Ativando licença na nuvem...", Color.FromRgb(56, 189, 248));
        _btnActivateKey.IsEnabled = false;

        try
        {
            using var client = new ConnectorApiClient();
            var result = await client.ActivateKeyAsync(key).ConfigureAwait(true);

            if (result.Success)
            {
                _txtManualKey.Clear();
                SetFeedback(result.Message, Color.FromRgb(74, 222, 128));
            }
            else
            {
                SetFeedback(result.Message, Color.FromRgb(248, 113, 113));
            }
        }
        catch (Exception ex)
        {
            SetFeedback($"Erro ao ativar chave: {ex.Message}", Color.FromRgb(248, 113, 113));
        }
        finally
        {
            _btnActivateKey.IsEnabled = true;
            RefreshUiFromStorage();
        }
    }

    private void HandleImportLeaseFile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Arquivos de Lease Node.aec (*.lease;*.jwt)|*.lease;*.jwt|Todos os Arquivos (*.*)|*.*",
            Title = "Importar Concessão de Licença Offline"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                string content = File.ReadAllText(dlg.FileName).Trim();
                var payload = LeaseStorage.ParseJwtPayload(content);
                if (payload == null)
                {
                    SetFeedback("O arquivo selecionado não contém um token JWT válido.", Color.FromRgb(248, 113, 113));
                    return;
                }

                LeaseStorage.SaveMasterLease(content);
                SetFeedback("Lease offline importado com sucesso!", Color.FromRgb(74, 222, 128));
                RefreshUiFromStorage();
            }
            catch (Exception ex)
            {
                SetFeedback($"Falha ao importar arquivo: {ex.Message}", Color.FromRgb(248, 113, 113));
            }
        }
    }

    private void HandleLogout()
    {
        var confirm = MessageBox.Show(
            "Deseja desconectar sua conta Node.aec desta máquina?\n\nAs concessões de licença locais serão removidas até o próximo login.",
            "Node.aec Connector",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            LeaseStorage.ClearMasterLease();
            LeaseStorage.ClearSession();
            SetFeedback("Conta desconectada com sucesso.", Color.FromRgb(148, 163, 184));
            RefreshUiFromStorage();
        }
    }

    private void SetFeedback(string message, Color color)
    {
        _txtFeedback.Text = message;
        _txtFeedback.Foreground = new SolidColorBrush(color);
    }

    private void OpenCatalog()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ConnectorConfig.CatalogUrl,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private static ImageSource? LoadAppIcon()
    {
        try
        {
            string dir = Path.GetDirectoryName(typeof(ConnectorWindow).Assembly.Location)
                         ?? AppDomain.CurrentDomain.BaseDirectory;
            string path = Path.Combine(dir, "Resources", "nodeaec-32.png");
            if (!File.Exists(path)) path = Path.Combine(dir, "nodeaec-32.png");

            if (File.Exists(path))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
        }
        catch { }
        return null;
    }

    public static void Open(Autodesk.Revit.UI.UIApplication? uiApp = null)
    {
        var win = new ConnectorWindow();
        win.Show();
        win.Activate();
    }
}
