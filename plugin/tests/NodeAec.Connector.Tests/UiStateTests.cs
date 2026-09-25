using System;
using System.Collections.Generic;
using System.Linq;
using NodeAec.Connector.Models;
using NodeAec.Connector.UI;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// Mapeamentos de estado das janelas (M7): ramos de conta, status de licença
/// (legível / sem prazo / expirado / vigente) e lista de plugins
/// (logged-out / vazio / ativos-primeiro) — a lógica de decisão antes embutida em
/// <c>ConnectorWindow.RenderUiFromStorage</c> e <c>PluginsWindow.RenderPlugins</c>.
/// </summary>
public class UiStateTests
{
    // ---------- Conta (RenderUiFromStorage) ----------

    [Fact]
    public void Account_WithFullName_ShowsGreetingAndLogout()
    {
        var (title, hint, showLogin, showLogout) = UiState.Account("Pablo", "pablo@nodeaec.com.br");

        Assert.Equal("Olá, Pablo! Você está conectado como:", title);
        Assert.Equal("pablo@nodeaec.com.br", hint);
        Assert.False(showLogin);
        Assert.True(showLogout);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Account_WithoutName_UsesGenericGreeting(string? name)
    {
        var (title, _, showLogin, showLogout) = UiState.Account(name, "pablo@nodeaec.com.br");

        Assert.Equal("Olá! Você está conectado como:", title);
        Assert.False(showLogin);
        Assert.True(showLogout);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Account_MissingEmail_ShowsLogin(string? email)
    {
        var (title, hint, showLogin, showLogout) = UiState.Account("Pablo", email);

        Assert.Equal("Você ainda não entrou.", title);
        Assert.Equal("Entre com sua conta para liberar seus plugins neste computador.", hint);
        Assert.True(showLogin);
        Assert.False(showLogout);
    }

    // ---------- Status da licença (RenderUiFromStorage) ----------

    [Fact]
    public void LicenseStatus_NoLeaseToken_IsNeutralNote()
    {
        var (text, tone) = UiState.LicenseStatus(null, payload: null);

        Assert.Equal("Nenhuma licença encontrada neste computador ainda.", text);
        Assert.Equal(LicenseStatusTone.Neutral, tone);
    }

    [Fact]
    public void LicenseStatus_UnreadablePayload_WarnsWithRefreshHint()
    {
        var (text, tone) = UiState.LicenseStatus("token-adulterado", payload: null);

        Assert.Equal("Não conseguimos ler as licenças salvas. Tente atualizar.", text);
        Assert.Equal(LicenseStatusTone.Warning, tone);
    }

    [Fact]
    public void LicenseStatus_MissingExpiry_WarnsWithoutPrintingEpoch()
    {
        // M5: exp fora da faixa/ausente vira null — o texto não pode conter 01/01/1970.
        var payload = new MasterLeasePayload { Exp = 0 };

        var (text, tone) = UiState.LicenseStatus("qualquer-token", payload);

        Assert.Equal("Não foi possível ler o prazo das licenças salvas. Clique em atualizar.", text);
        Assert.Equal(LicenseStatusTone.Warning, tone);
        Assert.DoesNotContain("1970", text);
    }

    [Fact]
    public void LicenseStatus_Expired_ShowsSinceDateWithWarning()
    {
        var payload = new MasterLeasePayload { Exp = DateTimeOffset.UtcNow.AddDays(-3).ToUnixTimeSeconds() };

        var (text, tone) = UiState.LicenseStatus("qualquer-token", payload);

        string expectedDate = payload.ExpiresAt!.Value.ToString("dd/MM/yyyy");
        Assert.Contains($"desatualizadas desde {expectedDate}", text);
        Assert.Equal(LicenseStatusTone.Warning, tone);
    }

    [Fact]
    public void LicenseStatus_Valid_ShowsUntilDateWithOkTone()
    {
        var payload = new MasterLeasePayload { Exp = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds() };

        var (text, tone) = UiState.LicenseStatus("qualquer-token", payload);

        string expectedDate = payload.ExpiresAt!.Value.ToString("dd/MM/yyyy");
        Assert.Contains($"atualizadas até {expectedDate}", text);
        Assert.Equal(LicenseStatusTone.Ok, tone);
    }

    // ---------- Lista de plugins (RenderPlugins) ----------

    [Fact]
    public void PluginsBranch_LoggedOut_WinsEvenWithEntitlements()
    {
        Assert.Equal(PluginsView.LoggedOut, UiState.PluginsBranch(isLoggedIn: false, entitlementCount: 3));
    }

    [Fact]
    public void PluginsBranch_LoggedInWithoutEntitlements_IsEmpty()
    {
        Assert.Equal(PluginsView.Empty, UiState.PluginsBranch(isLoggedIn: true, entitlementCount: 0));
    }

    [Fact]
    public void PluginsBranch_LoggedInWithEntitlements_IsList()
    {
        Assert.Equal(PluginsView.List, UiState.PluginsBranch(isLoggedIn: true, entitlementCount: 1));
    }

    [Fact]
    public void PluginsActiveFirst_MovesActiveBeforeInactive_PreservingRelativeOrder()
    {
        var inactiveByStatus = new EntitlementItem { Slug = "cancelado", Status = "cancelled" };
        var inactiveByDate = new EntitlementItem { Slug = "vencido", ExpiresAtString = "2020-01-01T00:00:00Z" };
        var activeOne = new EntitlementItem { Slug = "ativo-1" };
        var activeTwo = new EntitlementItem { Slug = "ativo-2" };

        // Ordem de entrada: inativo, ativo, ativo, inativo.
        IReadOnlyList<EntitlementItem> ordered = UiState.PluginsActiveFirst(
            new[] { inactiveByStatus, activeOne, activeTwo, inactiveByDate });

        Assert.Equal(new[] { "ativo-1", "ativo-2", "cancelado", "vencido" },
            ordered.Select(e => e.Slug).ToArray());
    }

    [Fact]
    public void PluginsActiveFirst_EmptyList_StaysEmpty()
    {
        IReadOnlyList<EntitlementItem> ordered =
            UiState.PluginsActiveFirst(Array.Empty<EntitlementItem>());

        Assert.Empty(ordered);
    }

    [Fact]
    public void PluginsActiveFirst_SkipsNullEntriesWithoutThrowing()
    {
        var active = new EntitlementItem { Slug = "ativo" };

        IReadOnlyList<EntitlementItem> ordered = UiState.PluginsActiveFirst(
            new[] { null!, active, null! });

        Assert.Single(ordered);
        Assert.Equal("ativo", ordered[0].Slug);
    }
}
