using NodeAec.Connector.Storage;

namespace NodeAec.Connector.Auth;

/// <summary>
/// Verificação headless de sessão para disponibilidade de comandos (ex.: botão "Meus Plugins").
/// Lê apenas o armazenamento local, sem chamadas de rede, para rodar na Ribbon e em testes.
/// </summary>
public static class LoginRequirement
{
    /// <summary>
    /// Retorna true quando existe uma sessão salva com e-mail (login já realizado).
    /// </summary>
    public static bool IsLoggedIn()
    {
        var session = LeaseStorage.LoadSession();
        return session.HasValue && !string.IsNullOrWhiteSpace(session.Value.Email);
    }
}
