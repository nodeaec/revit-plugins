using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NodeAec.Connector.Diagnostics;

namespace NodeAec.Connector.Storage;

/// <summary>
/// Cache local do JWKS público da plataforma Node.aec (<c>GET /license/jwks</c>).
/// O Connector atualiza o cache a cada sincronização/validação de lease e o gate
/// (<c>LeaseSignatureVerifier</c>) lê apenas deste cache — nunca baixa chaves em
/// tempo de validação offline. O JWKS é material público, portanto é gravado em
/// texto simples (sem DPAPI); a segurança vem da assinatura do lease, não do
/// armazenamento da chave.
/// </summary>
public static class SigningKeyStore
{
    /// <summary>Nome do arquivo de cache do JWKS no diretório base do Connector.</summary>
    public const string JwksFileName = "license-jwks.json";

    /// <summary>Retorna o caminho absoluto do arquivo de cache do JWKS.</summary>
    public static string GetJwksFilePath() => Path.Combine(LeaseStorage.GetBaseDirectory(), JwksFileName);

    /// <summary>
    /// Lê o JWKS em cache, ou <c>null</c> quando ainda não há cache válido.
    /// </summary>
    public static string? LoadCachedJwks()
    {
        try
        {
            string path = GetJwksFilePath();
            if (!File.Exists(path)) return null;

            string json = File.ReadAllText(path);
            return HasUsableKey(json) ? json : null;
        }
        catch (Exception ex)
        {
            ConnectorLog.Write("WARN", $"Falha ao ler cache do JWKS: {ex.GetType().Name}.");
            return null;
        }
    }

    /// <summary>
    /// Persiste atomicamente um documento JWKS. Retorna <c>false</c> (e não grava)
    /// quando o documento não contém nenhuma chave OKP/Ed25519 utilizável.
    /// </summary>
    /// <param name="jwksJson">Documento JWKS retornado pela API.</param>
    public static bool SaveCachedJwks(string? jwksJson)
    {
        if (!HasUsableKey(jwksJson))
        {
            return false;
        }

        try
        {
            LeaseStorage.WriteAllBytesAtomic(GetJwksFilePath(), System.Text.Encoding.UTF8.GetBytes(jwksJson!));
            return true;
        }
        catch (Exception ex)
        {
            ConnectorLog.Write("ERROR", $"Falha ao gravar cache do JWKS: {ex.GetType().Name}.");
            return false;
        }
    }

    /// <summary>
    /// Busca <c>GET /license/jwks</c> no servidor e atualiza o cache local.
    /// Melhor esforço: falhas de rede/servidor são registradas em log e retornam
    /// <c>false</c> sem derrubar a sincronização de leases em andamento.
    /// </summary>
    /// <param name="baseUrl">URL base da API Node.aec.</param>
    /// <param name="httpClient">Cliente HTTP a ser utilizado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public static async Task<bool> RefreshAsync(
        string baseUrl,
        HttpClient httpClient,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient
                .GetAsync($"{baseUrl.TrimEnd('/')}/license/jwks", cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                ConnectorLog.Write("WARN", $"Atualização do JWKS recusada pela API ({(int)response.StatusCode}).");
                return false;
            }

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!SaveCachedJwks(json))
            {
                ConnectorLog.Write("WARN", "Atualização do JWKS ignorada: documento sem chave utilizável.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            ConnectorLog.Write("WARN", $"Falha ao atualizar JWKS: {ex.GetType().Name}.");
            return false;
        }
    }

    /// <summary>
    /// Retorna as chaves públicas Ed25519 (kid + 32 bytes brutos) presentes no cache.
    /// Entradas malformadas são ignoradas individualmente.
    /// </summary>
    public static IReadOnlyList<(string? Kid, byte[] RawKey)> LoadVerificationKeys()
    {
        var keys = new List<(string? Kid, byte[] RawKey)>();
        string? json = LoadCachedJwks();
        if (json == null) return keys;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("keys", out var keyArray) ||
                keyArray.ValueKind != JsonValueKind.Array)
            {
                return keys;
            }

            foreach (var jwk in keyArray.EnumerateArray())
            {
                if (jwk.ValueKind != JsonValueKind.Object) continue;
                if (!string.Equals(GetString(jwk, "kty"), "OKP", StringComparison.Ordinal)) continue;
                if (!string.Equals(GetString(jwk, "crv"), "Ed25519", StringComparison.Ordinal)) continue;

                string? x = GetString(jwk, "x");
                byte[]? raw = TryFromBase64Url(x);
                if (raw == null || raw.Length != 32) continue;

                keys.Add((GetString(jwk, "kid"), raw));
            }
        }
        catch (JsonException ex)
        {
            ConnectorLog.Write("WARN", $"Cache do JWKS corrompido: {ex.GetType().Name}.");
        }

        return keys;
    }

    /// <summary>Verifica se o documento contém ao menos uma chave OKP/Ed25519 válida.</summary>
    private static bool HasUsableKey(string? jwksJson)
    {
        if (string.IsNullOrWhiteSpace(jwksJson)) return false;

        try
        {
            using var doc = JsonDocument.Parse(jwksJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty("keys", out var keyArray) ||
                keyArray.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var jwk in keyArray.EnumerateArray())
            {
                if (jwk.ValueKind != JsonValueKind.Object) continue;
                if (!string.Equals(GetString(jwk, "kty"), "OKP", StringComparison.Ordinal)) continue;
                if (!string.Equals(GetString(jwk, "crv"), "Ed25519", StringComparison.Ordinal)) continue;
                if (TryFromBase64Url(GetString(jwk, "x")) is byte[] raw && raw.Length == 32) return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Lê uma propriedade string do JWK, ou <c>null</c>.</summary>
    private static string? GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>Converte base64url em bytes, ou <c>null</c> se inválido.</summary>
    private static byte[]? TryFromBase64Url(string? input)
    {
        if (string.IsNullOrEmpty(input)) return null;

        string base64 = input.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
            case 1: return null;
        }

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
