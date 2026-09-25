using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace NodeAec.Connector.Hardware;

/// <summary>
/// Provedor canônico de identificador imutável de hardware (Machine ID)
/// baseado no hash SHA-256 de MachineGuid e MachineName do Windows.
/// </summary>
public static class HardwareId
{
    private static string? _cachedMachineId;

    /// <summary>
    /// Obtém o identificador único da máquina (hash SHA-256 em minúsculas).
    /// </summary>
    public static string GetMachineId()
    {
        if (!string.IsNullOrEmpty(_cachedMachineId))
        {
            return _cachedMachineId;
        }

        string? guid = null;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                           .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                guid = key?.GetValue("MachineGuid")?.ToString();
            }
            catch
            {
                // Silencioso em caso de restrição de permissões
            }
        }

        if (string.IsNullOrWhiteSpace(guid))
        {
            guid = Environment.MachineName;
        }

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes($"{guid}:{Environment.MachineName}"));

        // BitConverter em vez de Convert.ToHexString (que só existe em .NET 5+): produz o
        // MESMO hexadecimal (maiúsculo, sem separadores) após remover os traços — o
        // identificador da máquina não pode mudar, senão todas as licenças já emitidas
        // deixariam de corresponder a este computador.
        _cachedMachineId = BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        return _cachedMachineId;
    }
}
