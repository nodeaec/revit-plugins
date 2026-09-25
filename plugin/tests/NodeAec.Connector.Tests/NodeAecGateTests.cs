using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using NodeAec.Connector.Gate;
using NodeAec.Connector.Hardware;
using NodeAec.Connector.Models;
using NodeAec.Connector.Storage;
using Xunit;

namespace NodeAec.Connector.Tests;

public class NodeAecGateTests : IDisposable
{
    private readonly string _tempDir;

    public NodeAecGateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NodeAecGateTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        LeaseStorage.SetCustomBasePath(_tempDir);
        // JWKS da chave de teste: sem ele o gate rejeita a assinatura (modo fechado).
        TestHelpers.InstallTestSigningKey();
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
    public void Validate_WithoutLease_ReturnsUnlicensedWithConnectorPrompt()
    {
        var result = NodeAecGate.Validate("revit-automator");

        Assert.False(result.IsLicensed);
        Assert.Contains("Connector", result.Message);
    }

    [Fact]
    public void Validate_WithEmptySlug_ReturnsFailure()
    {
        var result = NodeAecGate.Validate("");

        Assert.False(result.IsLicensed);
    }

    [Fact]
    public void Validate_WithWrongMachineId_FailsHardwareBinding()
    {
        string wrongMid = new string('a', 64);
        var exp = DateTimeOffset.UtcNow.AddDays(20);
        var ents = new List<EntitlementItem>
        {
            new EntitlementItem { Slug = "revit-automator", Status = "active" }
        };

        string jwt = TestHelpers.CreateMasterLeaseJwt("usr_1", wrongMid, exp, ents);
        LeaseStorage.SaveMasterLease(jwt);

        var result = NodeAecGate.Validate("revit-automator");

        Assert.False(result.IsLicensed);
        Assert.Contains("Hardware ID divergente", result.Message);
    }

    [Fact]
    public void Validate_WithExpiredLease_FailsOfflineGracePeriod()
    {
        string currentMid = HardwareId.GetMachineId();
        var expiredDate = DateTimeOffset.UtcNow.AddDays(-1);
        var ents = new List<EntitlementItem>
        {
            new EntitlementItem { Slug = "revit-automator", Status = "active" }
        };

        string jwt = TestHelpers.CreateMasterLeaseJwt("usr_1", currentMid, expiredDate, ents);
        LeaseStorage.SaveMasterLease(jwt);

        var result = NodeAecGate.Validate("revit-automator");

        Assert.False(result.IsLicensed);
        Assert.Contains("expirou", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ProductNotInEntitlements_ReturnsUnlicensed()
    {
        string currentMid = HardwareId.GetMachineId();
        var exp = DateTimeOffset.UtcNow.AddDays(25);
        var ents = new List<EntitlementItem>
        {
            new EntitlementItem { Slug = "parametric-curtain-wall", Status = "active" }
        };

        string jwt = TestHelpers.CreateMasterLeaseJwt("usr_1", currentMid, exp, ents);
        LeaseStorage.SaveMasterLease(jwt);

        var result = NodeAecGate.Validate("revit-automator");

        Assert.False(result.IsLicensed);
        Assert.Contains("não consta", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ValidActiveProduct_SucceedsInstantly()
    {
        string currentMid = HardwareId.GetMachineId();
        var exp = DateTimeOffset.UtcNow.AddDays(28);
        var ents = new List<EntitlementItem>
        {
            new EntitlementItem
            {
                Slug = "revit-automator",
                Name = "Revit Automator PRO",
                LicenseKey = "NAEC-A1B2-C3D4-E5F6",
                Type = "perpetual",
                Status = "active"
            }
        };

        string jwt = TestHelpers.CreateMasterLeaseJwt("usr_1", currentMid, exp, ents);
        LeaseStorage.SaveMasterLease(jwt);

        var result = NodeAecGate.Validate("revit-automator");

        Assert.True(result.IsLicensed);
        Assert.Equal("perpetual", result.LicenseType);
        Assert.Equal("NAEC-A1B2-C3D4-E5F6", result.LicenseKey);
        Assert.Equal("Revit Automator PRO", result.ProductName);
    }

    [Fact]
    public void Validate_TamperedPayload_IsRejectedBySignatureCheck()
    {
        string currentMid = HardwareId.GetMachineId();
        var exp = DateTimeOffset.UtcNow.AddDays(20);
        var ents = new List<EntitlementItem>
        {
            new EntitlementItem { Slug = "revit-automator", Status = "active" }
        };

        // Lease emitido para OUTRO hardware; o "forjador" reescreve o `mid` para esta
        // máquina mantendo a assinatura original — deve morrer na checagem criptográfica.
        string jwt = TestHelpers.CreateMasterLeaseJwt("usr_1", new string('a', 64), exp, ents);
        string forged = ReplacePayloadClaim(jwt, "mid", currentMid);
        LeaseStorage.SaveMasterLease(forged);

        var result = NodeAecGate.Validate("revit-automator");

        Assert.False(result.IsLicensed);
        Assert.Contains("verificação de segurança", result.Message);
    }

    [Fact]
    public void Validate_WithoutCachedJwks_FailsClosed()
    {
        string currentMid = HardwareId.GetMachineId();
        var exp = DateTimeOffset.UtcNow.AddDays(20);
        var ents = new List<EntitlementItem>
        {
            new EntitlementItem { Slug = "revit-automator", Status = "active" }
        };

        string jwt = TestHelpers.CreateMasterLeaseJwt("usr_1", currentMid, exp, ents);
        LeaseStorage.SaveMasterLease(jwt);
        File.Delete(SigningKeyStore.GetJwksFilePath());

        var result = NodeAecGate.Validate("revit-automator");

        Assert.False(result.IsLicensed);
        Assert.Contains("verificação de segurança", result.Message);
    }

    [Fact]
    public void Validate_NonMasterScope_IsRejected()
    {
        string currentMid = HardwareId.GetMachineId();
        var exp = DateTimeOffset.UtcNow.AddDays(20);
        var ents = new List<EntitlementItem>
        {
            new EntitlementItem { Slug = "revit-automator", Status = "active" }
        };

        string jwt = TestHelpers.CreateMasterLeaseJwt("usr_1", currentMid, exp, ents, scope: "license");
        LeaseStorage.SaveMasterLease(jwt);

        var result = NodeAecGate.Validate("revit-automator");

        Assert.False(result.IsLicensed);
        Assert.Contains("formato não suportado", result.Message);
    }

    [Fact]
    public void Validate_IssuedAtInFuture_IsRejected()
    {
        string currentMid = HardwareId.GetMachineId();
        var exp = DateTimeOffset.UtcNow.AddDays(20);
        var ents = new List<EntitlementItem>
        {
            new EntitlementItem { Slug = "revit-automator", Status = "active" }
        };

        long futureIat = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds();
        string jwt = TestHelpers.CreateMasterLeaseJwt("usr_1", currentMid, exp, ents, iatOverride: futureIat);
        LeaseStorage.SaveMasterLease(jwt);

        var result = NodeAecGate.Validate("revit-automator");

        Assert.False(result.IsLicensed);
        Assert.Contains("data", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("\"node-aec-plugin\"", true)]
    [InlineData("[\"node-aec-desktop\",\"node-aec-plugin\"]", true)]
    [InlineData("[\"node-aec-desktop\"]", true)]
    [InlineData("[\"outro-produto\"]", false)]
    [InlineData("[]", false)]
    [InlineData("\"node-aec-api\"", false)]
    [InlineData("null", false)]
    public void HasPlatformAudience_AcceptsOnlyPlatformValues(string audJson, bool expected)
    {
        JsonElement aud = JsonSerializer.Deserialize<JsonElement>(audJson);

        Assert.Equal(expected, NodeAecGate.HasPlatformAudience(aud));
    }

    [Fact]
    public void HasPlatformAudience_MissingClaim_Denies()
    {
        Assert.False(NodeAecGate.HasPlatformAudience(null));
    }

    /// <summary>
    /// Reescreve um claim do payload mantendo o header e a assinatura originais
    /// (simula adulteração de arquivo: a assinatura deixa de conferir).
    /// </summary>
    private static string ReplacePayloadClaim(string jwt, string claim, string value)
    {
        string[] parts = jwt.Split('.');
        string payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(ToStandardBase64(parts[1])));

        var claims = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson)
            ?? throw new InvalidOperationException("Payload de teste ilegível.");
        claims[claim] = JsonSerializer.SerializeToElement(value);

        return $"{parts[0]}.{ToBase64Url(JsonSerializer.Serialize(claims))}.{parts[2]}";
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

    private static string ToBase64Url(string input)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(input))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
