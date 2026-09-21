using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NodeAec.Connector.Models;

/// <summary>
/// Estrutura de claims do Master Entitlements Lease JWT emitido pela plataforma Node.aec.
/// </summary>
public class MasterLeasePayload
{
    [JsonPropertyName("iss")]
    public string? Iss { get; set; }

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
    public List<EntitlementItem> Entitlements { get; set; } = new();

    [JsonIgnore]
    public DateTimeOffset ExpiresAt => DateTimeOffset.FromUnixTimeSeconds(Exp);

    [JsonIgnore]
    public DateTimeOffset IssuedAt => DateTimeOffset.FromUnixTimeSeconds(Iat);

    [JsonIgnore]
    public bool IsExpired => ExpiresAt < DateTimeOffset.UtcNow;
}
