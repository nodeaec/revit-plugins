using System.Text.RegularExpressions;
using NodeAec.Licensing.Sample.Gate;
using Xunit;

namespace NodeAec.Licensing.Tests;

public class HardwareIdTests
{
    [Fact]
    public void GetMachineId_ReturnsDeterministic64HexChars()
    {
        string id1 = HardwareId.GetMachineId();
        string id2 = HardwareId.GetMachineId();

        Assert.NotNull(id1);
        Assert.Equal(64, id1.Length);
        Assert.Matches("^[a-f0-9]{64}$", id1);
        Assert.Equal(id1, id2);
    }
}
