using System.Net;
using System.Text;

namespace NodeAec.Licensing.Tests;

/// <summary>
/// In-memory <see cref="HttpMessageHandler"/> stub. Routes requests to a
/// responder callback and records every request for API-contract assertions.
/// The responder may throw to simulate network failures.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_responder(request));
    }

    internal static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
