using System;
using NodeAec.Connector.Auth;
using Xunit;

namespace NodeAec.Connector.Tests;

public class DesktopAuthServiceTests
{
    [Fact]
    public void GenerateSecureState_ReturnsBase64Url32Bytes()
    {
        string state = DesktopAuthService.GenerateSecureState();

        Assert.NotNull(state);
        Assert.True(state.Length >= 40); // 32 bytes in base64 without padding ~ 43 chars
        Assert.DoesNotContain("+", state);
        Assert.DoesNotContain("/", state);
        Assert.DoesNotContain("=", state);
    }

    [Fact]
    public void GenerateSecureState_ProducesUniqueTokens()
    {
        string state1 = DesktopAuthService.GenerateSecureState();
        string state2 = DesktopAuthService.GenerateSecureState();

        Assert.NotEqual(state1, state2);
    }

    [Fact]
    public void GetAvailableLoopbackPort_ReturnsValidPort()
    {
        int port = DesktopAuthService.GetAvailableLoopbackPort();

        Assert.InRange(port, 1024, 65535);
    }

    [Fact]
    public void BuildAuthUrl_IncludesPortAndState()
    {
        var authService = new DesktopAuthService("https://nodeaec.com.br/auth/desktop");
        string url = authService.BuildAuthUrl(54321, "abc_123");

        Assert.Equal("https://nodeaec.com.br/auth/desktop?port=54321&state=abc_123", url);
    }

    [Fact]
    public void BuildAuthUrl_PreservesExistingQueryParameters()
    {
        var authService = new DesktopAuthService("https://nodeaec.com.br/auth/desktop?env=dev");
        string url = authService.BuildAuthUrl(54321, "abc_123");

        Assert.Equal("https://nodeaec.com.br/auth/desktop?env=dev&port=54321&state=abc_123", url);
    }
}
