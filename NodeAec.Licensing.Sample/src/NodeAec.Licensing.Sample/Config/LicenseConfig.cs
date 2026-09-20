using System;
using System.IO;
using NodeAec.Licensing.Client;

namespace NodeAec.Licensing.Sample.Config;

/// <summary>
/// Configuração central da integração com a plataforma Node.aec.
/// Universal para qualquer produto da plataforma.
/// Repositório oficial: https://github.com/nodeaec/revit-plugins
/// </summary>
public static class LicenseConfig
{
    /// <summary>
    /// URL base da API Node.aec em produção.
    /// A integração utiliza sempre o ambiente oficial de produção.
    /// </summary>
    public const string ApiUrl = "https://api.nodeaec.com.br";

    /// <summary>
    /// Identificador opcional do produto (caso o desenvolvedor queira isolar leases por produto).
    /// Padrão universal: "nodeaec".
    /// </summary>
    public static string ProductSlug =>
        Environment.GetEnvironmentVariable("NODE_AEC_PRODUCT_SLUG") ?? "nodeaec";

    /// <summary>
    /// URL do repositório oficial de integração e documentação.
    /// </summary>
    public const string IntegrationRepoUrl = "https://github.com/nodeaec/revit-plugins";

    /// <summary>
    /// Chave pública Ed25519 SPKI de produção da Node.aec (RFC 7517 / RFC 8032).
    /// Permite validação offline criptográfica sem dependências de rede.
    /// </summary>
    public const string DefaultPublicKeyPem =
        "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEArMYcaZMAlBeimfR6twrHZndEWOSaIHlSURYFhTjalMg=\n-----END PUBLIC KEY-----";

    public static string PublicKeyPem =>
        Environment.GetEnvironmentVariable("NODE_AEC_PUBLIC_KEY_PEM") ?? DefaultPublicKeyPem;

    public static readonly string LicenseDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NodeAec"
    );

    public static readonly string LicenseFilePath = Path.Combine(LicenseDirectory, "license.key");

    /// <summary>
    /// Cria uma instância configurada de <see cref="NodeAecLicenseClient"/>.
    /// </summary>
    public static NodeAecLicenseClient CreateClient(string? productSlug = null)
    {
        return new NodeAecLicenseClient(ApiUrl, productSlug ?? ProductSlug, PublicKeyPem);
    }

    /// <summary>
    /// Carrega a última chave de licença utilizada.
    /// </summary>
    public static string? LoadStoredKey()
    {
        try
        {
            string? env = Environment.GetEnvironmentVariable("NODE_AEC_LICENSE_KEY");
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim().ToUpperInvariant();

            if (File.Exists(LicenseFilePath))
            {
                string text = File.ReadAllText(LicenseFilePath).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.ToUpperInvariant();
                }
            }
        }
        catch
        {
            // Ignora falha de leitura em disco
        }
        return null;
    }

    /// <summary>
    /// Salva a chave de licença para facilitar reativação ou exibição.
    /// </summary>
    public static void SaveStoredKey(string key)
    {
        try
        {
            if (!Directory.Exists(LicenseDirectory))
            {
                Directory.CreateDirectory(LicenseDirectory);
            }
            File.WriteAllText(LicenseFilePath, key.Trim().ToUpperInvariant());
        }
        catch
        {
            // Best-effort
        }
    }
}
