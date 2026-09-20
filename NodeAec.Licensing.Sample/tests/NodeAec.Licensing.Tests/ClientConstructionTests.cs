using System.Net;
using NodeAec.Licensing.Client;
using Xunit;

namespace NodeAec.Licensing.Tests;

public class ClientConstructionTests
{
    [Fact]
    public void Constructor_NullArgs_UsesDefaultsWithoutThrowing()
    {
        using var c = new NodeAecLicenseClient(null, null, null);

        Assert.NotNull(c);
    }

    [Fact]
    public async Task BaseUrl_TrailingSlash_IsTrimmedInRequests()
    {
        var http = TestFactory.HttpWith(
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"),
            out var handler);
        using var client = new NodeAecLicenseClient("https://example.com/", TestFactory.UniqueSlug(), null, http);

        await client.ActivateAsync("NAEC-KEY");

        var uri = Assert.Single(handler.Requests).RequestUri!.ToString();
        Assert.StartsWith("https://example.com/license/activate", uri);
        Assert.DoesNotContain("//license", uri.Replace("https://", "", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Constructor_InjectedHttpClient_IsUsed()
    {
        var http = TestFactory.HttpWith(
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "LICENSE_NOT_FOUND"),
            out _);
        using var client = TestFactory.CreateClient(http: http);

        var r = await client.ActivateAsync("NAEC-KEY");

        Assert.Equal(LicenseStatus.KeyNotFound, r.Status);
    }
}
