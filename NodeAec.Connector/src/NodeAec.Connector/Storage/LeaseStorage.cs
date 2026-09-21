using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NodeAec.Connector.Models;

namespace NodeAec.Connector.Storage;

/// <summary>
/// Gerencia a persistência local do Master Entitlements Lease (%APPDATA%\NodeAec\entitlements.lease)
/// criptografado via Windows DPAPI (DataProtectionScope.CurrentUser).
/// </summary>
public static class LeaseStorage
{
    private static string? _customBasePath;

    /// <summary>
    /// Permite injetar um diretório base alternativo (útil para testes unitários isolados).
    /// </summary>
    public static void SetCustomBasePath(string? path)
    {
        _customBasePath = path;
    }

    public static string GetBaseDirectory()
    {
        if (!string.IsNullOrEmpty(_customBasePath))
        {
            if (!Directory.Exists(_customBasePath)) Directory.CreateDirectory(_customBasePath);
            return _customBasePath;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "NodeAec");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    public static string GetLeaseFilePath() => Path.Combine(GetBaseDirectory(), "entitlements.lease");
    public static string GetSessionFilePath() => Path.Combine(GetBaseDirectory(), "session.json");

    /// <summary>
    /// Salva o token JWT do lease mestre criptografado com DPAPI.
    /// </summary>
    public static void SaveMasterLease(string jwtToken)
    {
        if (string.IsNullOrWhiteSpace(jwtToken)) return;

        var path = GetLeaseFilePath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var rawBytes = Encoding.UTF8.GetBytes(jwtToken);

        byte[] bytesToWrite;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                bytesToWrite = ProtectedData.Protect(rawBytes, null, DataProtectionScope.CurrentUser);
            }
            catch
            {
                bytesToWrite = rawBytes;
            }
        }
        else
        {
            bytesToWrite = rawBytes;
        }

        File.WriteAllBytes(path, bytesToWrite);
    }

    /// <summary>
    /// Lê e descriptografa o token JWT do lease mestre local.
    /// </summary>
    public static string? LoadMasterLease()
    {
        var path = GetLeaseFilePath();
        if (!File.Exists(path)) return null;

        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) return null;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(decrypted);
                }
                catch
                {
                    // Se falhar o unprotect, pode ser token não criptografado em ambiente de teste
                    return Encoding.UTF8.GetString(bytes);
                }
            }

            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Remove o lease local (desativação / logout).
    /// </summary>
    public static void ClearMasterLease()
    {
        var path = GetLeaseFilePath();
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }

    /// <summary>
    /// Salva dados de sessão de usuário (email, token) criptografados.
    /// </summary>
    public static void SaveSession(string? userEmail, string? userToken)
    {
        var path = GetSessionFilePath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(new { email = userEmail ?? string.Empty, token = userToken ?? string.Empty });
        var rawBytes = Encoding.UTF8.GetBytes(json);

        byte[] bytesToWrite;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                bytesToWrite = ProtectedData.Protect(rawBytes, null, DataProtectionScope.CurrentUser);
            }
            catch
            {
                bytesToWrite = rawBytes;
            }
        }
        else
        {
            bytesToWrite = rawBytes;
        }

        File.WriteAllBytes(path, bytesToWrite);
    }

    /// <summary>
    /// Lê a sessão do usuário salva.
    /// </summary>
    public static (string? Email, string? Token)? LoadSession()
    {
        var path = GetSessionFilePath();
        if (!File.Exists(path)) return null;

        try
        {
            var bytes = File.ReadAllBytes(path);
            string json;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                    json = Encoding.UTF8.GetString(decrypted);
                }
                catch
                {
                    json = Encoding.UTF8.GetString(bytes);
                }
            }
            else
            {
                json = Encoding.UTF8.GetString(bytes);
            }

            using var doc = JsonDocument.Parse(json);
            string? email = doc.RootElement.TryGetProperty("email", out var e) ? e.GetString() : null;
            string? token = doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
            return (email, token);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Limpa a sessão do usuário.
    /// </summary>
    public static void ClearSession()
    {
        var path = GetSessionFilePath();
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }

    /// <summary>
    /// Decodifica o payload de um token JWT sem verificar assinatura criptográfica.
    /// </summary>
    public static MasterLeasePayload? ParseJwtPayload(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var parts = token.Split('.');
        if (parts.Length < 2) return null;

        try
        {
            var base64 = parts[1].Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return JsonSerializer.Deserialize<MasterLeasePayload>(json);
        }
        catch
        {
            return null;
        }
    }
}
