using NodeAec.Connector.Models;

namespace NodeAec.Connector.Diagnostics;

/// <summary>
/// Decisão pura de log do heartbeat de lease (M7): traduz um <see cref="SyncResult"/> na
/// linha de WARN do log local, ou <c>null</c> quando não há nada a registrar. Os ramos de
/// chave/cache usam <b>texto fixo</b> — nunca mensagem crua do servidor (M1) — e a única
/// mensagem dinâmica é o mapeado de domínio do próprio resultado, já sem tokens.
/// </summary>
internal static class HeartbeatLog
{
    /// <summary>
    /// Monta a linha de WARN do heartbeat, ou <c>null</c> quando o lease renovou com
    /// assinatura verificada e JWKS em dia (nada a reportar).
    /// </summary>
    /// <param name="result">Resultado da <c>ValidateHeartbeatAsync</c>.</param>
    /// <returns>Mensagem pronta para <c>ConnectorLog.Write("WARN", …)</c> ou <c>null</c>.</returns>
    internal static string? WarningMessage(SyncResult result)
    {
        if (!result.Success)
        {
            return $"Heartbeat de lease falhou: {result.Message}";
        }

        if (!result.KeysVerified)
        {
            // M1: texto fixo e sanitizado (nunca mensagem do servidor) — o lease renovou
            // sem chave para conferir a assinatura; o gate nega até o JWKS voltar.
            return "Heartbeat renovou o lease sem verificar a assinatura (JWKS indisponível).";
        }

        if (!result.JwksRefreshed)
        {
            return "Heartbeat validou o lease, mas o cache JWKS não pôde ser renovado.";
        }

        return null;
    }
}
