using System;
using NodeAec.Connector.Config;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// Regras de endpoint configurável (M2): <c>https://</c> sempre; <c>http://</c> somente com o
/// opt-in explícito <c>NODEAEC_ALLOW_INSECURE_DEV=1</c>; resto rejeitado com mensagem clara.
/// </summary>
public class ConnectorConfigTests
{
    private const string Variable = "NODEAEC_API_URL";
    private const string Fallback = "https://api.nodeaec.com.br";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateEndpointUrl_MissingValue_ReturnsSecureFallback(string? raw)
    {
        string result = ConnectorConfig.ValidateEndpointUrl(Variable, raw, allowInsecure: false, Fallback);

        Assert.Equal(Fallback, result);
    }

    [Fact]
    public void ValidateEndpointUrl_Https_IsAcceptedTrimmed()
    {
        string result = ConnectorConfig.ValidateEndpointUrl(
            Variable, "  https://api.example.test  ", allowInsecure: false, Fallback);

        Assert.Equal("https://api.example.test", result);
    }

    [Theory]
    [InlineData("http://localhost:9000")]
    [InlineData("http://api.example.test")]
    public void ValidateEndpointUrl_HttpWithoutOptIn_ThrowsWithClearMessage(string raw)
    {
        // Sem o opt-in, um http:// ambiente NUNCA pode rebaixar o transporte em silêncio.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConnectorConfig.ValidateEndpointUrl(Variable, raw, allowInsecure: false, Fallback));

        Assert.Contains(Variable, ex.Message);
        Assert.Contains("https://", ex.Message);
        Assert.Contains("NODEAEC_ALLOW_INSECURE_DEV", ex.Message);
    }

    [Fact]
    public void ValidateEndpointUrl_HttpWithOptIn_IsAccepted()
    {
        string result = ConnectorConfig.ValidateEndpointUrl(
            Variable, "http://localhost:9000", allowInsecure: true, Fallback);

        Assert.Equal("http://localhost:9000", result);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://api.example.test")]
    [InlineData("javascript:alert(1)")]
    public void ValidateEndpointUrl_MalformedOrForeignScheme_ThrowsEvenWithOptIn(string raw)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConnectorConfig.ValidateEndpointUrl(Variable, raw, allowInsecure: true, Fallback));

        Assert.Contains("NODEAEC_ALLOW_INSECURE_DEV", ex.Message);
    }

    [Fact]
    public void ValidateEndpointUrl_HttpsWithOptInDisabled_IsStillAccepted()
    {
        // O opt-in só relaxa http; https continua obrigatório-preferido e aceito.
        string result = ConnectorConfig.ValidateEndpointUrl(
            Variable, "https://api.example.test", allowInsecure: false, Fallback);

        Assert.Equal("https://api.example.test", result);
    }
}
