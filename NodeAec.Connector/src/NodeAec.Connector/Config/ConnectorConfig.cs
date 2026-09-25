using System;

namespace NodeAec.Connector.Config;

/// <summary>
/// Configuração global de endpoints, versão e chaves públicas da plataforma Node.aec.
/// </summary>
public static class ConnectorConfig
{
    public const string Version = "0.1.1";
    public const string PlatformDescription = "Windows / Revit 2026";

    /// <summary>
    /// URL base da API REST do Node.aec.
    /// </summary>
    public static string ApiBaseUrl { get; set; } =
        Environment.GetEnvironmentVariable("NODEAEC_API_URL") ?? "https://api.nodeaec.com.br";

    /// <summary>
    /// URL da página de login SSO para desktop (OAuth loopback).
    /// </summary>
    public static string WebAuthUrl { get; set; } =
        Environment.GetEnvironmentVariable("NODEAEC_AUTH_URL") ?? "https://nodeaec.com.br/auth/desktop";

    /// <summary>
    /// URL da página de catálogo de produtos.
    /// </summary>
    public static string CatalogUrl { get; set; } =
        Environment.GetEnvironmentVariable("NODEAEC_CATALOG_URL") ?? "https://nodeaec.com.br/products";

    /// <summary>
    /// Âncora pública SPKI (Ed25519, base64) opcional para verificação offline de leases,
    /// definida pela operação em <c>NODEAEC_LICENSE_PUBLIC_KEY_SPKI</c>.
    /// Quando ausente (padrão), a verificação usa o JWKS em cache local
    /// (<c>license-jwks.json</c>) atualizado automaticamente pelo Connector via
    /// <c>GET /license/jwks</c>. Nunca há chave privada neste repositório.
    /// </summary>
    public static string? LicensePublicKeySpkiBase64 =>
        Environment.GetEnvironmentVariable("NODEAEC_LICENSE_PUBLIC_KEY_SPKI");
}
