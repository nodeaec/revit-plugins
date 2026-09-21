using System;
using System.Diagnostics;
using System.IO;
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
/// Janela "Minha Conta" do Node.aec Connector para Autodesk Revit.
/// Linguagem pensada para arquitetos (sem jargão técnico): entrar, sair e atualizar
/// licenças. A ativação manual de chaves fica recolhida em um expansor fechado.
/// Identidade visual segue o light mode da web Node.aec.
/// </summary>
public class ConnectorWindow : Window
{
    private readonly TextBlock _txtAccountTitle;
    private readonly TextBlock _txtAccountHint;
    private readonly Button _btnLogin;
    private readonly Button _btnLogout;
    private readonly TextBlock _txtLicenseStatus;
    private readonly Button _btnSync;
    private readonly TextBox _txtManualKey;
    private readonly Button _btnActivateKey;
    private readonly TextBlock _txtMachineId;
    private readonly TextBlock _txtFeedback;

    public ConnectorWindow()
    {
        Title = "Minha Conta — Node.aec";
        Width = 560;
        Height = 700;
        MinWidth = 520;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = UiTheme.Brush(UiTheme.Background);
        Foreground = UiTheme.Brush(UiTheme.Text);
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

        // 1. Cabeçalho
        root.Children.Add(BuildHeader());

        // 2. Cartão da conta (entrar / sair)
        var accountCard = BuildCard("Sua conta", out var accountContent);
        _txtAccountTitle = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.Brush(UiTheme.Text),
            TextWrapping = TextWrapping.Wrap
        };
        accountContent.Children.Add(_txtAccountTitle);

        _txtAccountHint = new TextBlock
        {
            FontSize = 12,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            Margin = new Thickness(0, 4, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        accountContent.Children.Add(_txtAccountHint);

        var accountButtons = new StackPanel { Orientation = Orientation.Horizontal };
        _btnLogin = CreatePrimaryButton("Entrar com minha conta");
        _btnLogin.Click += async (s, e) => await HandleBrowserLoginAsync();
        accountButtons.Children.Add(_btnLogin);

        _btnLogout = CreateQuietButton("Sair da conta");
        _btnLogout.Margin = new Thickness(8, 0, 0, 0);
        _btnLogout.Click += (s, e) => HandleLogout();
        accountButtons.Children.Add(_btnLogout);
        accountContent.Children.Add(accountButtons);
        root.Children.Add(accountCard);

        // 3. Cartão das licenças neste computador
        var licenseCard = BuildCard("Neste computador", out var licenseContent);
        _txtLicenseStatus = new TextBlock
        {
            FontSize = 13,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        licenseContent.Children.Add(_txtLicenseStatus);

        _btnSync = CreatePrimaryButton("Atualizar minhas licenças");
        _btnSync.Click += async (s, e) => await HandleSyncAsync();
        licenseContent.Children.Add(_btnSync);
        root.Children.Add(licenseCard);

        // 4. Ativação manual recolhida (não polui a interface principal)
        var manualExpander = new Expander
        {
            Header = "Tenho uma chave de ativação",
            IsExpanded = false,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiTheme.Brush(UiTheme.Primary),
            Margin = new Thickness(0, 0, 0, 16)
        };
        var manualContent = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        manualContent.Children.Add(new TextBlock
        {
            Text = "Se a sua empresa enviou uma chave (começa com NAEC-...), digite abaixo para liberar.",
            FontSize = 12,
            FontWeight = FontWeights.Normal,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        var keyRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _txtManualKey = new TextBox
        {
            FontSize = 13,
            Padding = new Thickness(10, 8, 10, 8),
            Background = UiTheme.Brush(UiTheme.Card),
            Foreground = UiTheme.Brush(UiTheme.Text),
            BorderBrush = UiTheme.Brush(UiTheme.Border),
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Consolas, Courier New")
        };
        Grid.SetColumn(_txtManualKey, 0);
        keyRow.Children.Add(_txtManualKey);

        _btnActivateKey = CreatePrimaryButton("Ativar");
        _btnActivateKey.Margin = new Thickness(8, 0, 0, 0);
        _btnActivateKey.Click += async (s, e) => await HandleActivateKeyAsync();
        Grid.SetColumn(_btnActivateKey, 1);
        keyRow.Children.Add(_btnActivateKey);
        manualContent.Children.Add(keyRow);

        var btnImportLease = CreateLinkButton("ou importar um arquivo de licença (.lease)");
        btnImportLease.Click += (s, e) => HandleImportLeaseFile();
        manualContent.Children.Add(btnImportLease);

        _txtMachineId = new TextBlock
        {
            Text = $"Identificação desta máquina (para o suporte): {HardwareId.GetMachineId()}",
            FontSize = 11,
            FontFamily = new FontFamily("Consolas, Courier New"),
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        manualContent.Children.Add(_txtMachineId);
        manualExpander.Content = manualContent;
        root.Children.Add(manualExpander);

        // 5. Mensagem de retorno
        _txtFeedback = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(_txtFeedback);

        // 6. Rodapé
        root.Children.Add(BuildFooter());

        RefreshUiFromStorage();
    }

    private FrameworkElement BuildHeader()
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel();
        titlePanel.Children.Add(new TextBlock
        {
            Text = "Minha Conta",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = UiTheme.Brush(UiTheme.Primary)
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = "Suas licenças da Node.aec em um só lugar.",
            FontSize = 12,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            Margin = new Thickness(0, 4, 0, 8)
        });
        titlePanel.Children.Add(new Border
        {
            Background = UiTheme.Brush(UiTheme.Accent),
            Height = 3,
            Width = 48,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(2)
        });
        Grid.SetColumn(titlePanel, 0);
        header.Children.Add(titlePanel);

        var btnCatalog = CreateLinkButton("Ver catálogo ↗");
        btnCatalog.VerticalAlignment = VerticalAlignment.Center;
        btnCatalog.Click += (s, e) => OpenCatalog();
        Grid.SetColumn(btnCatalog, 1);
        header.Children.Add(btnCatalog);

        return header;
    }

    private FrameworkElement BuildCard(string title, out StackPanel contentPanel)
    {
        var border = new Border
        {
            Background = UiTheme.Brush(UiTheme.Card),
            BorderBrush = UiTheme.Brush(UiTheme.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 16)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = UiTheme.Brush(UiTheme.Primary),
            Margin = new Thickness(0, 0, 0, 6)
        });

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
            Text = $"Node.aec Connector {ConnectorConfig.Version}",
            FontSize = 11,
            Foreground = UiTheme.Brush(UiTheme.TextSecondary),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(docInfo, 0);
        footer.Children.Add(docInfo);

        var btnClose = CreateQuietButton("Fechar");
        btnClose.Click += (s, e) => Close();
        Grid.SetColumn(btnClose, 1);
        footer.Children.Add(btnClose);

        return footer;
    }

    private static Button CreatePrimaryButton(string content)
    {
        return new Button
        {
            Content = content,
            Background = UiTheme.Brush(UiTheme.Primary),
            Foreground = Brushes.White,
            Padding = new Thickness(16, 9, 16, 9),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            FocusVisualStyle = null
        };
    }

    private static Button CreateQuietButton(string content)
    {
        return new Button
        {
            Content = content,
            Background = UiTheme.Brush(UiTheme.SoftBackground),
            Foreground = UiTheme.Brush(UiTheme.Text),
            Padding = new Thickness(14, 8, 14, 8),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            FocusVisualStyle = null
        };
    }

    private static Button CreateLinkButton(string content)
    {
        return new Button
        {
            Content = content,
            FontSize = 12,
            Foreground = UiTheme.Brush(UiTheme.Primary),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            FocusVisualStyle = null
        };
    }

    public void RefreshUiFromStorage()
    {
        var session = LeaseStorage.LoadSession();
        if (session.HasValue && !string.IsNullOrWhiteSpace(session.Value.Email))
        {
            _txtAccountTitle.Text = "Olá! Você está conectado como:";
            _txtAccountHint.Text = session.Value.Email ?? string.Empty;
            _btnLogin.Visibility = Visibility.Collapsed;
            _btnLogout.Visibility = Visibility.Visible;
        }
        else
        {
            _txtAccountTitle.Text = "Você ainda não entrou.";
            _txtAccountHint.Text = "Entre com sua conta para liberar seus plugins neste computador.";
            _btnLogin.Visibility = Visibility.Visible;
            _btnLogout.Visibility = Visibility.Collapsed;
        }

        string? jwtToken = LeaseStorage.LoadMasterLease();
        if (string.IsNullOrWhiteSpace(jwtToken))
        {
            _txtLicenseStatus.Text = "Nenhuma licença encontrada neste computador ainda.";
            _txtLicenseStatus.Foreground = UiTheme.Brush(UiTheme.TextSecondary);
            return;
        }

        var payload = LeaseStorage.ParseJwtPayload(jwtToken);
        if (payload == null)
        {
            _txtLicenseStatus.Text = "Não conseguimos ler as licenças salvas. Tente atualizar.";
            _txtLicenseStatus.Foreground = UiTheme.Brush(UiTheme.Accent);
            return;
        }

        var exp = payload.ExpiresAt;
        if (payload.IsExpired)
        {
            _txtLicenseStatus.Text = $"Suas licenças estão desatualizadas desde {exp:dd/MM/yyyy}. Conecte-se à internet e clique em atualizar.";
            _txtLicenseStatus.Foreground = UiTheme.Brush(UiTheme.Accent);
        }
        else
        {
            _txtLicenseStatus.Text = $"Tudo certo — suas licenças estão atualizadas até {exp:dd/MM/yyyy}.";
            _txtLicenseStatus.Foreground = UiTheme.Brush(UiTheme.Primary);
        }
    }

    private async System.Threading.Tasks.Task HandleBrowserLoginAsync()
    {
        SetFeedback("Abrindo o navegador para você entrar com segurança...", UiTheme.Primary);
        _btnLogin.IsEnabled = false;

        try
        {
            var authService = new DesktopAuthService();
            string userToken = await authService.LoginViaBrowserAsync().ConfigureAwait(true);

            SetFeedback("Pronto! Buscando suas licenças...", UiTheme.Primary);
            using var client = new ConnectorApiClient();
            var syncResult = await client.SyncMasterEntitlementsAsync(userToken).ConfigureAwait(true);

            if (syncResult.Success)
            {
                var payload = LeaseStorage.ParseJwtPayload(syncResult.LeaseToken ?? string.Empty);
                LeaseStorage.SaveSession(payload?.Sub, userToken);
                SetFeedback($"Tudo pronto! {syncResult.GrantedCount} plugin(s) liberado(s) neste computador.", UiTheme.Primary);
            }
            else
            {
                SetFeedback($"Não foi possível buscar suas licenças: {syncResult.Message}", UiTheme.Accent);
            }
        }
        catch (Exception ex)
        {
            SetFeedback($"Algo não saiu como esperado: {ex.Message}", UiTheme.Accent);
        }
        finally
        {
            _btnLogin.IsEnabled = true;
            RefreshUiFromStorage();
        }
    }

    private async System.Threading.Tasks.Task HandleSyncAsync()
    {
        SetFeedback("Atualizando suas licenças...", UiTheme.Primary);
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
                SetFeedback("Licenças atualizadas com sucesso.", UiTheme.Primary);
            }
            else
            {
                SetFeedback($"Não foi possível atualizar agora: {result.Message}", UiTheme.Accent);
            }
        }
        catch (Exception ex)
        {
            SetFeedback($"Sem conexão no momento: {ex.Message}", UiTheme.Accent);
        }
        finally
        {
            _btnSync.IsEnabled = true;
            RefreshUiFromStorage();
        }
    }

    private async System.Threading.Tasks.Task HandleActivateKeyAsync()
    {
        string key = _txtManualKey.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            SetFeedback("Digite a chave enviada para você (começa com NAEC-...).", UiTheme.Accent);
            return;
        }

        SetFeedback("Ativando sua chave...", UiTheme.Primary);
        _btnActivateKey.IsEnabled = false;

        try
        {
            using var client = new ConnectorApiClient();
            var result = await client.ActivateKeyAsync(key).ConfigureAwait(true);

            if (result.Success)
            {
                _txtManualKey.Clear();
                SetFeedback("Chave ativada! Seus plugins foram liberados.", UiTheme.Primary);
            }
            else
            {
                SetFeedback(result.Message, UiTheme.Accent);
            }
        }
        catch (Exception ex)
        {
            SetFeedback($"Não foi possível ativar agora: {ex.Message}", UiTheme.Accent);
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
            Filter = "Arquivos de licença Node.aec (*.lease;*.jwt)|*.lease;*.jwt|Todos os arquivos (*.*)|*.*",
            Title = "Importar arquivo de licença"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                string content = File.ReadAllText(dlg.FileName).Trim();
                var payload = LeaseStorage.ParseJwtPayload(content);
                if (payload == null)
                {
                    SetFeedback("Este arquivo não parece ser uma licença válida.", UiTheme.Accent);
                    return;
                }

                LeaseStorage.SaveMasterLease(content);
                SetFeedback("Licença importada com sucesso!", UiTheme.Primary);
                RefreshUiFromStorage();
            }
            catch (Exception ex)
            {
                SetFeedback($"Não foi possível importar: {ex.Message}", UiTheme.Accent);
            }
        }
    }

    private void HandleLogout()
    {
        var confirm = MessageBox.Show(
            "Deseja sair da sua conta neste computador?\n\nSeus plugins ficarão bloqueados até o próximo login.",
            "Sair da conta",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            LeaseStorage.ClearMasterLease();
            LeaseStorage.ClearSession();
            SetFeedback("Você saiu da conta.", UiTheme.TextSecondary);
            RefreshUiFromStorage();
        }
    }

    private void SetFeedback(string message, Color color)
    {
        _txtFeedback.Text = message;
        _txtFeedback.Foreground = UiTheme.Brush(color);
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

    private static System.Windows.Media.ImageSource? LoadAppIcon()
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
