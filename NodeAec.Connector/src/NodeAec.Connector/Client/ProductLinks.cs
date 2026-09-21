using System;
using NodeAec.Connector.Config;

namespace NodeAec.Connector.Client;

/// <summary>
/// Constrói links públicos de produtos da Node.aec Store a partir do slug da concessão.
/// Lógica headless (sem dependências de WPF ou Revit API) para permitir testes via 'dotnet test'.
/// </summary>
public static class ProductLinks
{
    /// <summary>
    /// Monta a URL da página do produto (ex.: https://nodeaec.com.br/products/meu-plugin).
    /// Slugs vazios retornam a URL do catálogo.
    /// </summary>
    public static string BuildProductUrl(string? slug)
    {
        string baseUrl = (ConnectorConfig.CatalogUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(slug))
        {
            return baseUrl;
        }

        string normalized = slug.Trim();
        return $"{baseUrl}/{Uri.EscapeDataString(normalized)}";
    }
}
