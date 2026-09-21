using System;
using System.IO;
using NodeAec.Connector.Auth;
using NodeAec.Connector.Storage;
using Xunit;

namespace NodeAec.Connector.Tests;

public class LoginRequirementTests : IDisposable
{
    private readonly string _tempDir;

    public LoginRequirementTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NodeAecTests_" + Guid.NewGuid().ToString("N"));
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
    public void IsLoggedIn_WithoutSession_ReturnsFalse()
    {
        Assert.False(LoginRequirement.IsLoggedIn());
    }

    [Fact]
    public void IsLoggedIn_WithSavedSession_ReturnsTrue()
    {
        LeaseStorage.SaveSession("arquiteta@escritorio.com.br", "user-token");

        Assert.True(LoginRequirement.IsLoggedIn());
    }

    [Fact]
    public void IsLoggedIn_AfterLogout_ReturnsFalse()
    {
        LeaseStorage.SaveSession("arquiteta@escritorio.com.br", "user-token");
        LeaseStorage.ClearSession();

        Assert.False(LoginRequirement.IsLoggedIn());
    }
}
