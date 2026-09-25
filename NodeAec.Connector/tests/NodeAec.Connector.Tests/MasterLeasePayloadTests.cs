using System;
using NodeAec.Connector.Models;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// Faixa e legibilidade dos claims de data do lease (M5): segundos unix fora da faixa
/// plausível jamais podem lançar <c>ArgumentOutOfRangeException</c> (isso alcançaria os
/// blocos <c>finally</c> da UI) nem renderizar "01/01/1970" — e prazo ilegível nunca
/// libera (modo fechado).
/// </summary>
public class MasterLeasePayloadTests
{
    [Theory]
    [InlineData(0)]                // claim ausente
    [InlineData(-86400)]           // negativo (relógio ou payload forjado/corrompido)
    [InlineData(4102444800)]       // 2100-01-01: exatamente no teto — fora da faixa
    [InlineData(99999999999)]      // muito além de qualquer data plausível
    [InlineData(long.MaxValue)]
    public void ExpiresAt_OutOfRangeExp_IsNullAndTreatedAsExpired(long exp)
    {
        var payload = new MasterLeasePayload { Exp = exp };

        Assert.Null(payload.ExpiresAt);   // não lança e não devolve 01/01/1970
        Assert.True(payload.IsExpired);   // modo fechado: prazo ilegível = expirado
    }

    [Fact]
    public void ExpiresAt_PlausibleExp_IsRenderable()
    {
        var payload = new MasterLeasePayload
        {
            Exp = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()
        };

        Assert.NotNull(payload.ExpiresAt);
        Assert.False(payload.IsExpired);
        // A data é passível de formatação `dd/MM/yyyy` sem exceção.
        Assert.False(string.IsNullOrEmpty(payload.ExpiresAt!.Value.ToString("dd/MM/yyyy")));
    }

    [Fact]
    public void IssuedAt_OutOfRangeIat_IsNullInsteadOfThrowing()
    {
        var payload = new MasterLeasePayload { Iat = 0 };

        Assert.Null(payload.IssuedAt);
    }
}
