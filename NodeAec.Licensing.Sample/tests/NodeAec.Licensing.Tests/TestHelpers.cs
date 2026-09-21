using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace NodeAec.Licensing.Tests;

public static class TestHelpers
{
    public static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "NodeAecSampleTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void DeleteTempDir(string? dir)
    {
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    public static string CreateTestJwt(string machineId, long expUnix, object entitlements)
    {
        string header = "{\"alg\":\"EdDSA\",\"typ\":\"JWT\"}";
        string payload = JsonSerializer.Serialize(new
        {
            sub = "usr_test123456",
            mid = machineId,
            scope = "master-lease",
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            exp = expUnix,
            entitlements
        });

        string b64Header = Base64UrlEncode(Encoding.UTF8.GetBytes(header));
        string b64Payload = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        string dummySignature = Base64UrlEncode(new byte[64]);

        return $"{b64Header}.{b64Payload}.{dummySignature}";
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }
}
