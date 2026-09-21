using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NodeAec.Connector.Models;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace NodeAec.Connector.Tests;

public static class TestHelpers
{
    public static string CreateMasterLeaseJwt(
        string sub,
        string mid,
        DateTimeOffset expiresAt,
        List<EntitlementItem> entitlements,
        string scope = "master-lease")
    {
        var header = new { alg = "EdDSA", typ = "JWT" };
        var payload = new
        {
            iss = "node-aec",
            aud = new[] { "node-aec-desktop", "node-aec-plugin" },
            sub,
            mid,
            scope,
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            exp = expiresAt.ToUnixTimeSeconds(),
            entitlements
        };

        string headerBase64 = ToBase64Url(JsonSerializer.Serialize(header));
        string payloadBase64 = ToBase64Url(JsonSerializer.Serialize(payload));
        string dummySignature = ToBase64Url("dummy-eddsa-signature-data-bytes");

        return $"{headerBase64}.{payloadBase64}.{dummySignature}";
    }

    private static string ToBase64Url(string input)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_handler(request));
    }
}