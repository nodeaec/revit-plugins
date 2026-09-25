using System;
using System.IO;
using System.Text;
using NodeAec.Licensing.Sample.Gate;
using Xunit;

namespace NodeAec.Licensing.Tests;

/// <summary>
/// Testes da verificação criptográfica Ed25519 (RFC 8032) do lease local: seleção de
/// chave via JWKS/âncora fixa, rejeição de payload adulterado e decodificação SPKI.
/// </summary>
public class LeaseSignatureVerifierTests : IDisposable
{
    private const string Slug = "revit-automator";

    /// <summary>Prefixo DER de um SubjectPublicKeyInfo Ed25519 (RFC 8410) — mesmo do verificador.</summary>
    private static readonly byte[] SpkiEd25519Prefix =
    {
        0x30, 0x2a, 0x30, 0x05, 0x06, 0x03, 0x2b, 0x65, 0x70, 0x03, 0x21, 0x00,
    };

    private readonly string _tempDir;

    public LeaseSignatureVerifierTests()
    {
        _tempDir = TestHelpers.CreateTempDir();
        TestHelpers.InstallSigningKey(_tempDir);
    }

    public void Dispose()
    {
        TestHelpers.DeleteTempDir(_tempDir);
    }

    private string JwksPath => Path.Combine(_tempDir, LeaseSignatureVerifier.JwksFileName);

    private static string CreateSignedJwt()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(15).ToUnixTimeSeconds();
        var entitlements = new[]
        {
            new { slug = Slug, name = "Revit Automator", type = "perpetual", status = "active", granted = true }
        };

        return TestHelpers.CreateTestJwt(machineId, exp, entitlements);
    }

    private static byte[] Concat(byte[] left, byte[] right)
    {
        var result = new byte[left.Length + right.Length];
        Array.Copy(left, 0, result, 0, left.Length);
        Array.Copy(right, 0, result, left.Length, right.Length);
        return result;
    }

    [Fact]
    public void TryVerify_WhenSignedWithTestKey_ReturnsTrue()
    {
        bool ok = LeaseSignatureVerifier.TryVerify(CreateSignedJwt(), JwksPath, out string? reason);

        Assert.True(ok, reason);
        Assert.Null(reason);
    }

    [Fact]
    public void TryVerify_WhenPayloadTampered_ReturnsFailure()
    {
        string[] parts = CreateSignedJwt().Split('.');
        string forgedPayload = TestHelpers.Base64UrlEncode(
            Encoding.UTF8.GetBytes("{\"iss\":\"node-aec\",\"scope\":\"master-lease\",\"exp\":4102444800}"));
        string tampered = $"{parts[0]}.{forgedPayload}.{parts[2]}";

        bool ok = LeaseSignatureVerifier.TryVerify(tampered, JwksPath, out string? reason);

        Assert.False(ok);
        Assert.NotNull(reason);
    }

    [Fact]
    public void TryVerify_WhenKidUnknown_ReturnsFailure()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(15).ToUnixTimeSeconds();
        string jwt = TestHelpers.CreateTestJwt(machineId, exp, new object(), kid: "unknown-key");

        bool ok = LeaseSignatureVerifier.TryVerify(jwt, JwksPath, out string? reason);

        Assert.False(ok);
        Assert.Contains("indisponível", reason);
    }

    /// <summary>
    /// Sem cache do JWKS a verificação ainda acontece se a operação ancorar a chave em
    /// <c>NODEAEC_LICENSE_PUBLIC_KEY_SPKI</c> — caminho de chave fixa em vez de rotação.
    /// </summary>
    [Fact]
    public void TryVerify_WhenJwksAbsentButPinnedKeySet_VerifiesWithAnchor()
    {
        string spki = Convert.ToBase64String(Concat(SpkiEd25519Prefix, TestHelpers.TestRawPublicKey()));
        Environment.SetEnvironmentVariable(LeaseSignatureVerifier.PinnedKeyVariable, spki);

        try
        {
            File.Delete(JwksPath);

            bool ok = LeaseSignatureVerifier.TryVerify(CreateSignedJwt(), JwksPath, out string? reason);

            Assert.True(ok, reason);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LeaseSignatureVerifier.PinnedKeyVariable, null);
        }
    }

    [Fact]
    public void TryDecodeSpkiBase64_WhenEd25519_ReturnsRawKey()
    {
        byte[] raw = TestHelpers.TestRawPublicKey();
        string spki = Convert.ToBase64String(Concat(SpkiEd25519Prefix, raw));

        bool ok = LeaseSignatureVerifier.TryDecodeSpkiBase64(spki, out byte[] decoded, out string? reason);

        Assert.True(ok, reason);
        Assert.Equal(raw, decoded);
    }

    [Fact]
    public void TryDecodeSpkiBase64_WhenNotEd25519_ReturnsFailure()
    {
        byte[] raw = TestHelpers.TestRawPublicKey();
        var wrongPrefix = (byte[])SpkiEd25519Prefix.Clone();
        wrongPrefix[7] ^= 0xFF;
        string spki = Convert.ToBase64String(Concat(wrongPrefix, raw));

        bool ok = LeaseSignatureVerifier.TryDecodeSpkiBase64(spki, out _, out string? reason);

        Assert.False(ok);
        Assert.Contains("não é Ed25519", reason);
    }
}
