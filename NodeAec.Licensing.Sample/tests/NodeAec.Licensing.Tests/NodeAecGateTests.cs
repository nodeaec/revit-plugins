using System;
using System.IO;
using System.Text;
using NodeAec.Licensing.Sample.Gate;
using Xunit;

namespace NodeAec.Licensing.Tests;

/// <summary>
/// Testes do Micro-SDK.
///
/// Divididos em dois blocos:
/// 1) <c>Validate</c> — caminho de armazenamento: leitura do arquivo de lease e recusa em
///    modo fechado de tudo que não vier protegido pelo DPAPI do usuário;
/// 2) <c>ValidateLeaseToken</c> — política de aceitação (assinatura Ed25519, contrato do
///    token, hardware, prazo e produto) sobre um lease já decifrado.
///
/// O bloco 1 exercita apenas recusas que não exigem o DPAPI, de modo que a suíte roda em
/// qualquer sessão; o round-trip real de proteção/desproteção é coberto por
/// <c>LeaseStorageTests</c> no Node.aec Connector.
/// </summary>
public class NodeAecGateTests : IDisposable
{
    private const string Slug = "revit-automator";

    private readonly string _tempDir;

    public NodeAecGateTests()
    {
        _tempDir = TestHelpers.CreateTempDir();
        NodeAecGate.SetCustomBasePath(_tempDir);
        TestHelpers.InstallSigningKey(_tempDir);
    }

    public void Dispose()
    {
        NodeAecGate.SetCustomBasePath(null);
        TestHelpers.DeleteTempDir(_tempDir);
    }

    private string LeasePath => Path.Combine(_tempDir, "entitlements.lease");

    // ---------------------------------------------------------------- bloqueio de disco

    [Fact]
    public void Validate_WhenSlugEmpty_ReturnsFailure()
    {
        var result = NodeAecGate.Validate("");
        Assert.False(result.IsLicensed);
        Assert.Contains("não informado", result.Message);
    }

    [Fact]
    public void Validate_WhenLeaseFileMissing_ReturnsFailure()
    {
        var result = NodeAecGate.Validate(Slug);
        Assert.False(result.IsLicensed);
        Assert.Contains("Nenhuma credencial", result.Message);
        Assert.Contains("Connector", result.Message);
    }

    [Fact]
    public void Validate_WhenLeaseFileEmpty_ReturnsFailure()
    {
        File.WriteAllBytes(LeasePath, Array.Empty<byte>());

        var result = NodeAecGate.Validate(Slug);
        Assert.False(result.IsLicensed);
        Assert.Contains("vazio ou corrompido", result.Message);
    }

    /// <summary>
    /// Regressão do fallback removido: um lease legível em texto puro no disco nunca é
    /// aceito, mesmo que a assinatura e os claims estejam corretos.
    /// </summary>
    [Fact]
    public void Validate_WhenLeaseIsPlaintext_ReturnsFailure()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(15).ToUnixTimeSeconds();
        var entitlements = new[]
        {
            new { slug = Slug, name = "Revit Automator", type = "perpetual", status = "active", granted = true }
        };

        string jwt = TestHelpers.CreateTestJwt(machineId, exp, entitlements);
        File.WriteAllBytes(LeasePath, Encoding.UTF8.GetBytes(jwt));

        var result = NodeAecGate.Validate(Slug);
        Assert.False(result.IsLicensed);
        Assert.Contains("com segurança", result.Message);
    }

    // ---------------------------------------------------------------- política do lease

    [Fact]
    public void ValidateLeaseToken_WhenTokenMalformed_ReturnsFailure()
    {
        var result = NodeAecGate.ValidateLeaseToken("invalid.jwt.token", Slug);
        Assert.False(result.IsLicensed);
        Assert.Contains("verificação de segurança", result.Message);
    }

    [Fact]
    public void ValidateLeaseToken_WhenJwksMissing_ReturnsFailure()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(15).ToUnixTimeSeconds();
        string jwt = TestHelpers.CreateTestJwt(machineId, exp, Array.Empty<object>());

        File.Delete(Path.Combine(_tempDir, LeaseSignatureVerifier.JwksFileName));

        var result = NodeAecGate.ValidateLeaseToken(jwt, Slug);
        Assert.False(result.IsLicensed);
        Assert.Contains("verificação de segurança", result.Message);
    }

    [Fact]
    public void ValidateLeaseToken_WhenSignatureForged_ReturnsFailure()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(15).ToUnixTimeSeconds();
        string jwt = TestHelpers.CreateTestJwt(machineId, exp, Array.Empty<object>(), sign: false);

        var result = NodeAecGate.ValidateLeaseToken(jwt, Slug);
        Assert.False(result.IsLicensed);
        Assert.Contains("verificação de segurança", result.Message);
    }

    [Fact]
    public void ValidateLeaseToken_WhenIssuerWrong_ReturnsFailure()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(15).ToUnixTimeSeconds();
        string jwt = TestHelpers.CreateTestJwt(machineId, exp, Array.Empty<object>(), iss: "forged-issuer");

        var result = NodeAecGate.ValidateLeaseToken(jwt, Slug);
        Assert.False(result.IsLicensed);
        Assert.Contains("Origem da licença local desconhecida", result.Message);
    }

    [Fact]
    public void ValidateLeaseToken_WhenScopeWrong_ReturnsFailure()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(15).ToUnixTimeSeconds();
        string jwt = TestHelpers.CreateTestJwt(machineId, exp, Array.Empty<object>(), scope: "trial");

        var result = NodeAecGate.ValidateLeaseToken(jwt, Slug);
        Assert.False(result.IsLicensed);
        Assert.Contains("formato não suportado", result.Message);
    }

    [Fact]
    public void ValidateLeaseToken_WhenIssuedInFuture_ReturnsFailure()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(15).ToUnixTimeSeconds();
        long futureIat = DateTimeOffset.UtcNow.AddHours(6).ToUnixTimeSeconds();
        string jwt = TestHelpers.CreateTestJwt(machineId, exp, Array.Empty<object>(), iat: futureIat);

        var result = NodeAecGate.ValidateLeaseToken(jwt, Slug);
        Assert.False(result.IsLicensed);
        Assert.Contains("A data da licença local é inválida", result.Message);
    }

    [Fact]
    public void ValidateLeaseToken_WhenMachineIdMismatched_ReturnsFailure()
    {
        string wrongMachineId = "0000000000000000000000000000000000000000000000000000000000000000";
        long exp = DateTimeOffset.UtcNow.AddDays(15).ToUnixTimeSeconds();
        var entitlements = new[]
        {
            new { slug = Slug, name = "Revit Automator", type = "perpetual", status = "active", granted = true }
        };

        string jwt = TestHelpers.CreateTestJwt(wrongMachineId, exp, entitlements);

        var result = NodeAecGate.ValidateLeaseToken(jwt, Slug);
        Assert.False(result.IsLicensed);
        Assert.Contains("Hardware ID divergente", result.Message);
    }

    [Fact]
    public void ValidateLeaseToken_WhenLeaseExpired_ReturnsFailure()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
        var entitlements = new[]
        {
            new { slug = Slug, name = "Revit Automator", type = "perpetual", status = "active", granted = true }
        };

        string jwt = TestHelpers.CreateTestJwt(machineId, exp, entitlements);

        var result = NodeAecGate.ValidateLeaseToken(jwt, Slug);
        Assert.False(result.IsLicensed);
        Assert.Contains("expirou", result.Message);
    }

    [Fact]
    public void ValidateLeaseToken_WhenProductNotInEntitlements_ReturnsFailure()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(20).ToUnixTimeSeconds();
        var entitlements = new[]
        {
            new { slug = "other-tool", name = "Other Tool", type = "perpetual", status = "active", granted = true }
        };

        string jwt = TestHelpers.CreateTestJwt(machineId, exp, entitlements);

        var result = NodeAecGate.ValidateLeaseToken(jwt, Slug);
        Assert.False(result.IsLicensed);
        Assert.Contains("não consta nas licenças ativas", result.Message);
    }

    [Fact]
    public void ValidateLeaseToken_WhenProductSeatLimitReached_ReturnsFailure()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(20).ToUnixTimeSeconds();
        var entitlements = new[]
        {
            new { slug = Slug, name = "Revit Automator", type = "subscription", status = "seat_limit_reached", granted = false }
        };

        string jwt = TestHelpers.CreateTestJwt(machineId, exp, entitlements);

        var result = NodeAecGate.ValidateLeaseToken(jwt, Slug);
        Assert.False(result.IsLicensed);
        Assert.Contains("limite de computadores simultâneos", result.Message);
    }

    [Fact]
    public void ValidateLeaseToken_WhenProductExpired_ReturnsFailure()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(20).ToUnixTimeSeconds();
        long productExp = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds();
        var entitlements = new[]
        {
            new { slug = Slug, name = "Revit Automator", type = "trial", status = "active", expiresAt = productExp, granted = false }
        };

        string jwt = TestHelpers.CreateTestJwt(machineId, exp, entitlements);

        var result = NodeAecGate.ValidateLeaseToken(jwt, Slug);
        Assert.False(result.IsLicensed);
        Assert.Contains("expirou", result.Message);
    }

    [Fact]
    public void ValidateLeaseToken_WhenProductActiveAndValid_ReturnsSuccess()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(25).ToUnixTimeSeconds();
        var entitlements = new[]
        {
            new
            {
                slug = Slug,
                name = "Revit Automator Pro",
                licenseKey = "NAEC-A2C4-E6G8-H2K4",
                type = "perpetual",
                status = "active",
                granted = true
            }
        };

        string jwt = TestHelpers.CreateTestJwt(machineId, exp, entitlements);

        var result = NodeAecGate.ValidateLeaseToken(jwt, Slug);
        Assert.True(result.IsLicensed);
        Assert.Equal("perpetual", result.LicenseType);
        Assert.Equal("Revit Automator Pro", result.ProductName);
        Assert.Equal("NAEC-A2C4-E6G8-H2K4", result.LicenseKey);
    }

    [Fact]
    public void ValidateLeaseToken_IsCaseInsensitiveForProductSlug()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(25).ToUnixTimeSeconds();
        var entitlements = new[]
        {
            new
            {
                slug = Slug,
                name = "Revit Automator",
                licenseKey = "NAEC-KEY-1234",
                type = "subscription",
                status = "active",
                granted = true
            }
        };

        string jwt = TestHelpers.CreateTestJwt(machineId, exp, entitlements);

        var resultUpper = NodeAecGate.ValidateLeaseToken(jwt, "REVIT-AUTOMATOR");
        var resultMixed = NodeAecGate.ValidateLeaseToken(jwt, "Revit-Automator");

        Assert.True(resultUpper.IsLicensed);
        Assert.True(resultMixed.IsLicensed);
    }
}
