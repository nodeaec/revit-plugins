using Xunit;

namespace NodeAec.Licensing.Tests;

public class MachineIdTests
{
    [Fact]
    public void GetMachineId_IsStableSha256Hex()
    {
        using var client = TestFactory.CreateClient();

        var a = client.GetMachineId();
        var b = client.GetMachineId();

        Assert.Equal(a, b);
        Assert.Matches("^[0-9a-f]{64}$", a);
    }

    [Fact]
    public void GetMachineId_IsStableAcrossInstances()
    {
        using var c1 = TestFactory.CreateClient();
        using var c2 = TestFactory.CreateClient();

        Assert.Equal(c1.GetMachineId(), c2.GetMachineId());
    }
}
