using System;
using System.Collections.Generic;
using System.IO;
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
}
