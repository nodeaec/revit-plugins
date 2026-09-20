using System.Net;
using System.Text.Json;
using NodeAec.Licensing.Client;
using Xunit;

namespace NodeAec.Licensing.Tests;

public class ValidateAndDeactivateAsyncTests : IDisposable
{
    public void Dispose()
    {
        TestFactory.CleanupTestCaches();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ValidateLicense_NoCachedLease_ReturnsUnlicensed()
    {
        // Unique slug => storage file cannot exist => returns before any HTTP.
        var http = TestFactory.HttpWith(
            _ => throw new InvalidOperationException("must not call HTTP"),
            out var handler);
        using var client = TestFactory.CreateClient(http: http);

        var r = await client.ValidateLicenseAsync();

        Assert.False(r.IsValid);
        Assert.Equal(LicenseStatus.Unlicensed, r.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Deactivate_NoCachedLease_ReturnsTrueWithoutHttpCall()
    {
        var http = TestFactory.HttpWith(
            _ => throw new InvalidOperationException("must not call HTTP"),
            out var handler);
        using var client = TestFactory.CreateClient(http: http);

        Assert.True(await client.DeactivateAsync());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FullCycle_ActivateValidateDeactivate_WindowsOnly()
    {
        // DPAPI file cache only works in a real Windows user session; on Linux
        // (or constrained contexts like WSL-interop launches) the cache
        // silently no-ops, so this cycle is covered there by the uncached
        // tests above.
        if (!TestFactory.IsDapiCacheAvailable())
            return;

        const string baseUrl = "https://api.nodeaec.com.br";
        var slug = TestFactory.UniqueSlug("cycle");
        using var probe = new NodeAecLicenseClient(baseUrl, slug, "TEST-PEM");
        var mid = probe.GetMachineId();
        var lease = JwtHelper.CreateLeaseToken(JwtHelper.ValidPayload(mid));
        var renewed = JwtHelper.CreateLeaseToken(JwtHelper.ValidPayload(mid));

        HttpResponseMessage Responder(HttpRequestMessage req)
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/license/activate", StringComparison.Ordinal))
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    leaseToken = lease,
                    expiresAt = "2026-12-31T00:00:00Z",
                    product = new { name = "Prod", url = "https://nodeaec.com.br/products/x" }
                }));
            if (path.EndsWith("/license/validate", StringComparison.Ordinal))
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    leaseToken = renewed,
                    expiresAt = "2026-12-31T00:00:00Z",
                    type = "commercial",
                    product = new { name = "Prod", url = "https://nodeaec.com.br/products/x" }
                }));
            if (path.EndsWith("/license/deactivate", StringComparison.Ordinal))
                return FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}");
            throw new InvalidOperationException($"unexpected request: {req.RequestUri}");
        }

        var http = TestFactory.HttpWith(Responder, out var handler);
        using var client = new NodeAecLicenseClient(baseUrl, slug, "TEST-PEM", http);

        var act = await client.ActivateAsync("NAEC-CYCLE");
        Assert.True(act.IsValid, $"activate: [{act.Status}] {act.ErrorMessage}");

        var val = await client.ValidateLicenseAsync();
        Assert.True(val.IsValid, $"validate: [{val.Status}] {val.ErrorMessage}");
        Assert.False(val.IsOffline);
        Assert.Equal(LicenseStatus.Valid, val.Status);

        Assert.True(await client.DeactivateAsync());

        // Cache was cleared: a second deactivate must not touch HTTP.
        var calls = handler.Requests.Count;
        Assert.True(await client.DeactivateAsync());
        Assert.Equal(calls, handler.Requests.Count);
    }
}
