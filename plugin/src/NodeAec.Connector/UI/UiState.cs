using System;
using System.Collections.Generic;
using System.Linq;
using NodeAec.Connector.Models;

namespace NodeAec.Connector.UI;

/// <summary>Tonalidade do texto de estado de licença; a janela mapeia para os pincéis do UiTheme.</summary>
internal enum LicenseStatusTone
{
    /// <summary>Sem lease local — texto neutro (secundário).</summary>
    Neutral,

    /// <summary>Estado degradado/ilegível — destaque de atenção.</summary>
    Warning,

    /// <summary>Lease válido e dentro do prazo — cor de sucesso.</summary>
    Ok,
}

/// <summary>Ramo da lista de plugins em <c>PluginsWindow.RenderPlugins</c>.</summary>
internal enum PluginsView
{
    /// <summary>Sem sessão local — chamada para entrar.</summary>
    LoggedOut,

    /// <summary>Sessão ativa, mas nenhum produto vinculado — estado vazio + catálogo.</summary>
    Empty,

    /// <summary>Sessão ativa com produtos — renderizar os cards (ativos primeiro).</summary>
    List,
}

/// <summary>
/// Mapeamentos puros de estado → texto/ordem/ramo usados por
/// <c>ConnectorWindow.RenderUiFromStorage</c> e <c>PluginsWindow.RenderPlugins</c> (M7).
/// Sem dependências de WPF/Revit: fica ligado no projeto de testes e cobre os ramos de
/// "logged-out / lease ilegível / expirado / vazio / ativos-primeiro".
/// </summary>
internal static class UiState
{
    /// <summary>Texto do ramo "logged out" da lista de plugins.</summary>
    internal const string LoggedOutPluginsText = "Entre com sua conta para ver seus plugins aqui.";

    /// <summary>Texto do estado vazio da lista de plugins.</summary>
    internal const string NoPluginsText = "Nenhum plugin vinculado à sua conta ainda.";

    /// <summary>
    /// Mapeia a sessão local para o bloco de conta da janela (título, dica, visibilidade
    /// dos botões entrar/sair). Sessão ausente ou sem e-mail é tratada como deslogado.
    /// </summary>
    /// <param name="name">Nome do usuário da sessão (opcional).</param>
    /// <param name="email">E-mail da sessão; em branco/nulo ⇒ deslogado.</param>
    /// <returns>Título, dica e visibilidade dos botões.</returns>
    internal static (string Title, string Hint, bool ShowLogin, bool ShowLogout) Account(string? name, string? email)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            // Garantido pelo teste acima: anotado (NotNullWhen) no .NET 8, parâmetro
            // oblíquo no net48 — daí o bang explícito para o Hint nunca ser nulo.
            string loggedInEmail = email!;
            return string.IsNullOrWhiteSpace(name)
                ? ("Olá! Você está conectado como:", loggedInEmail, ShowLogin: false, ShowLogout: true)
                : ($"Olá, {name}! Você está conectado como:", loggedInEmail, ShowLogin: false, ShowLogout: true);
        }

        return ("Você ainda não entrou.",
                "Entre com sua conta para liberar seus plugins neste computador.",
                ShowLogin: true,
                ShowLogout: false);
    }

    /// <summary>
    /// Mapeia o lease local para o texto de status de licença e sua tonalidade.
    /// Ordem dos ramos: sem token → ilegível → <c>exp</c> ausente/fora da faixa (M5) →
    /// expirado → tudo certo. Nunca imprime 01/01/1970 nem lança.
    /// </summary>
    /// <param name="leaseJwt">Token do lease mestre carregado do disco (ou nulo/vazio).</param>
    /// <param name="payload">Payload decodificado; nulo quando o token não pôde ser lido.</param>
    /// <returns>Texto exibido e tom de cor.</returns>
    internal static (string Text, LicenseStatusTone Tone) LicenseStatus(string? leaseJwt, MasterLeasePayload? payload)
    {
        if (string.IsNullOrWhiteSpace(leaseJwt))
        {
            return ("Nenhuma licença encontrada neste computador ainda.", LicenseStatusTone.Neutral);
        }

        if (payload == null)
        {
            return ("Não conseguimos ler as licenças salvas. Tente atualizar.", LicenseStatusTone.Warning);
        }

        // `exp` ausente/fora da faixa vira null (M5): nunca imprimir 01/01/1970.
        if (payload.ExpiresAt is not { } exp)
        {
            return ("Não foi possível ler o prazo das licenças salvas. Clique em atualizar.", LicenseStatusTone.Warning);
        }

        if (payload.IsExpired)
        {
            return ($"Suas licenças estão desatualizadas desde {exp:dd/MM/yyyy}. Conecte-se à internet e clique em atualizar.",
                    LicenseStatusTone.Warning);
        }

        return ($"Tudo certo — suas licenças estão atualizadas até {exp:dd/MM/yyyy}.", LicenseStatusTone.Ok);
    }

    /// <summary>
    /// Escolhe o ramo da lista de plugins: sem sessão vence qualquer lease presente;
    /// com sessão, a lista é vazia ou renderizável.
    /// </summary>
    /// <param name="isLoggedIn">Resultado de <c>LoginRequirement.IsLoggedIn()</c>.</param>
    /// <param name="entitlementCount">Quantidade de itens do lease decodificado.</param>
    /// <returns>Ramo a renderizar.</returns>
    internal static PluginsView PluginsBranch(bool isLoggedIn, int entitlementCount)
    {
        if (!isLoggedIn) return PluginsView.LoggedOut;
        return entitlementCount == 0 ? PluginsView.Empty : PluginsView.List;
    }

    /// <summary>
    /// Ordena os produtos para exibição: concessões ativas primeiro, mantendo a ordem
    /// relativa de cada grupo (LINQ <c>OrderBy</c> é estável). Entradas nulas de um lease
    /// malformado são descartadas antes da ordenação (defesa — o emissor não as emite).
    /// </summary>
    /// <param name="entitlements">Itens do lease, na ordem do token.</param>
    /// <returns>Cópia ordenada (ativos primeiro), sem nulos.</returns>
    internal static IReadOnlyList<EntitlementItem> PluginsActiveFirst(IEnumerable<EntitlementItem> entitlements)
    {
        return entitlements.Where(e => e != null).OrderBy(e => e.IsActive() ? 0 : 1).ToList();
    }
}
