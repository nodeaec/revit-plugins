using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NodeAec.Licensing.Sample.Gate;
using Xunit;

namespace NodeAec.Licensing.Tests;

public class NodeAecGateTests : IDisposable
{
    private readonly string _tempDir;

    public NodeAecGateTests()
    {
        _tempDir = TestHelpers.CreateTempDir();
        NodeAecGate.SetCustomBasePath(_tempDir);
    }

    public void Dispose()
    {
        NodeAecGate.SetCustomBasePath(null);
        TestHelpers.DeleteTempDir(_tempDir);
    }

    private void WriteEncryptedLease(string jwt)
    {
        string filePath = Path.Combine(_tempDir, "entitlements.lease");
        byte[] rawBytes = Encoding.UTF8.GetBytes(jwt);
        byte[] bytesToWrite;
        try
        {
            bytesToWrite = ProtectedData.Protect(rawBytes, null, DataProtectionScope.CurrentUser);
        }
        catch
        {
            bytesToWrite = rawBytes;
        }
        File.WriteAllBytes(filePath, bytesToWrite);
    }

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
        var result = NodeAecGate.Validate("revit-automator");
        Assert.False(result.IsLicensed);
        Assert.Contains("Nenhuma credencial", result.Message);
        Assert.Contains("Connector", result.Message);
    }

    [Fact]
    public void Validate_WhenLeaseFileEmpty_ReturnsFailure()
    {
        string filePath = Path.Combine(_tempDir, "entitlements.lease");
        File.WriteAllBytes(filePath, Array.Empty<byte>());

        var result = NodeAecGate.Validate("revit-automator");
        Assert.False(result.IsLicensed);
        Assert.Contains("vazio ou corrompido", result.Message);
    }

    [Fact]
    public void Validate_WhenLeaseMalformed_ReturnsFailure()
    {
        WriteEncryptedLease("invalid.jwt.token");

        var result = NodeAecGate.Validate("revit-automator");
        Assert.False(result.IsLicensed);
        Assert.Contains("Estrutura da concessão inválida", result.Message);
    }

    [Fact]
    public void Validate_WhenMachineIdMismatched_ReturnsFailure()
    {
        string wrongMachineId = "0000000000000000000000000000000000000000000000000000000000000000";
        long exp = DateTimeOffset.UtcNow.AddDays(15).ToUnixTimeSeconds();
        var entitlements = new[]
        {
            new { slug = "revit-automator", name = "Revit Automator", type = "perpetual", status = "active", granted = true }
        };

        string jwt = TestHelpers.CreateTestJwt(wrongMachineId, exp, entitlements);
        WriteEncryptedLease(jwt);

        var result = NodeAecGate.Validate("revit-automator");
        Assert.False(result.IsLicensed);
        Assert.Contains("Hardware ID divergente", result.Message);
    }

    [Fact]
    public void Validate_WhenLeaseExpired_ReturnsFailure()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
        var entitlements = new[]
        {
            new { slug = "revit-automator", name = "Revit Automator", type = "perpetual", status = "active", granted = true }
        };

        string jwt = TestHelpers.CreateTestJwt(machineId, exp, entitlements);
        WriteEncryptedLease(jwt);

        var result = NodeAecGate.Validate("revit-automator");
        Assert.False(result.IsLicensed);
        Assert.Contains("expirou", result.Message);
    }

    [Fact]
    public void Validate_WhenProductNotInEntitlements_ReturnsFailure()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(20).ToUnixTimeSeconds();
        var entitlements = new[]
        {
            new { slug = "other-tool", name = "Other Tool", type = "perpetual", status = "active", granted = true }
        };

        string jwt = TestHelpers.CreateTestJwt(machineId, exp, entitlements);
        WriteEncryptedLease(jwt);

        var result = NodeAecGate.Validate("revit-automator");
        Assert.False(result.IsLicensed);
        Assert.Contains("não consta nas licenças ativas", result.Message);
    }

    [Fact]
    public void Validate_WhenProductSeatLimitReached_ReturnsFailure()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(20).ToUnixTimeSeconds();
        var entitlements = new[]
        {
            new { slug = "revit-automator", name = "Revit Automator", type = "subscription", status = "seat_limit_reached", granted = false }
        };

        string jwt = TestHelpers.CreateTestJwt(machineId, exp, entitlements);
        WriteEncryptedLease(jwt);

        var result = NodeAecGate.Validate("revit-automator");
        Assert.False(result.IsLicensed);
        Assert.Contains("limite de computadores simultâneos", result.Message);
    }

    [Fact]
    public void Validate_WhenProductExpired_ReturnsFailure()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(20).ToUnixTimeSeconds();
        long productExp = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds();
        var entitlements = new[]
        {
            new { slug = "revit-automator", name = "Revit Automator", type = "trial", status = "active", expiresAt = productExp, granted = false }
        };

        string jwt = TestHelpers.CreateTestJwt(machineId, exp, entitlements);
        WriteEncryptedLease(jwt);

        var result = NodeAecGate.Validate("revit-automator");
        Assert.False(result.IsLicensed);
        Assert.Contains("expirou", result.Message);
    }

    [Fact]
    public void Validate_WhenProductActiveAndValid_ReturnsSuccess()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(25).ToUnixTimeSeconds();
        var entitlements = new[]
        {
            new
            {
                slug = "revit-automator",
                name = "Revit Automator Pro",
                licenseKey = "NAEC-A2C4-E6G8-H2K4",
                type = "perpetual",
                status = "active",
                granted = true
            }
        };

        string jwt = TestHelpers.CreateTestJwt(machineId, exp, entitlements);
        WriteEncryptedLease(jwt);

        var result = NodeAecGate.Validate("revit-automator");
        Assert.True(result.IsLicensed);
        Assert.Equal("perpetual", result.LicenseType);
        Assert.Equal("Revit Automator Pro", result.ProductName);
        Assert.Equal("NAEC-A2C4-E6G8-H2K4", result.LicenseKey);
    }

    [Fact]
    public void Validate_IsCaseInsensitiveForProductSlug()
    {
        string machineId = HardwareId.GetMachineId();
        long exp = DateTimeOffset.UtcNow.AddDays(25).ToUnixTimeSeconds();
        var entitlements = new[]
        {
            new
            {
                slug = "revit-automator",
                name = "Revit Automator",
                licenseKey = "NAEC-KEY-1234",
                type = "subscription",
                status = "active",
                granted = true
            }
        };

        string jwt = TestHelpers.CreateTestJwt(machineId, exp, entitlements);
        WriteEncryptedLease(jwt);

        var resultUpper = NodeAecGate.Validate("REVIT-AUTOMATOR");
        var resultMixed = NodeAecGate.Validate("Revit-Automator");

        Assert.True(resultUpper.IsLicensed);
        Assert.True(resultMixed.IsLicensed);
    }
}
