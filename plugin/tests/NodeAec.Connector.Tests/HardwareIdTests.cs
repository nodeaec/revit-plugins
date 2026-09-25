using System;
using NodeAec.Connector.Hardware;
using Xunit;

namespace NodeAec.Connector.Tests;

public class HardwareIdTests
{
    [Fact]
    public void GetMachineId_ReturnsNonEmptyHex64String()
    {
        string id = HardwareId.GetMachineId();

        Assert.NotNull(id);
        Assert.NotEmpty(id);
        Assert.Equal(64, id.Length); // SHA-256 in hex
    }

    [Fact]
    public void GetMachineId_IsConsistentAcrossMultipleCalls()
    {
        string id1 = HardwareId.GetMachineId();
        string id2 = HardwareId.GetMachineId();

        Assert.Equal(id1, id2);
    }
}
