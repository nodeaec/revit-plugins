using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace NodeAec.Licensing.Sample.Gate;

/// <summary>
/// Verifica a assinatura Ed25519 (RFC 8032) do lease local <b>antes</b> de qualquer claim
/// ser confiável. É o espelho desacoplado da implementação canônica do Node.aec Connector
/// (<c>Cryptography/LeaseSignatureVerifier.cs</c>), sem dependência do assembly do
/// Connector, para que possa ser copiada junto com o Micro-SDK.
///
/// Chaves candidatas, nesta ordem:
/// 1) JWKS em cache gravado pelo Connector em <c>license-jwks.json</c> (mesmo diretório do lease);
/// 2) âncora SPKI opcional definida em <c>NODEAEC_LICENSE_PUBLIC_KEY_SPKI</c>.
///
/// Falha sempre em modo fechado: sem chave utilizável ou com assinatura inválida, o lease
/// não é aceito. Nenhuma requisição de rede é feita aqui.
/// </summary>
public static class LeaseSignatureVerifier
{
    /// <summary>Algoritmo de assinatura aceito nos leases (EdDSA / Ed25519).</summary>
    public const string AcceptedAlgorithm = "EdDSA";

    /// <summary>Tamanho da chave bruta Ed25519 em bytes.</summary>
    private const int RawKeySize = 32;

    /// <summary>Tamanho da assinatura Ed25519 em bytes.</summary>
    private const int SignatureSize = 64;

    /// <summary>Nome do arquivo de cache do JWKS gravado pelo Connector ao lado do lease.</summary>
    public const string JwksFileName = "license-jwks.json";

    /// <summary>Variável de ambiente opcional com uma chave pública SPKI fixa (base64).</summary>
    public const string PinnedKeyVariable = "NODEAEC_LICENSE_PUBLIC_KEY_SPKI";

    /// <summary>Prefixo DER fixo de um SubjectPublicKeyInfo Ed25519 (RFC 8410).</summary>
    private static readonly byte[] SpkiEd25519Prefix =
    {
        0x30, 0x2a, 0x30, 0x05, 0x06, 0x03, 0x2b, 0x65, 0x70, 0x03, 0x21, 0x00,
    };

    /// <summary>
    /// Verifica a assinatura Ed25519 de um JWT de lease no formato <c>header.payload.signature</c>.
    /// </summary>
    /// <param name="jwt">Token JWT bruto.</param>
    /// <param name="jwksFilePath">Caminho do cache local do JWKS gravado pelo Connector.</param>
    /// <param name="reason">Motivo legível da falha quando o retorno é <c>false</c> (para log; nunca exibir ao usuário).</param>
    /// <returns><c>true</c> somente quando o header é EdDSA e a assinatura confere com uma chave candidata.</returns>
    public static bool TryVerify(string? jwt, string jwksFilePath, out string? reason)
    {
        reason = null;

        if (string.IsNullOrWhiteSpace(jwt))
        {
            reason = "token ausente";
            return false;
        }

        string[] parts = jwt.Trim().Split('.');
        if (parts.Length != 3)
        {
            reason = "estrutura JWT inválida";
            return false;
        }

        if (!TryReadHeader(parts[0], out string? algorithm, out string? kid, out reason))
        {
            return false;
        }

        if (!string.Equals(algorithm, AcceptedAlgorithm, StringComparison.Ordinal))
        {
            reason = $"algoritmo não suportado ({algorithm})";
            return false;
        }

        byte[]? signature = TryFromBase64Url(parts[2]);
        if (signature == null || signature.Length != SignatureSize)
        {
            reason = "assinatura em formato inválido";
            return false;
        }

        byte[] data = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var candidates = OrderCandidates(kid, LoadVerificationKeys(jwksFilePath));
        if (candidates.Count == 0)
        {
            reason = "chave pública de verificação indisponível (JWKS ausente)";
            return false;
        }

        foreach (var candidate in candidates)
        {
            if (VerifySignature(data, signature, candidate.RawKey))
            {
                return true;
            }
        }

        reason = kid == null
            ? "assinatura não corresponde a nenhuma chave conhecida"
            : $"assinatura não corresponde à chave {kid}";
        return false;
    }

    /// <summary>
    /// Decodifica uma chave pública SPKI (base64 padrão) de Ed25519 para os 32 bytes brutos da curva.
    /// </summary>
    /// <param name="spkiBase64">Chave SPKI em base64 (44 bytes DER no total).</param>
    /// <param name="rawKey">Chave bruta de 32 bytes quando o retorno é <c>true</c>.</param>
    /// <param name="reason">Motivo legível da falha quando o retorno é <c>false</c>.</param>
    /// <returns><c>true</c> quando a chave SPKI tem o formato Ed25519 esperado.</returns>
    public static bool TryDecodeSpkiBase64(string? spkiBase64, out byte[] rawKey, out string? reason)
    {
        rawKey = Array.Empty<byte>();

        byte[]? der = null;
        try
        {
            der = string.IsNullOrWhiteSpace(spkiBase64) ? null : Convert.FromBase64String(spkiBase64.Trim());
        }
        catch (FormatException)
        {
            reason = "chave SPKI não é base64 válido";
            return false;
        }

        if (der == null)
        {
            reason = "chave SPKI vazia";
            return false;
        }

        if (der.Length != SpkiEd25519Prefix.Length + RawKeySize)
        {
            reason = "chave SPKI com tamanho inesperado";
            return false;
        }

        for (int i = 0; i < SpkiEd25519Prefix.Length; i++)
        {
            if (der[i] != SpkiEd25519Prefix[i])
            {
                reason = "chave SPKI não é Ed25519";
                return false;
            }
        }

        // Cópia explícita em vez de fatia com range (`der[i..]`), que exige System.Index/
        // System.Range — tipos ausentes no .NET Framework 4.8 (Revit 2023/2024).
        rawKey = new byte[RawKeySize];
        Array.Copy(der, SpkiEd25519Prefix.Length, rawKey, 0, RawKeySize);
        reason = null;
        return true;
    }

    /// <summary>
    /// Monta a lista de chaves candidatas: primeiro as âncoras fixas, depois as que batem
    /// com o <c>kid</c> do header e por último as entradas do JWKS sem <c>kid</c>.
    /// </summary>
    private static List<PublicKeyCandidate> OrderCandidates(string? kid, IReadOnlyList<PublicKeyCandidate> cached)
    {
        var ordered = new List<PublicKeyCandidate>();

        string? pinnedSpki = Environment.GetEnvironmentVariable(PinnedKeyVariable);
        if (!string.IsNullOrWhiteSpace(pinnedSpki) &&
            TryDecodeSpkiBase64(pinnedSpki, out byte[] pinnedRaw, out _))
        {
            ordered.Add(new PublicKeyCandidate(null, pinnedRaw));
        }

        if (kid != null)
        {
            foreach (var candidate in cached)
            {
                if (string.Equals(candidate.Kid, kid, StringComparison.Ordinal))
                {
                    ordered.Add(candidate);
                }
            }
        }

        foreach (var candidate in cached)
        {
            if (candidate.Kid == null)
            {
                ordered.Add(candidate);
            }
        }

        return ordered;
    }

    /// <summary>
    /// Lê as chaves Ed25519 do cache local do JWKS. Entradas malformadas são ignoradas
    /// individualmente; arquivo ausente ou corrompido resulta em lista vazia (modo fechado).
    /// </summary>
    private static List<PublicKeyCandidate> LoadVerificationKeys(string jwksFilePath)
    {
        var keys = new List<PublicKeyCandidate>();

        try
        {
            if (string.IsNullOrWhiteSpace(jwksFilePath) || !File.Exists(jwksFilePath))
            {
                return keys;
            }

            string json = File.ReadAllText(jwksFilePath);
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

                byte[]? raw = TryFromBase64Url(GetString(jwk, "x"));
                if (raw == null || raw.Length != RawKeySize) continue;

                keys.Add(new PublicKeyCandidate(GetString(jwk, "kid"), raw));
            }
        }
        catch (Exception)
        {
            // Cache ilegível nunca deve derrubar a validação: apenas não confere.
            return new List<PublicKeyCandidate>();
        }

        return keys;
    }

    /// <summary>Decodifica o header JWT e extrai <c>alg</c> e <c>kid</c>.</summary>
    private static bool TryReadHeader(string headerB64, out string? algorithm, out string? kid, out string? reason)
    {
        algorithm = null;
        kid = null;

        byte[]? headerBytes = TryFromBase64Url(headerB64);
        if (headerBytes == null)
        {
            reason = "header JWT inválido";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(headerBytes));
            var root = doc.RootElement;
            algorithm = root.TryGetProperty("alg", out var alg) && alg.ValueKind == JsonValueKind.String
                ? alg.GetString()
                : null;
            kid = root.TryGetProperty("kid", out var kidEl) && kidEl.ValueKind == JsonValueKind.String
                ? kidEl.GetString()
                : null;
        }
        catch (JsonException)
        {
            reason = "header JWT não é JSON válido";
            return false;
        }

        if (string.IsNullOrWhiteSpace(algorithm))
        {
            reason = "header JWT sem algoritmo";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>Lê uma propriedade string do JWK, ou <c>null</c>.</summary>
    private static string? GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>Executa a verificação Ed25519 com a chave bruta informada.</summary>
    private static bool VerifySignature(byte[] data, byte[] signature, byte[] rawKey)
    {
        try
        {
            var signer = new Ed25519Signer();
            signer.Init(false, new Ed25519PublicKeyParameters(rawKey, 0));
            signer.BlockUpdate(data, 0, data.Length);
            return signer.VerifySignature(signature);
        }
        catch (Exception)
        {
            // Chave malformada nunca deve derrubar a validação: apenas não confere.
            return false;
        }
    }

    /// <summary>Converte base64url (com ou sem padding) em bytes, ou <c>null</c> se inválido.</summary>
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

    /// <summary>Par (kid, chave bruta) usado na seleção de chaves de verificação.</summary>
    private readonly struct PublicKeyCandidate
    {
        public PublicKeyCandidate(string? kid, byte[] rawKey)
        {
            Kid = kid;
            RawKey = rawKey;
        }

        public string? Kid { get; }

        public byte[] RawKey { get; }
    }
}
