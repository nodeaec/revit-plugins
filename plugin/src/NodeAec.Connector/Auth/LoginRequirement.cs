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
        return HasLoginEmail(LeaseStorage.LoadSession());
    }

    /// <summary>
    /// Decide se uma sessão carregada conta como login realizado: precisa existir e ter
    /// e-mail não vazio — token sem e-mail não identifica ninguém. Puro, para teste
    /// headless (o caminho completo passa por DPAPI e não é semear fora de sessão interativa).
    /// </summary>
    /// <param name="session">Sessão carregada, ou nula quando não há sessão salva.</param>
    /// <returns><c>true</c> se há sessão com e-mail preenchido.</returns>
    internal static bool HasLoginEmail((string? Name, string? Email, string? Token)? session)
    {
        return session.HasValue && !string.IsNullOrWhiteSpace(session.Value.Email);
    }
}
