using System;
using System.Collections.Generic;

namespace NodeAec.Connector.Models;

/// <summary>
/// Resultado da operação de sincronização ou renovação do Master Entitlements Lease.
/// </summary>
public class SyncResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int GrantedCount { get; set; }
    public int TotalCount { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? LeaseToken { get; set; }
    public List<EntitlementItem> Entitlements { get; set; } = new();

    public static SyncResult Succeeded(string leaseToken, List<EntitlementItem> entitlements, DateTimeOffset? expiresAt, int granted, int total, string message = "Licenças sincronizadas com sucesso.")
    {
        return new SyncResult
        {
            Success = true,
            Message = message,
            LeaseToken = leaseToken,
            Entitlements = entitlements,
            ExpiresAt = expiresAt,
            GrantedCount = granted,
            TotalCount = total,
        };
    }

    public static SyncResult Failed(string message)
    {
        return new SyncResult
        {
            Success = false,
            Message = message,
        };
    }
}
