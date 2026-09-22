using System;

namespace NodeAec.Connector.Config;

/// <summary>
/// Configuração global de endpoints, versão e chaves públicas da plataforma Node.aec.
/// </summary>
public static class ConnectorConfig
{
    public const string Version = "0.1";
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
    /// Chave pública oficial SPKI (Ed25519) do Node.aec para conferência offline de leases.
    /// </summary>
    public const string DefaultPublicKeySpkiBase64 = "MCowBQYDK2VwAyEAGbX7HwE+YvJkWjQ9zX8bN3fV0c2Pq4L1m5K6y7x8w9A=";
}
