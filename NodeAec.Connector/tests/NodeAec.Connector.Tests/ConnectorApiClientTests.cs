using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NodeAec.Connector.Client;
using NodeAec.Connector.Storage;
using Xunit;

namespace NodeAec.Connector.Tests;

public class ConnectorApiClientTests : IDisposable
{
    private readonly string _tempDir;

    public ConnectorApiClientTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NodeAecApiTests_" + Guid.NewGuid().ToString("N"));
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
    public async Task SyncMasterEntitlementsAsync_WithoutToken_FailsFast()
    {
        using var client = new ConnectorApiClient();

        var result = await client.SyncMasterEntitlementsAsync(string.Empty);

        Assert.False(result.Success);
        Assert.Contains("ausente", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SyncMasterEntitlementsAsync_OnSuccess_SavesLeaseAndReturnsEntitlements()
    {
        string mockLease = "hdr.payload.sig";
        var mockResponse = new
        {
            success = true,
            leaseToken = mockLease,
            expiresAt = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
            grantedCount = 1,
            totalCount = 1,
            entitlements = new[]
            {
                new
                {
                    slug = "revit-automator",
                    name = "Revit Automator",
                    type = "perpetual",
                    status = "active",
                    granted = true
                }
            }
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("/account/entitlements/lease", req.RequestUri!.AbsolutePath);
            Assert.NotNull(req.Headers.Authorization);
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("valid-user-jwt", req.Headers.Authorization!.Parameter);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(mockResponse))
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.SyncMasterEntitlementsAsync("valid-user-jwt");

        Assert.True(result.Success);
        Assert.Equal(1, result.GrantedCount);
        Assert.Single(result.Entitlements);
        Assert.Equal("revit-automator", result.Entitlements[0].Slug);
        Assert.Equal(mockLease, LeaseStorage.LoadMasterLease());
    }

    [Fact]
    public async Task ActivateKeyAsync_OnActivationLimitReached_ReturnsFriendlyMessage()
    {
        var errorResponse = new
        {
            code = "ACTIVATION_LIMIT_REACHED",
            error = "Seat limit reached."
        };

        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal("/license/activate", req.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(JsonSerializer.Serialize(errorResponse))
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new ConnectorApiClient("https://api.test", httpClient);

        var result = await client.ActivateKeyAsync("NAEC-KEY1-KEY2-KEY3-KEY4");

        Assert.False(result.Success);
        Assert.Contains("Limite de assentos simultâneos atingido", result.Message);
    }
}
