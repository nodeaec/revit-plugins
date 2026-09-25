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

    /// <summary>
    /// Indica se a assinatura do lease gravado em disco por esta operação foi confirmada
    /// com uma chave pública disponível (JWKS em cache ou âncora fixa). Assume <c>true</c>
    /// quando a operação não gravou lease novo; caminhos que persistem definem sempre o
    /// valor real — um lease salvo sem chave disponível sai daqui com <c>false</c>.
    /// </summary>
    public bool KeysVerified { get; set; } = true;

    /// <summary>
    /// Resultado da atualização do cache JWKS (<c>GET /license/jwks</c>) nesta operação.
    /// <c>false</c> = a renovação das chaves falhou (a verificação pode ter usado o cache
    /// anterior). Assume <c>true</c> quando nenhuma atualização foi necessária.
    /// </summary>
    public bool JwksRefreshed { get; set; } = true;

    /// <summary>
    /// Aviso pronto para exibição quando a operação ficou degradada em relação às chaves
    /// de verificação (lease salvo sem assinatura verificada, ou JWKS não renovado).
    /// <c>null</c> = sem degradação; a UI então mostra a mensagem de sucesso padrão.
    /// </summary>
    public string? VerificationWarning =>
        !KeysVerified
            ? "Suas licenças foram salvas, mas as chaves de verificação não puderam ser obtidas — os plugins podem continuar bloqueados até a próxima sincronização bem-sucedida."
            : !JwksRefreshed
                ? "Suas licenças foram atualizadas, mas a renovação das chaves de verificação falhou agora; as chaves já salvas continuam valendo."
                : null;

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
