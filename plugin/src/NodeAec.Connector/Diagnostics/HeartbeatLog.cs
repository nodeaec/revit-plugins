using NodeAec.Connector.Models;

namespace NodeAec.Connector.Diagnostics;

/// <summary>
/// Decisão pura de log do heartbeat de lease (M7/P13): traduz um <see cref="SyncResult"/>
/// na linha de WARN do log local, ou <c>null</c> quando não há nada a registrar. Todos os
/// ramos são <b>texto fixo</b>: <see cref="SyncResult.Message"/> pode conter texto cru do
/// servidor (caminho não mapeado), que a regra de sanitização do log proíbe — a UI é quem
/// o exibe.
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
            // Categoria estável (P13): a mensagem do resultado não entra no log.
            return "Heartbeat de lease falhou (falha de sincronização).";
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
