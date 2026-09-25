using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NodeAec.Connector.Cryptography;
using NodeAec.Connector.Models;
using NodeAec.Connector.Storage;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Xunit;

namespace NodeAec.Connector.Tests;

public class LeaseSignatureVerifierTests : IDisposable
{
    private readonly string _tempDir;

    public LeaseSignatureVerifierTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NodeAecVerifierTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        LeaseStorage.SetCustomBasePath(_tempDir);
    }

    public void Dispose()
    {
        LeaseStorage.SetCustomBasePath(null);
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Ed25519Signing_MatchesRfc8032KnownAnswerVector()
    {
        // Vetor TEST 1 da RFC 8032: assinatura determinística sobre mensagem vazia.
        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(TestHelpers.Rfc8032TestSeed, 0));
        signer.BlockUpdate(Array.Empty<byte>(), 0, 0);
        byte[] signature = signer.GenerateSignature();

        byte[] expected = Convert.FromHexString(
            "E5564300C360AC729086E2CC806E828A84877F1EB8E5D974D873E065224901555FB8821590A33BACC61E39701CF9B46BD25BF5F0595BBE24655141438E7A100B");
        Assert.Equal(expected, signature);
    }

    [Fact]
    public void Ed25519PublicKeyDerivation_MatchesRfc8032Vector()
    {
        byte[] publicKey = new Ed25519PrivateKeyParameters(TestHelpers.Rfc8032TestSeed, 0)
            .GeneratePublicKey()
            .GetEncoded();

        Assert.Equal(TestHelpers.Rfc8032TestPublicKey, publicKey);
    }

    [Fact]
    public void TryVerify_ValidSignedToken_ReturnsTrue()
    {
        TestHelpers.InstallTestSigningKey();
        string jwt = CreateToken();

        bool valid = LeaseSignatureVerifier.TryVerify(jwt, out string? reason);

        Assert.True(valid, reason);
        Assert.Null(reason);
    }

    [Fact]
    public void TryVerify_TamperedPayload_ReturnsFalse()
    {
        TestHelpers.InstallTestSigningKey();
        string jwt = CreateToken();

        string[] parts = jwt.Split('.');
        string payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(ToStandardBase64(parts[1])));
        string tamperedJson = payloadJson.Replace("\"usr_1\"", "\"usr_intruso\"");
        string tampered = $"{parts[0]}.{ToBase64Url(tamperedJson)}.{parts[2]}";

        bool valid = LeaseSignatureVerifier.TryVerify(tampered, out string? reason);

        Assert.False(valid);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void TryVerify_SignedByUnknownKey_ReturnsFalse()
    {
        TestHelpers.InstallTestSigningKey();

        // Chave de produção forjada: assinatura válida em si, mas não é a chave publicada.
        byte[] attackerSeed = SHA256Of("attacker-seed");
        string jwt = TestHelpers.CreateSignedJwt(
            new { iss = "node-aec", scope = "master-lease" },
            new Ed25519PrivateKeyParameters(attackerSeed, 0));

        bool valid = LeaseSignatureVerifier.TryVerify(jwt, out string? reason);

        Assert.False(valid);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void TryVerify_AlgorithmConfusion_RejectedBeforeAnything()
    {
        TestHelpers.InstallTestSigningKey();

        string header = ToBase64Url("{\"alg\":\"HS256\",\"typ\":\"JWT\",\"kid\":\"node-aec-test-1\"}");
        string payload = ToBase64Url("{\"iss\":\"node-aec\"}");
        string jwt = $"{header}.{payload}.c2lnbmF0dXJl";

        bool valid = LeaseSignatureVerifier.TryVerify(jwt, out string? reason);

        Assert.False(valid);
        Assert.Contains("algoritmo", reason);
    }

    [Fact]
    public void TryVerify_WithoutJwksCache_FailsClosed()
    {
        string jwt = CreateToken();

        bool valid = LeaseSignatureVerifier.TryVerify(jwt, out string? reason);

        Assert.False(valid);
        Assert.Contains("indisponível", reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("apenas-uma-parte")]
    [InlineData("a.b.c.d")]
    public void TryVerify_MalformedInput_ReturnsFalse(string? jwt)
    {
        TestHelpers.InstallTestSigningKey();

        Assert.False(LeaseSignatureVerifier.TryVerify(jwt, out string? reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void TryDecodeSpkiBase64_RoundTripsEd25519PublicKey()
    {
        byte[] raw = TestHelpers.Rfc8032TestPublicKey;
        byte[] der = new byte[12 + raw.Length];
        new byte[]
        {
            0x30, 0x2a, 0x30, 0x05, 0x06, 0x03, 0x2b, 0x65, 0x70, 0x03, 0x21, 0x00,
        }.CopyTo(der, 0);
        raw.CopyTo(der, 12);
        string spki = Convert.ToBase64String(der);

        bool ok = LeaseSignatureVerifier.TryDecodeSpkiBase64(spki, out byte[] decoded, out string? reason);

        Assert.True(ok, reason);
        Assert.Equal(raw, decoded);
    }

    [Fact]
    public void TryDecodeSpkiBase64_RejectsNonEd25519AndGarbage()
    {
        Assert.False(LeaseSignatureVerifier.TryDecodeSpkiBase64("não-é-base64!!", out _, out _));
        Assert.False(LeaseSignatureVerifier.TryDecodeSpkiBase64(Convert.ToBase64String(new byte[10]), out _, out _));
        Assert.False(LeaseSignatureVerifier.TryDecodeSpkiBase64(null, out _, out _));
    }

    [Fact]
    public void LoadVerificationKeys_IgnoresMalformedEntries()
    {
        string goodX = ToBase64Url(TestHelpers.Rfc8032TestPublicKey);
        string jwks =
            "{\"keys\":[" +
            "{\"kty\":\"OKP\",\"crv\":\"Ed25519\",\"x\":\"curta\",\"kid\":\"ruim\"}," +
            "{\"kty\":\"RSA\",\"n\":\"abc\",\"kid\":\"rsa\"}," +
            $"{{\"kty\":\"OKP\",\"crv\":\"Ed25519\",\"x\":\"{goodX}\",\"kid\":\"boa\"}}" +
            "]}";
        Assert.True(SigningKeyStore.SaveCachedJwks(jwks));

        var keys = SigningKeyStore.LoadVerificationKeys();

        Assert.Single(keys);
        Assert.Equal("boa", keys[0].Kid);
        Assert.Equal(TestHelpers.Rfc8032TestPublicKey, keys[0].RawKey);
    }

    [Fact]
    public void SaveCachedJwks_RejectsDocumentWithoutUsableKey()
    {
        Assert.False(SigningKeyStore.SaveCachedJwks("{\"keys\":[]}"));
        Assert.False(SigningKeyStore.SaveCachedJwks("não é json"));
        Assert.False(SigningKeyStore.SaveCachedJwks(null));
        Assert.Null(SigningKeyStore.LoadCachedJwks());
    }

    private static string CreateToken()
    {
        var ents = new List<EntitlementItem>
        {
            new EntitlementItem { Slug = "revit-automator", Status = "active" }
        };

        return TestHelpers.CreateMasterLeaseJwt(
            "usr_1",
            new string('m', 64),
            DateTimeOffset.UtcNow.AddDays(10),
            ents);
    }

    private static byte[] SHA256Of(string input)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(input));
    }

    private static string ToStandardBase64(string base64Url)
    {
        string base64 = base64Url.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }

        return base64;
    }

    private static string ToBase64Url(string input) => ToBase64Url(Encoding.UTF8.GetBytes(input));

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
