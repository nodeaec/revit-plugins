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
        // 32 bytes em base64url sem padding são sempre exatamente 43 caracteres.
        Assert.Equal(43, state.Length);
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

    [Theory]
    [InlineData("same-token", "same-token", true)]   // state idêntico → aceito
    [InlineData("same-token", "other-token", false)] // conteúdo diferente → rejeitado
    [InlineData("abc", "abcd", false)]               // prefixo comum não engana (sem saída antecipada)
    [InlineData("", "", true)]
    [InlineData(null, null, true)]
    [InlineData(null, "abc", false)]
    [InlineData("abc", null, false)]
    public void FixedTimeEquals_ComparesExactlyWithoutEarlyExit(string? left, string? right, bool expected)
    {
        Assert.Equal(expected, DesktopAuthService.FixedTimeEquals(left, right));
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

    [Theory]
    [InlineData("/callback", true)]        // formato real emitido pelo portal web
    [InlineData("/callback/", true)]       // variação com barra final
    [InlineData("/CALLBACK", true)]
    [InlineData("/favicon.ico", false)]    // requisições que o listener deve ignorar
    [InlineData("/callback/extra", false)]
    [InlineData("/", false)]
    [InlineData(null, false)]
    public void IsCallbackPath_MatchesOnlyAuthenticationCallback(string? path, bool expected)
    {
        Assert.Equal(expected, DesktopAuthService.IsCallbackPath(path));
    }
}
