using System.Windows.Media;

namespace NodeAec.Connector.UI;

/// <summary>
/// Paleta oficial da Node.aec web (light mode) aplicada às janelas do Connector.
/// Centraliza as cores para manter identidade visual consistente entre Minha Conta e Meus Plugins.
/// </summary>
public static class UiTheme
{
    /// <summary>Fundo das janelas (#FAFAFA).</summary>
    public static readonly Color Background = Color.FromRgb(0xFA, 0xFA, 0xFA);

    /// <summary>Fundo dos cartões (#FFFFFF).</summary>
    public static readonly Color Card = Color.FromRgb(0xFF, 0xFF, 0xFF);

    /// <summary>Bordas e divisórias claras.</summary>
    public static readonly Color Border = Color.FromRgb(0xDD, 0xD9, 0xD9);

    /// <summary>Texto principal (#232323).</summary>
    public static readonly Color Text = Color.FromRgb(0x23, 0x23, 0x23);

    /// <summary>Texto secundário (dicas e descrições).</summary>
    public static readonly Color TextSecondary = Color.FromRgb(0x5F, 0x5F, 0x5F);

    /// <summary>Azul petróleo principal da marca (#1E4E79).</summary>
    public static readonly Color Primary = Color.FromRgb(0x1E, 0x4E, 0x79);

    /// <summary>Verde de sucesso (#005A30).</summary>
    public static readonly Color Success = Color.FromRgb(0x00, 0x5A, 0x30);

    /// <summary>Vermelho de erro (#9E0016).</summary>
    public static readonly Color Error = Color.FromRgb(0x9E, 0x00, 0x16);

    /// <summary>Azul informativo (#005976).</summary>
    public static readonly Color Info = Color.FromRgb(0x00, 0x59, 0x76);

    /// <summary>Fundo suave para linhas de status.</summary>
    public static readonly Color SoftBackground = Color.FromRgb(0xE7, 0xE6, 0xE6);

    /// <summary>
    /// Cria um pincel congelado (thread-safe, sem vazamento de recursos GDI).
    /// </summary>
    public static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
