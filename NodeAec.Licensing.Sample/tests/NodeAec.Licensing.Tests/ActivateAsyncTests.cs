using System.Net;
using System.Text.Json;
using NodeAec.Licensing.Client;
using Xunit;

namespace NodeAec.Licensing.Tests;

public class ActivateAsyncTests : IDisposable
{
    public void Dispose()
    {
        TestFactory.CleanupTestCaches();
        GC.SuppressFinalize(this);
    }

    private static string SuccessJson(string leaseToken, string? name = "Prod", string? url = "https://nodeaec.com.br/products/x") =>
        JsonSerializer.Serialize(new { leaseToken, expiresAt = "2026-12-31T00:00:00Z", product = new { name, url } });

    private static HttpClient ActivateOnly(Func<HttpRequestMessage, HttpResponseMessage> responder, out FakeHttpMessageHandler handler) =>
        TestFactory.HttpWith(
            req => req.RequestUri!.AbsolutePath.EndsWith("/license/activate", StringComparison.Ordinal)
                ? responder(req)
                : throw new InvalidOperationException($"unexpected request: {req.RequestUri}"),
            out handler);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyKey_ReturnsKeyNotFound_WithoutHttpCall(string? key)
    {
        var http = TestFactory.HttpWith(
            _ => throw new InvalidOperationException("must not call HTTP"),
            out var handler);
        using var client = TestFactory.CreateClient(http: http);

        var r = await client.ActivateAsync(key!);

        Assert.False(r.IsValid);
        Assert.Equal(LicenseStatus.KeyNotFound, r.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Success_WithProductBlock_UsesResponseMetadata_WithoutCatalogCall()
    {
        using var probe = TestFactory.CreateClient();
        var token = JwtHelper.CreateLeaseToken(JwtHelper.ValidPayload(probe.GetMachineId()));
        var http = ActivateOnly(
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessJson(token)),
            out var handler);
        using var client = TestFactory.CreateClient(http: http);

        var r = await client.ActivateAsync("  naec-abc-123 ");

        Assert.True(r.IsValid);
        Assert.Equal(LicenseStatus.Valid, r.Status);
        Assert.Equal("NAEC-ABC-123", r.LicenseKey);
        Assert.Equal("Prod", r.ProductName);
        Assert.Equal("https://nodeaec.com.br/products/x", r.ProductUrl);
        Assert.Single(handler.Requests);

        // Locks the server API contract: normalized key, machine binding, platform.
        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("NAEC-ABC-123", doc.RootElement.GetProperty("licenseKey").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("machineId").GetString()));
        Assert.Equal("Revit / Windows", doc.RootElement.GetProperty("platform").GetString());
    }

    [Fact]
    public async Task Success_WithoutProductBlock_ResolvesProductFromToken()
    {
        using var probe = TestFactory.CreateClient();
        var mid = probe.GetMachineId();
        var token = JwtHelper.CreateLeaseToken(JwtHelper.ValidPayload(mid));
        var json = JsonSerializer.Serialize(new { leaseToken = token, expiresAt = "2026-12-31T00:00:00Z" });
        var http = ActivateOnly(
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, json),
            out var handler);
        using var client = TestFactory.CreateClient(http: http);

        var r = await client.ActivateAsync("NAEC-KEY");

        Assert.True(r.IsValid);
        Assert.Equal("My Product", r.ProductName);
        Assert.Equal("https://nodeaec.com.br/products/my-product", r.ProductUrl);
        Assert.DoesNotContain(handler.Requests, req => req.RequestUri!.AbsolutePath.Contains("/products"));
    }

    [Fact]
    public async Task Success_WithMalformedLeaseToken_ReturnsNetworkError()
    {
        // ParseJwtPayload throws inside ActivateAsync and is mapped to NetworkError.
        var http = ActivateOnly(
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessJson("a.!!!.c")),
            out _);
        using var client = TestFactory.CreateClient(http: http);

        var r = await client.ActivateAsync("NAEC-KEY");

        Assert.False(r.IsValid);
        Assert.Equal(LicenseStatus.NetworkError, r.Status);
    }

    [Fact]
    public async Task Success_MissingLeaseToken_ReturnsNetworkError()
    {
        var http = ActivateOnly(
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{\"expiresAt\":\"2026-12-31T00:00:00Z\"}"),
            out _);
        using var client = TestFactory.CreateClient(http: http);

        var r = await client.ActivateAsync("NAEC-KEY");

        Assert.False(r.IsValid);
        Assert.Equal(LicenseStatus.NetworkError, r.Status);
    }

    [Fact]
    public async Task Forbidden_WithLimitReached_ReturnsSeatLimitReached()
    {
        var http = ActivateOnly(
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.Forbidden, "ACTIVATION_LIMIT_REACHED"),
            out _);
        using var client = TestFactory.CreateClient(http: http);

        var r = await client.ActivateAsync("NAEC-KEY");

        Assert.False(r.IsValid);
        Assert.Equal(LicenseStatus.SeatLimitReached, r.Status);
    }

    [Fact]
    public async Task Forbidden_Other_ReturnsExpired()
    {
        var http = ActivateOnly(
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.Forbidden, "SUSPENDED"),
            out _);
        using var client = TestFactory.CreateClient(http: http);

        var r = await client.ActivateAsync("NAEC-KEY");

        Assert.Equal(LicenseStatus.Expired, r.Status);
    }

    [Fact]
    public async Task NotFound_WithLicenseNotFound_ReturnsKeyNotFound()
    {
        var http = ActivateOnly(
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "LICENSE_NOT_FOUND"),
            out _);
        using var client = TestFactory.CreateClient(http: http);

        var r = await client.ActivateAsync("NAEC-KEY");

        Assert.Equal(LicenseStatus.KeyNotFound, r.Status);
    }

    [Fact]
    public async Task ServerError_ReturnsNetworkErrorWithStatusCode()
    {
        var http = ActivateOnly(
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "boom"),
            out _);
        using var client = TestFactory.CreateClient(http: http);

        var r = await client.ActivateAsync("NAEC-KEY");

        Assert.Equal(LicenseStatus.NetworkError, r.Status);
        Assert.Contains("InternalServerError", r.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpThrows_ReturnsNetworkError()
    {
        var http = TestFactory.HttpWith(
            _ => throw new HttpRequestException("dns down"),
            out _);
        using var client = TestFactory.CreateClient(http: http);

        var r = await client.ActivateAsync("NAEC-KEY");

        Assert.Equal(LicenseStatus.NetworkError, r.Status);
        Assert.Contains("Could not reach", r.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomDeviceName_IsSentInBody()
    {
        using var probe = TestFactory.CreateClient();
        var token = JwtHelper.CreateLeaseToken(JwtHelper.ValidPayload(probe.GetMachineId()));
        var http = ActivateOnly(
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, SuccessJson(token)),
            out var handler);
        using var client = TestFactory.CreateClient(http: http);

        await client.ActivateAsync("NAEC-KEY", "MY-PC");

        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("MY-PC", doc.RootElement.GetProperty("deviceName").GetString());
    }
}
