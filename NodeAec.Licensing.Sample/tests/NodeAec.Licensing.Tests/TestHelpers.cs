using System;
using System.IO;
using System.Text;
using System.Text.Json;
using NodeAec.Licensing.Sample.Gate;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace NodeAec.Licensing.Tests;

/// <summary>
/// Utilitários dos testes do Micro-SDK.
///
/// Os leases de teste são assinados de verdade com Ed25519 e a chave pública correspondente
/// é publicada no cache do JWKS (<c>license-jwks.json</c>), reproduzindo o que o Node.aec
/// Connector grava a partir de <c>GET /license/jwks</c>. Nenhum teste depende de gravar texto
/// puro no disco ou de o DPAPI estar disponível na sessão.
/// </summary>
public static class TestHelpers
{
    /// <summary>kid usado pela chave de teste, presente no header e no JWKS.</summary>
    public const string TestKeyId = "test-key-1";

    /// <summary>Semente Ed25519 do vetor de teste oficial do RFC 8032 §7.1.</summary>
    private static readonly byte[] TestSeed = Convert.FromHexString(
        "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");

    public static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "NodeAecSampleTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void DeleteTempDir(string? dir)
    {
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    /// <summary>
    /// Grava ao lado do lease o cache do JWKS com a chave pública da chave de teste e
    /// neutraliza qualquer âncora fixa herdada do ambiente, para que a verificação de
    /// assinatura seja determinística.
    /// </summary>
    public static void InstallSigningKey(string dir)
    {
        Environment.SetEnvironmentVariable(LeaseSignatureVerifier.PinnedKeyVariable, null);

        byte[] raw = TestRawPublicKey();

        string jwks = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "OKP",
                    crv = "Ed25519",
                    x = Base64UrlEncode(raw),
                    kid = TestKeyId,
                    alg = LeaseSignatureVerifier.AcceptedAlgorithm
                }
            }
        });

        File.WriteAllText(Path.Combine(dir, LeaseSignatureVerifier.JwksFileName), jwks);
    }

    /// <summary>
    /// Monta um lease JWT assinado com a chave de teste.
    /// Os parâmetros opcionais existem para exercitar os caminhos de rejeição (assinatura
    /// ausente, <c>iss</c>/<c>scope</c> indevidos, <c>iat</c> no futuro).
    /// </summary>
    public static string CreateTestJwt(
        string machineId,
        long expUnix,
        object entitlements,
        string iss = "node-aec",
        string scope = "master-lease",
        long? iat = null,
        bool sign = true,
        string kid = TestKeyId)
    {
        string header = JsonSerializer.Serialize(new { alg = LeaseSignatureVerifier.AcceptedAlgorithm, typ = "JWT", kid });
        string payload = JsonSerializer.Serialize(new
        {
            iss,
            sub = "usr_test123456",
            mid = machineId,
            scope,
            iat = iat ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            exp = expUnix,
            entitlements
        });

        string signingInput = $"{Base64UrlEncode(Encoding.UTF8.GetBytes(header))}." +
                              $"{Base64UrlEncode(Encoding.UTF8.GetBytes(payload))}";

        // Assinatura forjada (zeros) simula token adulterado: nunca confere com a chave.
        byte[] signature = sign ? Sign(Encoding.ASCII.GetBytes(signingInput)) : new byte[64];

        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    /// <summary>Chave pública bruta (32 bytes) correspondente à semente de teste.</summary>
    public static byte[] TestRawPublicKey()
    {
        return new Ed25519PrivateKeyParameters(TestSeed, 0).GeneratePublicKey().GetEncoded();
    }

    /// <summary>Assina um trecho com a semente Ed25519 de teste.</summary>
    public static byte[] Sign(byte[] data)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(TestSeed, 0));
        signer.BlockUpdate(data, 0, data.Length);
        return signer.GenerateSignature();
    }

    /// <summary>Converte bytes no formato base64url (sem padding) usado pelo JWT.</summary>
    public static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }
}
