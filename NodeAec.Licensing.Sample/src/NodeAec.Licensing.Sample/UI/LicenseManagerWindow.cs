using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Windows;
using NodeAec.Licensing.Client;
using NodeAec.Licensing.Sample.Config;

namespace NodeAec.Licensing.Sample.UI;

/// <summary>
/// Janela interativa WPF para gerenciamento de licenças Node.aec.
/// Suporta qualquer produto da plataforma (universal) e direciona para o repositório github.com/nodeaec/revit-plugins.
/// </summary>
public class LicenseManagerWindow : Window
{
    private readonly TextBlock _txtStatusBadge;
    private readonly TextBlock _txtLicenseKey;
    private readonly TextBlock _txtProductName;
    private readonly Button _btnProductLink;
    private string? _currentProductUrl;
    private readonly TextBlock _txtLicenseType;
    private readonly TextBlock _txtExpires;
    private readonly TextBlock _txtMachineId;
    private readonly TextBox _txtInputKey;
    private readonly TextBlock _txtFeedback;
    private readonly Button _btnActivate;
    private readonly Button _btnValidate;
    private readonly Button _btnDeactivate;

    public LicenseManagerWindow()
    {
        Title = "Node.aec - Gerenciador de Licença";
        Width = 560;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(248, 249, 250));
        FontFamily = new FontFamily("Segoe UI");

        // Aplica o ícone da Node.aec na janela
        try
        {
            var icon = LoadAppIcon();
            if (icon != null)
            {
                Icon = icon;
            }

            if (ComponentManager.ApplicationWindow != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = ComponentManager.ApplicationWindow;
            }
        }
        catch
        {
            // Fallback se o identificador do Revit não estiver disponível
        }

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status card
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Input card
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Feedback
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Repo & Docs banner
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Actions

        // 1. Header
        var headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = "Node.aec // Licenciamento",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(25, 118, 210))
        };
        Grid.SetColumn(titleText, 0);
        titleRow.Children.Add(titleText);

        var btnGitHubHeader = new Button
        {
            Content = "github.com/nodeaec/revit-plugins",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(25, 118, 210)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        btnGitHubHeader.Click += (s, e) => OpenIntegrationRepo();
        Grid.SetColumn(btnGitHubHeader, 1);
        titleRow.Children.Add(btnGitHubHeader);

        var subtitleText = new TextBlock
        {
            Text = $"Servidor: {LicenseConfig.ApiUrl}",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(117, 117, 117)),
            Margin = new Thickness(0, 4, 0, 0)
        };

        headerPanel.Children.Add(titleRow);
        headerPanel.Children.Add(subtitleText);
        Grid.SetRow(headerPanel, 0);
        root.Children.Add(headerPanel);

        // 2. Status Card
        var statusBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 16)
        };
        var statusGrid = new Grid();
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        statusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        statusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        statusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        statusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        statusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(statusGrid, 0, "Status:", _txtStatusBadge = CreateBadge());
        AddRow(statusGrid, 1, "Chave Ativa:", _txtLicenseKey = new TextBlock { FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Consolas") });

        var prodPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _txtProductName = new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _btnProductLink = new Button
        {
            Content = "↗ Ver no site",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 10,
            Cursor = System.Windows.Input.Cursors.Hand,
            Visibility = Visibility.Collapsed
        };
        _btnProductLink.Click += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(_currentProductUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(_currentProductUrl) { UseShellExecute = true });
                }
                catch { }
            }
        };
        prodPanel.Children.Add(_txtProductName);
        prodPanel.Children.Add(_btnProductLink);
        AddRow(statusGrid, 2, "Produto:", prodPanel);

        AddRow(statusGrid, 3, "Tipo:", _txtLicenseType = new TextBlock());
        AddRow(statusGrid, 4, "Validade:", _txtExpires = new TextBlock());

        var midPanel = new StackPanel { Orientation = Orientation.Horizontal };
        _txtMachineId = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        var btnCopyMid = new Button
        {
            Content = "Copiar",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 10,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnCopyMid.Click += (s, e) =>
        {
            try
            {
                using var c = LicenseConfig.CreateClient();
                Clipboard.SetText(c.GetMachineId());
                SetFeedback("Machine ID copiado para a área de transferência.", isError: false);
            }
            catch { }
        };
        midPanel.Children.Add(_txtMachineId);
        midPanel.Children.Add(btnCopyMid);
        AddRow(statusGrid, 5, "Machine ID:", midPanel);

        statusBorder.Child = statusGrid;
        Grid.SetRow(statusBorder, 1);
        root.Children.Add(statusBorder);

        // 3. Input Card
        var inputBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var inputStack = new StackPanel();
        var lblInput = new TextBlock
        {
            Text = "Ativar Chave de Licença (qualquer produto Node.aec):",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var inputRow = new Grid();
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        _txtInputKey = new TextBox
        {
            Padding = new Thickness(8, 6, 8, 6),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _txtInputKey.TextChanged += (s, e) =>
        {
            int caret = _txtInputKey.CaretIndex;
            string upper = _txtInputKey.Text.ToUpperInvariant();
            if (_txtInputKey.Text != upper)
            {
                _txtInputKey.Text = upper;
                _txtInputKey.CaretIndex = caret;
            }
        };

        _btnActivate = new Button
        {
            Content = "Ativar Chave",
            Background = new SolidColorBrush(Color.FromRgb(25, 118, 210)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            Padding = new Thickness(12, 6, 12, 6)
        };
        _btnActivate.Click += async (s, e) => await HandleActivateAsync();

        inputRow.Children.Add(_txtInputKey);
        Grid.SetColumn(_txtInputKey, 0);
        inputRow.Children.Add(_btnActivate);
        Grid.SetColumn(_btnActivate, 1);

        inputStack.Children.Add(lblInput);
        inputStack.Children.Add(inputRow);
        inputBorder.Child = inputStack;
        Grid.SetRow(inputBorder, 2);
        root.Children.Add(inputBorder);

        // 4. Feedback text
        _txtFeedback = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 12),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(_txtFeedback, 3);
        root.Children.Add(_txtFeedback);

        // 5. GitHub Repo & Integration Card
        var repoBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 16)
        };
        var repoGrid = new Grid();
        repoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        repoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var repoInfoStack = new StackPanel();
        var repoTitle = new TextBlock
        {
            Text = "Documentação & SDK para Desenvolvedores",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
        };
        var repoDesc = new TextBlock
        {
            Text = "Acesse o código-fonte de referência e guias de integração em github.com/nodeaec/revit-plugins",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            Margin = new Thickness(0, 2, 0, 0)
        };
        repoInfoStack.Children.Add(repoTitle);
        repoInfoStack.Children.Add(repoDesc);
        Grid.SetColumn(repoInfoStack, 0);
        repoGrid.Children.Add(repoInfoStack);

        var btnOpenRepo = new Button
        {
            Content = "Abrir no GitHub",
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 11,
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        btnOpenRepo.Click += (s, e) => OpenIntegrationRepo();
        Grid.SetColumn(btnOpenRepo, 1);
        repoGrid.Children.Add(btnOpenRepo);

        repoBorder.Child = repoGrid;
        Grid.SetRow(repoBorder, 4);
        root.Children.Add(repoBorder);

        // 6. Action Bar
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        _btnValidate = new Button
        {
            Content = "Validar (Heartbeat)",
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        _btnValidate.Click += async (s, e) => await HandleValidateAsync();

        _btnDeactivate = new Button
        {
            Content = "Desativar Posto",
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        _btnDeactivate.Click += async (s, e) => await HandleDeactivateAsync();

        var btnClose = new Button
        {
            Content = "Fechar",
            Padding = new Thickness(16, 8, 16, 8),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnClose.Click += (s, e) => Close();

        actionsPanel.Children.Add(_btnValidate);
        actionsPanel.Children.Add(_btnDeactivate);
        actionsPanel.Children.Add(btnClose);
        Grid.SetRow(actionsPanel, 6);
        root.Children.Add(actionsPanel);

        Content = root;
        Loaded += async (s, e) => await RefreshStateAsync();
    }

    /// <summary>
    /// Abre a URL do repositório oficial de integração e documentação no navegador padrão.
    /// </summary>
    private static void OpenIntegrationRepo()
    {
        try
        {
            Process.Start(new ProcessStartInfo(LicenseConfig.IntegrationRepoUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível abrir o link:\n{ex.Message}", "Node.aec", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>
    /// Adiciona uma linha de chave/valor rotulada à grade de status.
    /// </summary>
    private static void AddRow(Grid grid, int row, string label, UIElement element)
    {
        var lbl = new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(74, 85, 104)),
            Margin = new Thickness(0, 3, 8, 3)
        };
        Grid.SetRow(lbl, row);
        Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        Grid.SetRow(element, row);
        Grid.SetColumn(element, 1);
        grid.Children.Add(element);
    }

    /// <summary>
    /// Cria o componente visual de badge para exibição do status da licença.
    /// </summary>
    private static TextBlock CreateBadge()
    {
        return new TextBlock
        {
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    /// <summary>
    /// Recarrega o estado atual da licença local ou online e atualiza a interface.
    /// </summary>
    private async Task RefreshStateAsync()
    {
        SetBusy(true);
        try
        {
            using var client = LicenseConfig.CreateClient();
            string mid = client.GetMachineId();
            _txtMachineId.Text = mid.Length > 24 ? $"{mid.Substring(0, 16)}...{mid.Substring(mid.Length - 8)}" : mid;

            string? stored = LicenseConfig.LoadStoredKey();
            if (!string.IsNullOrWhiteSpace(stored))
            {
                _txtInputKey.Text = stored;
            }

            var check = await client.ValidateLicenseAsync(allowOffline: true);
            UpdateDisplay(check);
        }
        catch (Exception ex)
        {
            SetFeedback($"Erro ao verificar estado: {ex.Message}", isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Atualiza todos os campos de exibição da janela com base no resultado da validação.
    /// </summary>
    private void UpdateDisplay(LicenseValidationResult check)
    {
        if (check.IsValid)
        {
            string mode = check.IsOffline ? " (Offline Ed25519)" : " (Online)";
            string type = (check.LicenseType ?? "active").ToLowerInvariant();
            if (type == "trial")
            {
                SetBadge(_txtStatusBadge, $"Avaliação / Trial{mode}", Color.FromRgb(255, 248, 225), Color.FromRgb(245, 127, 23));
            }
            else
            {
                SetBadge(_txtStatusBadge, $"Ativa{mode}", Color.FromRgb(232, 245, 233), Color.FromRgb(46, 125, 50));
            }

            _txtLicenseKey.Text = check.LicenseKey ?? "(Chave em cache)";
            _txtProductName.Text = !string.IsNullOrWhiteSpace(check.ProductName)
                ? check.ProductName
                : "Produto Node.aec";
            _currentProductUrl = !string.IsNullOrWhiteSpace(check.ProductUrl)
                ? check.ProductUrl
                : "https://nodeaec.com.br/products";
            _btnProductLink.Visibility = Visibility.Visible;
            _txtLicenseType.Text = check.LicenseType?.ToUpperInvariant() ?? "PADRÃO";
            _txtExpires.Text = check.ExpiresAt.HasValue
                ? $"{check.ExpiresAt.Value:yyyy-MM-dd HH:mm} UTC"
                : "Sem expiração definida";
        }
        else
        {
            if (check.Status == LicenseStatus.Expired)
            {
                SetBadge(_txtStatusBadge, "Expirada", Color.FromRgb(238, 238, 238), Color.FromRgb(97, 97, 97));
            }
            else
            {
                SetBadge(_txtStatusBadge, "Não Licenciado", Color.FromRgb(255, 235, 238), Color.FromRgb(198, 40, 40));
            }

            _txtLicenseKey.Text = "(Nenhuma licença ativa)";
            _txtProductName.Text = "-";
            _currentProductUrl = null;
            _btnProductLink.Visibility = Visibility.Collapsed;
            _txtLicenseType.Text = "-";
            _txtExpires.Text = "-";
        }
    }

    private static void SetBadge(TextBlock badge, string text, Color bg, Color fg)
    {
        badge.Text = text;
        badge.Background = new SolidColorBrush(bg);
        badge.Foreground = new SolidColorBrush(fg);
    }

    private async Task HandleActivateAsync()
    {
        string key = _txtInputKey.Text.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            SetFeedback("Por favor, digite ou cole uma chave de licença válida.", isError: true);
            return;
        }

        SetBusy(true);
        SetFeedback("Contatando servidor Node.aec...", isError: false);
        try
        {
            using var client = LicenseConfig.CreateClient();
            var result = await client.ActivateAsync(key);

            if (result.IsValid)
            {
                LicenseConfig.SaveStoredKey(key);
                UpdateDisplay(result);
                SetFeedback($"✓ Licença ativada com sucesso! Válida até {result.ExpiresAt:yyyy-MM-dd}.", isError: false);
            }
            else
            {
                UpdateDisplay(result);
                SetFeedback($"Falha na ativação [{result.Status}]: {result.ErrorMessage}", isError: true);
            }
        }
        catch (Exception ex)
        {
            SetFeedback($"Erro ao ativar: {ex.Message}", isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task HandleValidateAsync()
    {
        SetBusy(true);
        SetFeedback("Renovando lease token com o servidor...", isError: false);
        try
        {
            using var client = LicenseConfig.CreateClient();
            var result = await client.ValidateLicenseAsync(allowOffline: true);
            UpdateDisplay(result);

            if (result.IsValid)
            {
                string mode = result.IsOffline ? "off-line (Ed25519 validado)" : "online (heartbeat renovado)";
                SetFeedback($"✓ Licença válida em modo {mode}! Vencimento: {result.ExpiresAt:yyyy-MM-dd HH:mm} UTC.", isError: false);
            }
            else
            {
                SetFeedback($"Acesso bloqueado [{result.Status}]: {result.ErrorMessage}", isError: true);
            }
        }
        catch (Exception ex)
        {
            SetFeedback($"Erro ao validar: {ex.Message}", isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task HandleDeactivateAsync()
    {
        var confirm = MessageBox.Show(
            "Deseja realmente desativar esta máquina?\n\nA vaga será liberada imediatamente para que outra estação possa utilizar a licença.",
            "Confirmar Desativação",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        if (confirm != MessageBoxResult.Yes) return;

        SetBusy(true);
        SetFeedback("Liberando assento no servidor...", isError: false);
        try
        {
            using var client = LicenseConfig.CreateClient();
            bool ok = await client.DeactivateAsync();
            await RefreshStateAsync();

            if (ok)
            {
                SetFeedback("✓ Máquina desativada com sucesso. Assento liberado no Node.aec.", isError: false);
            }
            else
            {
                SetFeedback("Aviso: Lease local removido, mas o servidor não confirmou a liberação online.", isError: true);
            }
        }
        catch (Exception ex)
        {
            SetFeedback($"Erro ao desativar: {ex.Message}", isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy)
    {
        _btnActivate.IsEnabled = !isBusy;
        _btnValidate.IsEnabled = !isBusy;
        _btnDeactivate.IsEnabled = !isBusy;
        _txtInputKey.IsEnabled = !isBusy;
    }

    private void SetFeedback(string message, bool isError)
    {
        _txtFeedback.Text = message;
        _txtFeedback.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(198, 40, 40))
            : new SolidColorBrush(Color.FromRgb(46, 125, 50));
    }

    private static ImageSource? LoadAppIcon()
    {
        try
        {
            string dir = Path.GetDirectoryName(typeof(LicenseManagerWindow).Assembly.Location)
                ?? AppDomain.CurrentDomain.BaseDirectory;
            string iconPath = Path.Combine(dir, "Resources", "nodeaec-32.png");
            string? targetPath = File.Exists(iconPath) ? iconPath : Path.Combine(dir, "nodeaec-32.png");

            if (File.Exists(targetPath))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(targetPath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
        }
        catch { }
        return null;
    }

    public static void Open(Autodesk.Revit.UI.UIApplication? app = null)
    {
        var window = new LicenseManagerWindow();
        ((Window)window).ShowDialog();
    }
}
