using System.Windows;
using System.Windows.Media;

namespace NodeAec.Connector.UI;

/// <summary>
/// Paleta oficial da Node.aec web (tokens light mode) aplicada às janelas e botões do Connector:
/// fundo #E7E6E6, texto #232323, cor principal #1E4E79 e cor de destaque #A79D12.
/// Centraliza cores e ícones para manter identidade visual consistente.
/// </summary>
public static class UiTheme
{
    /// <summary>Fundo das janelas (#E7E6E6, token de fundo claro).</summary>
    public static readonly Color Background = Color.FromRgb(0xE7, 0xE6, 0xE6);

    /// <summary>Fundo dos cartões (branco, para contraste com o fundo).</summary>
    public static readonly Color Card = Color.FromRgb(0xFF, 0xFF, 0xFF);

    /// <summary>Bordas e divisórias (cinza neutro).</summary>
    public static readonly Color Border = Color.FromRgb(0xCF, 0xCF, 0xCF);

    /// <summary>Texto principal (#232323).</summary>
    public static readonly Color Text = Color.FromRgb(0x23, 0x23, 0x23);

    /// <summary>Texto secundário (dicas e descrições, cinza neutro).</summary>
    public static readonly Color TextSecondary = Color.FromRgb(0x5F, 0x5F, 0x5F);

    /// <summary>Cor principal da marca (#1E4E79): títulos, ações primárias e links.</summary>
    public static readonly Color Primary = Color.FromRgb(0x1E, 0x4E, 0x79);

    /// <summary>Cor de destaque da marca (#A79D12): avisos, badges e detalhes.</summary>
    public static readonly Color Accent = Color.FromRgb(0xA7, 0x9D, 0x12);

    /// <summary>Fundo neutro para botões secundários.</summary>
    public static readonly Color SoftBackground = Color.FromRgb(0xDA, 0xD8, 0xD8);

    /// <summary>
    /// Cria um pincel congelado (thread-safe, sem vazamento de recursos).
    /// </summary>
    public static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Ícone do botão "Meus Plugins": grade 2x2 na cor principal.
    /// Vetorial (escala sem perda para 16px e 32px).
    /// </summary>
    public static ImageSource PluginsIcon(bool large)
    {
        var group = new GeometryGroup();
        group.Children.Add(new RectangleGeometry(new Rect(2, 2, 5.5, 5.5)));
        group.Children.Add(new RectangleGeometry(new Rect(8.5, 2, 5.5, 5.5)));
        group.Children.Add(new RectangleGeometry(new Rect(2, 8.5, 5.5, 5.5)));
        group.Children.Add(new RectangleGeometry(new Rect(8.5, 8.5, 5.5, 5.5)));

        double scale = large ? 2.0 : 1.0;
        group.Transform = new ScaleTransform(scale, scale);
        group.Freeze();

        var drawing = new GeometryDrawing(Brush(Primary), null, group);
        drawing.Freeze();

        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    /// <summary>
    /// Ícone do botão "Explorar Catálogo": lupa (anel na cor principal, haste no destaque).
    /// Vetorial (escala sem perda para 16px e 32px).
    /// </summary>
    public static ImageSource CatalogIcon(bool large)
    {
        var lens = new EllipseGeometry(new Point(7, 7), 4.6, 4.6);
        lens.Freeze();

        var handle = new StreamGeometry();
        using (var context = handle.Open())
        {
            context.BeginFigure(new Point(10.4, 10.4), false, false);
            context.LineTo(new Point(14.2, 14.2), true, false);
        }
        handle.Freeze();

        var lensPen = new Pen(Brush(Primary), 2.4);
        lensPen.Freeze();
        var handlePen = new Pen(Brush(Accent), 2.6)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        handlePen.Freeze();

        var drawings = new DrawingGroup();
        drawings.Children.Add(new GeometryDrawing(null, lensPen, lens));
        drawings.Children.Add(new GeometryDrawing(null, handlePen, handle));

        double scale = large ? 2.0 : 1.0;
        drawings.Transform = new ScaleTransform(scale, scale);
        drawings.Freeze();

        var image = new DrawingImage(drawings);
        image.Freeze();
        return image;
    }
}
