using NodeAec.Licensing.Client;

namespace NodeAec.Licensing.Tests;

/// <summary>
/// Shared factories. Every client gets a unique product slug so file-backed
/// caches (%APPDATA%/NodeAec/Licenses) never collide with real licenses on a
/// dev machine or with other tests.
/// </summary>
internal static class TestFactory
{
    internal static string UniqueSlug(string prefix = "unit-test") =>
        $"{prefix}-{Guid.NewGuid():N}";

    internal static NodeAecLicenseClient CreateClient(string? baseUrl = null, HttpClient? http = null) =>
        new(baseUrl ?? "https://api.nodeaec.com.br", UniqueSlug(), "TEST-PEM", http);

    internal static HttpClient HttpWith(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        out FakeHttpMessageHandler handler)
    {
        handler = new FakeHttpMessageHandler(responder);
        return new HttpClient(handler);
    }

    /// <summary>
    /// DPAPI is unavailable on non-Windows and in some constrained Windows
    /// contexts (e.g. processes launched via WSL interop run in a separate
    /// logon session without master keys). Probe instead of assuming.
    /// </summary>
    internal static bool IsDapiCacheAvailable()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            var blob = System.Security.Cryptography.ProtectedData.Protect(
                new byte[] { 1 }, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            var roundtrip = System.Security.Cryptography.ProtectedData.Unprotect(
                blob, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return roundtrip is { Length: 1 };
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes cache files written by tests (unique unit-test-*/cycle-* slugs)
    /// from the real %APPDATA% / ~/.config location. Best effort.
    /// </summary>
    internal static void CleanupTestCaches()
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NodeAec", "Licenses");
            if (!Directory.Exists(folder))
                return;
            foreach (var file in Directory.GetFiles(folder))
            {
                var name = Path.GetFileName(file);
                var isOurs = name.StartsWith("unit-test-", StringComparison.Ordinal)
                    || name.StartsWith("cycle-", StringComparison.Ordinal);
                var isCache = name.EndsWith(".lic", StringComparison.Ordinal)
                    || name.EndsWith(".product.json", StringComparison.Ordinal);
                if (isOurs && isCache)
                    File.Delete(file);
            }
        }
        catch
        {
            // Best effort: never fail a test on cleanup.
        }
    }
}
