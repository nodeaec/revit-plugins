using NodeAec.Licensing.Sample.Config;
using Xunit;

namespace NodeAec.Licensing.Tests;

/// <summary>
/// Serializes all env-var tests: env vars are process-global.
/// </summary>
[CollectionDefinition("EnvVars", DisableParallelization = true)]
public class EnvVarCollection
{
}

[Collection("EnvVars")]
public class LicenseConfigTests : IDisposable
{
    private static readonly string[] EnvKeys =
    {
        "NODE_AEC_PRODUCT_SLUG",
        "NODE_AEC_PUBLIC_KEY_PEM",
        "NODE_AEC_LICENSE_KEY"
    };

    private readonly Dictionary<string, string?> _saved = new();

    public LicenseConfigTests()
    {
        foreach (var k in EnvKeys)
            _saved[k] = Environment.GetEnvironmentVariable(k);
    }

    public void Dispose()
    {
        foreach (var kv in _saved)
            Environment.SetEnvironmentVariable(kv.Key, kv.Value);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ApiUrl_IsProductionEndpoint()
    {
        Assert.Equal("https://api.nodeaec.com.br", LicenseConfig.ApiUrl);
    }

    [Fact]
    public void IntegrationRepoUrl_PointsToGithub()
    {
        Assert.Contains("github.com/nodeaec", LicenseConfig.IntegrationRepoUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultPublicKeyPem_IsSpkiPem()
    {
        Assert.StartsWith("-----BEGIN PUBLIC KEY-----", LicenseConfig.DefaultPublicKeyPem, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductSlug_Default_IsNodeaec()
    {
        Environment.SetEnvironmentVariable("NODE_AEC_PRODUCT_SLUG", null);

        Assert.Equal("nodeaec", LicenseConfig.ProductSlug);
    }

    [Fact]
    public void ProductSlug_EnvOverride_IsUsed()
    {
        Environment.SetEnvironmentVariable("NODE_AEC_PRODUCT_SLUG", "my-plugin");

        Assert.Equal("my-plugin", LicenseConfig.ProductSlug);
    }

    [Fact]
    public void PublicKeyPem_Default_FallsBackToEmbedded()
    {
        Environment.SetEnvironmentVariable("NODE_AEC_PUBLIC_KEY_PEM", null);

        Assert.Equal(LicenseConfig.DefaultPublicKeyPem, LicenseConfig.PublicKeyPem);
    }

    [Fact]
    public void PublicKeyPem_EnvOverride_IsUsed()
    {
        Environment.SetEnvironmentVariable("NODE_AEC_PUBLIC_KEY_PEM", "TEST-PEM");

        Assert.Equal("TEST-PEM", LicenseConfig.PublicKeyPem);
    }

    [Fact]
    public void CreateClient_DoesNotThrow()
    {
        Environment.SetEnvironmentVariable("NODE_AEC_PRODUCT_SLUG", null);
        Environment.SetEnvironmentVariable("NODE_AEC_PUBLIC_KEY_PEM", null);

        using var a = LicenseConfig.CreateClient();
        using var b = LicenseConfig.CreateClient("custom-slug");

        Assert.NotNull(a);
        Assert.NotNull(b);
    }

    [Fact]
    public void LoadStoredKey_EnvVar_TakesPrecedenceAndNormalizes()
    {
        // NOTE: the file fallback (%APPDATA%/NodeAec/license.key) is NOT tested:
        // it reads the developer's real machine state and is not hermetic.
        Environment.SetEnvironmentVariable("NODE_AEC_LICENSE_KEY", "  naec-abc-123 ");

        Assert.Equal("NAEC-ABC-123", LicenseConfig.LoadStoredKey());
    }
}
