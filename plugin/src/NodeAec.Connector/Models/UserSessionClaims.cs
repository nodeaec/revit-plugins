using System.Text.Json.Serialization;

namespace NodeAec.Connector.Models;

/// <summary>
/// Claims de identidade do token de sessão do usuário emitido pela plataforma
/// Node.aec (id, email, name). O lease mestre não carrega email/nome — só o id
/// público em "sub" —, por isso a identidade exibida na UI deve vir do token
/// de usuário salvo na sessão.
/// </summary>
public class UserSessionClaims
{
    /// <summary>Identificador público do usuário (14 caracteres Base62).</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Email da conta, usado na UI "Minha Conta".</summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>Nome exibido do usuário.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
