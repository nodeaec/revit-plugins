using System;
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

    [JsonPropertyName("granted")]
    public bool Granted { get; set; } = true;

    [JsonPropertyName("expiresAt")]
    public string? ExpiresAtString { get; set; }

    [JsonIgnore]
    public DateTimeOffset? ExpiresAt
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ExpiresAtString)) return null;
            if (DateTimeOffset.TryParse(ExpiresAtString, out var dt)) return dt;
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
