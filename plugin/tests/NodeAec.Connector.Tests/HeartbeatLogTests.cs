using System.Collections.Generic;
using NodeAec.Connector.Diagnostics;
using NodeAec.Connector.Models;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// Sanitização do log do heartbeat de startup (M7): linha fixa nos ramos de chave/cache
/// (nunca mensagem crua do servidor), mensagem de falha mapeada e silêncio quando está
/// tudo certo.
/// </summary>
public class HeartbeatLogTests
{
    [Fact]
    public void WarningMessage_HealthyRun_ReturnsNull()
    {
        var result = SyncResult.Succeeded("lease", new List<EntitlementItem>(), null, 1, 1);

        Assert.Null(HeartbeatLog.WarningMessage(result));
    }

    [Fact]
    public void WarningMessage_Failure_UsesStableCategoryInsteadOfResultMessage()
    {
        // P13: `Message` em falha pode ser texto cru do servidor — categoria estável só.
        var result = SyncResult.Failed("texto cru do servidor que não pode vazar para o log");

        string? warning = HeartbeatLog.WarningMessage(result);

        Assert.Equal("Heartbeat de lease falhou (falha de sincronização).", warning);
        Assert.DoesNotContain("texto cru do servidor", warning);
    }

    [Fact]
    public void WarningMessage_KeysNotVerified_UsesFixedTextInsteadOfServerMessage()
    {
        var result = new SyncResult
        {
            Success = true,
            KeysVerified = false,
            JwksRefreshed = true,
            Message = "texto cru do servidor que não pode vazar para o log",
        };

        string? warning = HeartbeatLog.WarningMessage(result);

        Assert.Equal("Heartbeat renovou o lease sem verificar a assinatura (JWKS indisponível).", warning);
        Assert.DoesNotContain("texto cru do servidor", warning);
    }

    [Fact]
    public void WarningMessage_JwksNotRefreshed_UsesFixedText()
    {
        var result = new SyncResult
        {
            Success = true,
            KeysVerified = true,
            JwksRefreshed = false,
            Message = "texto cru do servidor que não pode vazar para o log",
        };

        string? warning = HeartbeatLog.WarningMessage(result);

        Assert.Equal("Heartbeat validou o lease, mas o cache JWKS não pôde ser renovado.", warning);
        Assert.DoesNotContain("texto cru do servidor", warning);
    }

    [Fact]
    public void WarningMessage_FailureWinsOverKeyFlags()
    {
        // Precedência: falha de renovação é o fato principal a registrar.
        var result = new SyncResult { Success = false, KeysVerified = false, Message = "offline" };

        string? warning = HeartbeatLog.WarningMessage(result);

        Assert.Equal("Heartbeat de lease falhou (falha de sincronização).", warning);
        Assert.DoesNotContain("offline", warning);
    }
}
