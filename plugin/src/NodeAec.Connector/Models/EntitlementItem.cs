using System;
using System.Globalization;
using System.Text.Json.Serialization;

namespace NodeAec.Connector.Models;

/// <summary>
/// Representa uma concessão ou autorização individual de produto contida no Master Entitlements Lease.
/// </summary>
public class EntitlementItem
{
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("licenseKey")]
    public string? LicenseKey { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "perpetual";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "active";

    /// <summary>
    /// Claim <c>granted</c> do token: o emissor só inclui concessões concedidas, mas o
    /// valor é honrado por <see cref="IsActive"/> (L6) — <c>false</c> nega, mesmo que o
    /// resto do item esteja válido. Ausente no token → padrão <c>true</c>.
    /// </summary>
    [JsonPropertyName("granted")]
    public bool Granted { get; set; } = true;

    [JsonPropertyName("expiresAt")]
    public string? ExpiresAtString { get; set; }

    /// <summary>
    /// Prazo de expiração interpretado de forma determinística: cultura invariante (uma
    /// data no formato do servidor nunca depende do formato regional da máquina) e
    /// <see cref="DateTimeStyles.AssumeUniversal"/> — sem offset explícito, a data vale
    /// meia-noite UTC, não meia-noite local (a máquina não é adiantada/atrasada na
    /// expiração pelo próprio fuso). Inválido/fora da faixa → <c>null</c>.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset? ExpiresAt
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ExpiresAtString)) return null;
            if (DateTimeOffset.TryParse(ExpiresAtString, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)) return dt;
            return null;
        }
    }

    [JsonPropertyName("maxActivations")]
    public int? MaxActivations { get; set; }

    [JsonPropertyName("activeActivations")]
    public int? ActiveActivations { get; set; }

    /// <summary>
    /// Indica se a concessão está ativa e válida para uso imediato.
    /// </summary>
    public bool IsActive()
    {
        // L6: o claim `granted` era deserializado e ignorado. Honrá-lo aqui mantém o
        // modelo e o gate coerentes: concessão explicitamente não concedida nunca está
        // ativa (fail-closed caso o emissor passe a emitir `granted: false`).
        if (!Granted)
        {
            return false;
        }

        if (!string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ExpiresAt.HasValue && ExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            return false;
        }

        return true;
    }
}
