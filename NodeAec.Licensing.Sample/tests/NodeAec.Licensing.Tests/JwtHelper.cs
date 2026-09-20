using System.Text;
using System.Text.Json;

namespace NodeAec.Licensing.Tests;

/// <summary>
/// Builds unsigned JWT-shaped lease tokens for offline-validation tests.
/// Production tokens are Ed25519-signed by the server; the offline validator
/// under test currently does NOT verify the signature (see the forgeability
/// test), so any signature segment is accepted.
/// </summary>
internal static class JwtHelper
{
    internal static string Base64UrlEncode(string raw) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static string CreateLeaseToken(object payload)
    {
        const string header = "{\"alg\":\"EdDSA\",\"typ\":\"JWT\"}";
        return $"{Base64UrlEncode(header)}.{Base64UrlEncode(JsonSerializer.Serialize(payload))}.testsig";
    }

    internal static object ValidPayload(
        string machineId,
        string? productSlug = "my-product",
        string? productName = "My Product",
        DateTimeOffset? expiresAt = null) =>
        new
        {
            sub = "user-1",
            lic = "NAEC-TEST-KEY",
            prd = productSlug,
            prn = productName,
            mid = machineId,
            typ = "commercial",
            exp = (expiresAt ?? DateTimeOffset.UtcNow.AddDays(10)).ToUnixTimeSeconds(),
            iat = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
        };
}
