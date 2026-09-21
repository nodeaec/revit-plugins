using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace NodeAec.Licensing.Sample.Gate;

/// <summary>
/// Gera o identificador imutável de hardware da estação (Machine ID via SHA-256).
/// Compatível com o algoritmo do Node.aec Connector e da API.
/// </summary>
public static class HardwareId
{
    public static string GetMachineId()
    {
        string? guid = null;
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                       .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            guid = key?.GetValue("MachineGuid")?.ToString();
        }
        catch
        {
            // Silencioso se registro não puder ser lido
        }

        if (string.IsNullOrWhiteSpace(guid))
        {
            guid = Environment.MachineName;
        }

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes($"{guid}:{Environment.MachineName}"));
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}
