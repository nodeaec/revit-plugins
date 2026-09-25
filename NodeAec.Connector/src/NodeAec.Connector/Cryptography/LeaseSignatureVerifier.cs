using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using NodeAec.Connector.Config;
using NodeAec.Connector.Storage;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace NodeAec.Connector.Cryptography;

/// <summary>
/// Verifica a assinatura Ed25519 (RFC 8032) de tokens de lease emitidos pela plataforma Node.aec
/// antes de qualquer claim ser confiável. As chaves públicas candidatas vêm, nesta ordem:
/// 1) JWKS em cache local (<c>%APPDATA%\NodeAec\license-jwks.json</c>, atualizado pelo Connector
///    a cada sincronização/validação via <c>GET /license/jwks</c>);
/// 2) âncora SPKI opcional definida em <c>NODEAEC_LICENSE_PUBLIC_KEY_SPKI</c> (operações que
///    preferem chave fixa em vez de rotação por JWKS).
/// Falha sempre em modo fechado: sem chave ou com assinatura inválida, o lease não é aceito.
/// </summary>
public static class LeaseSignatureVerifier
{
    /// <summary>Algoritmo de assinatura aceito nos leases (EdDSA / Ed25519).</summary>
    public const string AcceptedAlgorithm = "EdDSA";

    /// <summary>Tamanho da chave bruta Ed25519 em bytes.</summary>
    private const int RawKeySize = 32;

    /// <summary>Tamanho da assinatura Ed25519 em bytes.</summary>
    private const int SignatureSize = 64;

    /// <summary>Prefixo DER fixo de um SubjectPublicKeyInfo Ed25519 (RFC 8410).</summary>
    private static readonly byte[] SpkiEd25519Prefix =
    {
        0x30, 0x2a, 0x30, 0x05, 0x06, 0x03, 0x2b, 0x65, 0x70, 0x03, 0x21, 0x00,
    };

    /// <summary>
    /// Verifica a assinatura Ed25519 de um JWT de lease no formato <c>header.payload.signature</c>.
    /// </summary>
    /// <param name="jwt">Token JWT bruto.</param>
    /// <param name="reason">Motivo legível da falha quando o retorno é <c>false</c> (para log; nunca exibir internamente).</param>
    /// <returns><c>true</c> somente quando o header é EdDSA e a assinatura confere com uma chave candidata.</returns>
    public static bool TryVerify(string? jwt, out string? reason)
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
        var candidates = OrderCandidates(kid, SigningKeyStore.LoadVerificationKeys());
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

        reason = kid == null ? "assinatura não corresponde a nenhuma chave conhecida"
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
    /// Monta a lista de chaves candidatas: primeiro as que batem com o <c>kid</c> do header,
    /// depois as âncoras fixas sem <c>kid</c> (ex.: <c>NODEAEC_LICENSE_PUBLIC_KEY_SPKI</c>).
    /// </summary>
    private static List<PublicKeyCandidate> OrderCandidates(string? kid, IReadOnlyList<(string? Kid, byte[] RawKey)> cached)
    {
        var ordered = new List<PublicKeyCandidate>();

        string? pinnedSpki = ConnectorConfig.LicensePublicKeySpkiBase64;
        string? decodeReason = null;
        if (!string.IsNullOrWhiteSpace(pinnedSpki) &&
            TryDecodeSpkiBase64(pinnedSpki, out byte[] pinnedRaw, out decodeReason))
        {
            ordered.Add(new PublicKeyCandidate(null, pinnedRaw));
        }
        else if (!string.IsNullOrWhiteSpace(pinnedSpki))
        {
            Diagnostics.ConnectorLog.Write("WARN", $"Chave SPKI fixa ignorada: {decodeReason}.");
        }

        if (kid != null)
        {
            foreach (var candidate in cached)
            {
                if (string.Equals(candidate.Kid, kid, StringComparison.Ordinal))
                {
                    ordered.Add(new PublicKeyCandidate(candidate.Kid, candidate.RawKey));
                }
            }
        }

        foreach (var candidate in cached)
        {
            if (candidate.Kid == null)
            {
                ordered.Add(new PublicKeyCandidate(candidate.Kid, candidate.RawKey));
            }
        }

        return ordered;
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
    private static byte[]? TryFromBase64Url(string input)
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
    public readonly record struct PublicKeyCandidate(string? Kid, byte[] RawKey);
}
