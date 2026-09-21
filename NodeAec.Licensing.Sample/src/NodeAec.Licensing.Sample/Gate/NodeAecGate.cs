using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NodeAec.Licensing.Sample.Gate;

/// <summary>
/// Micro-SDK canônico de validação de licenças para plugins do ecossistema Node.aec.
/// Executa em menos de 1ms, em memória/disco local, sem nenhuma requisição HTTP.
/// A sincronização, login SSO e governança de assentos são gerenciados pelo Node.aec Connector.
/// </summary>
public static class NodeAecGate
{
    private static string? _customBasePath;

    /// <summary>
    /// Injeta um caminho base personalizado para testes unitários isolados.
    /// </summary>
    public static void SetCustomBasePath(string? path)
    {
        _customBasePath = path;
    }

    public static string GetLeaseFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_customBasePath))
        {
            return Path.Combine(_customBasePath, "entitlements.lease");
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "NodeAec", "entitlements.lease");
    }

    /// <summary>
    /// Resultado retornado pela validação do Micro-SDK.
    /// </summary>
    public class GateResult
    {
        public bool IsLicensed { get; set; }
        public string? LicenseType { get; set; }
        public string? LicenseKey { get; set; }
        public string? ProductName { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public string Message { get; set; } = string.Empty;

        public static GateResult Success(string? type, string? key, string? name, DateTimeOffset? expiresAt, string message = "Licença ativa e verificada.")
        {
            return new GateResult
            {
                IsLicensed = true,
                LicenseType = type ?? "commercial",
                LicenseKey = key,
                ProductName = name,
                ExpiresAt = expiresAt,
                Message = message
            };
        }

        public static GateResult Failure(string message)
        {
            return new GateResult
            {
                IsLicensed = false,
                Message = message
            };
        }
    }

    /// <summary>
    /// Valida se o produto informado pelo seu <paramref name="productSlug"/> possui
    /// autorização ativa nesta estação. Não realiza chamadas de rede.
    /// </summary>
    public static GateResult Validate(string productSlug)
    {
        if (string.IsNullOrWhiteSpace(productSlug))
        {
            return GateResult.Failure("Slug do produto não informado para validação.");
        }

        string leasePath = GetLeaseFilePath();
        if (!File.Exists(leasePath))
        {
            return GateResult.Failure("Nenhuma credencial do Node.aec encontrada nesta estação. Abra o Node.aec Connector na Ribbon para entrar com sua conta ou ativar sua licença.");
        }

        try
        {
            byte[] fileBytes = File.ReadAllBytes(leasePath);
            if (fileBytes.Length == 0)
            {
                return GateResult.Failure("Arquivo de concessão vazio ou corrompido. Abra o Node.aec Connector para sincronizar.");
            }

            string jwt;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    byte[] decrypted = ProtectedData.Unprotect(fileBytes, null, DataProtectionScope.CurrentUser);
                    jwt = Encoding.UTF8.GetString(decrypted);
                }
                catch
                {
                    // Fallback para arquivo não criptografado (ambiente de testes)
                    jwt = Encoding.UTF8.GetString(fileBytes);
                }
            }
            else
            {
                jwt = Encoding.UTF8.GetString(fileBytes);
            }

            var payload = ParseJwtPayload(jwt);
            if (payload == null)
            {
                return GateResult.Failure("Estrutura da concessão inválida. Abra o Node.aec Connector para ressincronizar.");
            }

            // 1. Validação de hardware (Machine ID)
            string currentMachineId = HardwareId.GetMachineId();
            if (!string.Equals(payload.Mid, currentMachineId, StringComparison.OrdinalIgnoreCase))
            {
                return GateResult.Failure("A concessão de licenças foi emitida para outra estação de trabalho (Hardware ID divergente).");
            }

            // 2. Validação de expiração da tolerância offline (30 dias)
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (payload.Exp < now)
            {
                var expDate = DateTimeOffset.FromUnixTimeSeconds(payload.Exp);
                return GateResult.Failure($"O prazo de tolerância offline expirou em {expDate:dd/MM/yyyy}. Conecte-se à internet para sincronizar.");
            }

            // 3. Busca do produto na lista de concessões
            var item = payload.Entitlements?.FirstOrDefault(e =>
                string.Equals(e.Slug, productSlug.Trim(), StringComparison.OrdinalIgnoreCase));

            if (item == null)
            {
                return GateResult.Failure($"O produto '{productSlug}' não consta nas licenças ativas desta conta. Adquira ou ative no catálogo Node.aec.");
            }

            string status = item.Status?.Trim().ToLowerInvariant() ?? string.Empty;
            if (status == "seat_limit_reached")
            {
                return GateResult.Failure($"O limite de computadores simultâneos para '{item.Name ?? productSlug}' foi atingido.");
            }

            if (item.ExpiresAtUnix.HasValue && item.ExpiresAtUnix.Value < now)
            {
                var itemExpDate = DateTimeOffset.FromUnixTimeSeconds(item.ExpiresAtUnix.Value);
                return GateResult.Failure($"A licença ou período de teste de '{item.Name ?? productSlug}' expirou em {itemExpDate:dd/MM/yyyy}.");
            }

            if (status != "active")
            {
                return GateResult.Failure($"A licença de '{item.Name ?? productSlug}' está com status '{item.Status}'.");
            }

            DateTimeOffset? itemExpires = item.ExpiresAtUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(item.ExpiresAtUnix.Value)
                : null;

            return GateResult.Success(item.Type, item.LicenseKey, item.Name ?? productSlug, itemExpires);
        }
        catch (Exception ex)
        {
            return GateResult.Failure($"Falha ao verificar concessão local: {ex.Message}");
        }
    }

    /// <summary>
    /// Invoca a interface do Node.aec Connector caso carregado no processo atual,
    /// ou abre o portal da plataforma no navegador.
    /// </summary>
    public static void OpenConnector()
    {
        try
        {
            var uiType = Type.GetType("NodeAec.Connector.UI.ConnectorWindow, NodeAec.Connector");
            if (uiType != null)
            {
                var openMethod = uiType.GetMethod("Open", BindingFlags.Public | BindingFlags.Static);
                openMethod?.Invoke(null, new object?[] { null });
                return;
            }
        }
        catch
        {
            // Fallback para navegador
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://nodeaec.com.br",
                UseShellExecute = true
            });
        }
        catch
        {
            // Silencioso
        }
    }

    private static InternalLeasePayload? ParseJwtPayload(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var parts = token.Split('.');
        if (parts.Length < 2) return null;

        try
        {
            string base64 = parts[1].Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }

            byte[] jsonBytes = Convert.FromBase64String(base64);
            return JsonSerializer.Deserialize<InternalLeasePayload>(jsonBytes);
        }
        catch
        {
            return null;
        }
    }

    internal class InternalLeasePayload
    {
        [JsonPropertyName("sub")]
        public string? Sub { get; set; }

        [JsonPropertyName("mid")]
        public string? Mid { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("iat")]
        public long Iat { get; set; }

        [JsonPropertyName("exp")]
        public long Exp { get; set; }

        [JsonPropertyName("entitlements")]
        public List<InternalEntitlement>? Entitlements { get; set; }
    }

    internal class InternalEntitlement
    {
        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("licenseKey")]
        public string? LicenseKey { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "perpetual";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "active";

        [JsonPropertyName("expiresAt")]
        public long? ExpiresAtUnix { get; set; }

        [JsonPropertyName("granted")]
        public bool Granted { get; set; } = true;
    }
}
