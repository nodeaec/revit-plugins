using System;
using System.Globalization;
using NodeAec.Connector.Models;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// Interpretação de <c>expiresAt</c> das concessões (L5): cultura invariante e
/// meia-noite UTC para datas sem offset — o prazo não pode variar com o formato
/// regional nem com o fuso da máquina do usuário.
/// </summary>
public class EntitlementItemTests
{
    [Fact]
    public void ExpiresAt_DateOnly_ParsesAsUtcMidnightNotLocalMidnight()
    {
        var item = new EntitlementItem { ExpiresAtString = "2030-01-15" };

        // Data sem offset vale 00:00 UTC: numa máquina UTC-3 o parse local (antigo)
        // daria 03:00 UTC e adiaria a expiração; numa UTC+ seria adiantada.
        Assert.Equal(new DateTimeOffset(2030, 1, 15, 0, 0, 0, TimeSpan.Zero), item.ExpiresAt);
    }

    [Fact]
    public void ExpiresAt_IsoTimestampWithOffset_KeepsTheInstant()
    {
        var item = new EntitlementItem { ExpiresAtString = "2030-06-01T10:00:00+02:00" };

        Assert.Equal(new DateTimeOffset(2030, 6, 1, 10, 0, 0, TimeSpan.FromHours(2)), item.ExpiresAt);
    }

    [Fact]
    public void ExpiresAt_SlashedDate_IsIndependentOfTheMachineCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            // pt-BR lê dd/MM/yyyy (3 de abril); com cultura invariante o mesmo texto é
            // MM/dd/yyyy (4 de março). O resultado não pode depender do locale da máquina.
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            var item = new EntitlementItem { ExpiresAtString = "03/04/2026" };

            Assert.Equal(new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero), item.ExpiresAt);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("não é uma data")]
    public void ExpiresAt_MissingOrUnparseable_ReturnsNull(string? expiresAtString)
    {
        var item = new EntitlementItem { ExpiresAtString = expiresAtString };

        Assert.Null(item.ExpiresAt);
    }

    // ---------- Claim `granted` (L6) ----------

    [Fact]
    public void IsActive_ClaimAbsent_DefaultsToGranted()
    {
        // Token sem o claim mantém o inicializador do modelo (true).
        var item = new EntitlementItem { Status = "active" };

        Assert.True(item.IsActive());
    }

    [Fact]
    public void IsActive_GrantedFalse_IsInactiveEvenWithActiveStatus()
    {
        var item = new EntitlementItem { Status = "active", Granted = false };

        Assert.False(item.IsActive());
    }

    [Fact]
    public void IsActive_GrantedTrueActiveStatusAndNoExpiry_IsActive()
    {
        var item = new EntitlementItem { Status = "active", Granted = true };

        Assert.True(item.IsActive());
    }
}
