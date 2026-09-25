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

    /// <summary>
    /// Instante de expiração (<c>exp</c>). Segundos unix fora da faixa plausível
    /// (0 = ausente, negativo, ou ≥ 2100-01-01) resultam em <c>null</c> em vez de
    /// lançar <see cref="ArgumentOutOfRangeException"/> — <c>FromUnixTimeSeconds</c>
    /// só é chamado após o teste de faixa (M5).
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset? ExpiresAt => IsPlausibleUnixSeconds(Exp)
        ? DateTimeOffset.FromUnixTimeSeconds(Exp)
        : null;

    /// <summary>
    /// Instante de emissão (<c>iat</c>), com a mesma faixa segura de <see cref="ExpiresAt"/>.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset? IssuedAt => IsPlausibleUnixSeconds(Iat)
        ? DateTimeOffset.FromUnixTimeSeconds(Iat)
        : null;

    /// <summary>
    /// Prazo de tolerância offline. Sem <c>exp</c> plausível o lease é tratado como
    /// expirado (modo fechado): um lease assinado legítimo sempre traz <c>exp</c> dentro
    /// da faixa, então prazo ilegível nunca libera.
    /// </summary>
    [JsonIgnore]
    public bool IsExpired => ExpiresAt is null || ExpiresAt < DateTimeOffset.UtcNow;

    /// <summary>Limite superior de segundos unix plausíveis: 01/01/2100.</summary>
    private const long MaxPlausibleUnixSeconds = 4102444800;

    /// <summary>
    /// Segundos unix válidos para claims de data: estritamente positivo (0 = ausente)
    /// e antes de 01/01/2100 (relógio adulterado / valor corrompido).
    /// </summary>
    private static bool IsPlausibleUnixSeconds(long seconds)
        => seconds > 0 && seconds < MaxPlausibleUnixSeconds;
}
