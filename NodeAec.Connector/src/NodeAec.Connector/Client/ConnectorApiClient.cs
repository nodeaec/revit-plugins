using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NodeAec.Connector.Config;
using NodeAec.Connector.Hardware;
using NodeAec.Connector.Models;
using NodeAec.Connector.Storage;

namespace NodeAec.Connector.Client;

/// <summary>
/// Cliente HTTP para a API oficial do Node.aec.
/// Executa sincronização do Master Entitlements Lease, ativação de chaves avulsas,
/// renovação periódica (heartbeat) e desativação de assentos.
/// </summary>
public class ConnectorApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _baseUrl;

    public ConnectorApiClient(string? baseUrl = null, HttpClient? httpClient = null)
    {
        _baseUrl = (baseUrl ?? ConnectorConfig.ApiBaseUrl).TrimEnd('/');
        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }
    }

    /// <summary>
    /// Sincroniza todas as licenças ativas do usuário autenticado para a máquina atual,
    /// obtendo o Master Entitlements Lease assinado e persistindo via DPAPI.
    /// </summary>
    public async Task<SyncResult> SyncMasterEntitlementsAsync(string userToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userToken))
        {
            return SyncResult.Failed("Token de autenticação do usuário ausente.");
        }

        string machineId = HardwareId.GetMachineId();
        string deviceName = Environment.MachineName;

        var requestPayload = new
        {
            machineId,
            deviceName,
            platform = ConnectorConfig.PlatformDescription,
            connectorVersion = ConnectorConfig.Version,
        };

        var requestJson = JsonSerializer.Serialize(requestPayload);
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/account/entitlements/lease")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken.Trim());

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string errorMsg = ParseApiErrorMessage(responseBody, (int)response.StatusCode);
                return SyncResult.Failed(errorMsg);
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            string? leaseToken = root.TryGetProperty("leaseToken", out var lt) ? lt.GetString() : null;
            if (string.IsNullOrWhiteSpace(leaseToken))
            {
                return SyncResult.Failed("Resposta da API não continha o token de concessão (leaseToken).");
            }

            int granted = root.TryGetProperty("grantedCount", out var gc) ? gc.GetInt32() : 0;
            int total = root.TryGetProperty("totalCount", out var tc) ? tc.GetInt32() : 0;

            DateTimeOffset? expiresAt = null;
            if (root.TryGetProperty("expiresAt", out var expElem) && expElem.GetString() is string expStr)
            {
                if (DateTimeOffset.TryParse(expStr, out var parsedExp)) expiresAt = parsedExp;
            }

            var entitlements = ParseEntitlements(root) ?? new List<EntitlementItem>();

            // Atualiza o cache de chaves públicas (JWKS) para verificação offline do lease
            await SigningKeyStore.RefreshAsync(_baseUrl, _httpClient, cancellationToken).ConfigureAwait(false);

            // Salva o token mestre em disco protegido com DPAPI (falha = modo fechado, sem texto puro)
            if (!LeaseStorage.SaveMasterLease(leaseToken))
            {
                return SyncResult.Failed("Suas licenças foram recebidas, mas não puderam ser salvas neste computador. Verifique as permissões do usuário e tente novamente.");
            }

            return SyncResult.Succeeded(leaseToken, entitlements, expiresAt, granted, total);
        }
        catch (Exception ex)
        {
            return SyncResult.Failed($"Erro de conexão com o servidor Node.aec: {ex.Message}");
        }
    }

    /// <summary>
    /// Ativa uma chave de licença manual avulsa (NAEC-XXXX-...) para esta máquina.
    /// </summary>
    public async Task<SyncResult> ActivateKeyAsync(string licenseKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return SyncResult.Failed("Informe a chave de licença no formato NAEC-XXXX-...");
        }

        string machineId = HardwareId.GetMachineId();
        string deviceName = Environment.MachineName;

        var requestPayload = new
        {
            licenseKey = licenseKey.Trim().ToUpperInvariant(),
            machineId,
            deviceName,
            platform = ConnectorConfig.PlatformDescription,
            clientVersion = ConnectorConfig.Version,
        };

        var requestJson = JsonSerializer.Serialize(requestPayload);
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync($"{_baseUrl}/license/activate", content, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string errorMsg = ParseApiErrorMessage(responseBody, (int)response.StatusCode);
                return SyncResult.Failed(errorMsg);
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            string? leaseToken = root.TryGetProperty("leaseToken", out var lt) ? lt.GetString() : null;
            if (string.IsNullOrWhiteSpace(leaseToken))
            {
                return SyncResult.Failed("Resposta da API não continha o token de concessão.");
            }

            // ATENÇÃO: o lease emitido por /license/activate é de produto único (sem claim
            // `entitlements`) e NUNCA substitui o Master Entitlements Lease local. Gravá-lo em
            // `entitlements.lease` apagaria as demais concessões e o gate passaria a negar
            // tudo. A ativação só libera de fato quando a conta ressincroniza o lease mestre
            // (fluxo tratado pela janela após este retorno).
            await SigningKeyStore.RefreshAsync(_baseUrl, _httpClient, cancellationToken).ConfigureAwait(false);

            var payload = LeaseStorage.ParseJwtPayload(leaseToken);

            return SyncResult.Succeeded(
                leaseToken,
                new List<EntitlementItem>(),
                payload?.ExpiresAt,
                0,
                0,
                "Chave ativada com sucesso!");
        }
        catch (Exception ex)
        {
            return SyncResult.Failed($"Falha ao ativar chave: {ex.Message}");
        }
    }

    /// <summary>
    /// Valida o lease token atual com a API e emite um lease renovado (heartbeat).
    /// </summary>
    public async Task<SyncResult> ValidateHeartbeatAsync(string? leaseToken = null, CancellationToken cancellationToken = default)
    {
        string? token = leaseToken ?? LeaseStorage.LoadMasterLease();
        if (string.IsNullOrWhiteSpace(token))
        {
            return SyncResult.Failed("Nenhum lease token encontrado para validar.");
        }

        string machineId = HardwareId.GetMachineId();
        var requestPayload = new { machineId };
        var requestJson = JsonSerializer.Serialize(requestPayload);
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/license/validate")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return SyncResult.Failed(ParseApiErrorMessage(responseBody, (int)response.StatusCode));
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // A API responde `valid: false` quando a concessão não vale mais nesta máquina.
            if (root.TryGetProperty("valid", out var validElem) &&
                validElem.ValueKind == JsonValueKind.False)
            {
                return SyncResult.Failed("A plataforma informou que esta concessão não está mais válida neste computador. Entre com sua conta para renovar.");
            }

            string? renewedToken = root.TryGetProperty("leaseToken", out var lt) ? lt.GetString() : null;
            if (!string.IsNullOrWhiteSpace(renewedToken))
            {
                await SigningKeyStore.RefreshAsync(_baseUrl, _httpClient, cancellationToken).ConfigureAwait(false);

                if (!LeaseStorage.SaveMasterLease(renewedToken))
                {
                    return SyncResult.Failed("A licença foi renovada, mas não puderam ser salvas neste computador. Verifique as permissões do usuário.");
                }

                token = renewedToken;
            }

            // A resposta pode trazer status granulares mais frescos que o token local
            // (ex.: `seat_released`); quando presente, ela tem prioridade sobre o payload.
            var entitlements = ParseEntitlements(root) ?? LeaseStorage.ParseJwtPayload(token)?.Entitlements
                ?? new List<EntitlementItem>();
            int activeCount = entitlements.FindAll(e => e.IsActive()).Count;
            var expiresAt = LeaseStorage.ParseJwtPayload(token)?.ExpiresAt;

            return SyncResult.Succeeded(
                token,
                entitlements,
                expiresAt,
                activeCount,
                entitlements.Count,
                "Validação concluída com sucesso.");
        }
        catch (Exception ex)
        {
            return SyncResult.Failed($"Falha de rede ao validar: {ex.Message}");
        }
    }

    /// <summary>
    /// Desativa um assento associado a esta máquina.
    /// </summary>
    public async Task<bool> DeactivateLicenseAsync(string licenseKey, CancellationToken cancellationToken = default)
    {
        string machineId = HardwareId.GetMachineId();
        var requestPayload = new { licenseKey = licenseKey.Trim(), machineId };
        var requestJson = JsonSerializer.Serialize(requestPayload);
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync($"{_baseUrl}/license/deactivate", content, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Converte o array <c>entitlements</c> da resposta, quando presente, ou retorna
    /// <c>null</c> para que o chamador use o payload do lease local como alternativa.
    /// </summary>
    private static List<EntitlementItem>? ParseEntitlements(JsonElement root)
    {
        if (!root.TryGetProperty("entitlements", out var entArray) ||
            entArray.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var entitlements = new List<EntitlementItem>();
        foreach (var item in entArray.EnumerateArray())
        {
            var ent = JsonSerializer.Deserialize<EntitlementItem>(item.GetRawText());
            if (ent != null) entitlements.Add(ent);
        }

        return entitlements;
    }

    /// <summary>
    /// Mapeia a resposta de erro da API — formato real
    /// <c>{ error: true, status, type, code, message }</c> — para uma mensagem amigável
    /// em linguagem de usuário. Prioridade: código estável mapeado → mensagem do servidor
    /// → código cru → status HTTP. Nunca lança: corpo não-JSON cai no fallback por status.
    /// </summary>
    /// <param name="responseBody">Corpo JSON (ou texto) da resposta de erro.</param>
    /// <param name="statusCode">Status HTTP da resposta.</param>
    private static string ParseApiErrorMessage(string responseBody, int statusCode)
    {
        string? code = null;
        string? serverMessage = null;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("code", out var codeElem) &&
                    codeElem.ValueKind == JsonValueKind.String)
                {
                    code = codeElem.GetString();
                }

                // `error` é booleano no contrato; só o `message` traz texto legível.
                if (doc.RootElement.TryGetProperty("message", out var msgElem) &&
                    msgElem.ValueKind == JsonValueKind.String)
                {
                    serverMessage = msgElem.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Corpo não é JSON (proxy/gateway): segue pelo status HTTP.
        }

        string? mapped = code switch
        {
            "BAD_REQUEST" => "Os dados enviados foram recusados. Revise e tente novamente.",
            "UNAUTHORIZED" => "Sua sessão expirou. Entre com sua conta novamente.",
            "FORBIDDEN" => "Você não tem permissão para esta ação.",
            "NOT_FOUND" => "Serviço não encontrado. Verifique a conexão com a plataforma Node.aec.",
            "RATE_LIMITED" or "LICENSE_RATE_LIMITED" =>
                "Muitas tentativas em pouco tempo. Aguarde alguns minutos e tente novamente.",
            "ACCOUNT_INACTIVE" => "Sua conta não está ativa. Fale com o suporte do Node.aec.",
            "LICENSE_KEY_REQUIRED" => "Informe a chave de licença (formato NAEC-XXXX-XXXX-XXXX-XXXX).",
            "MACHINE_ID_REQUIRED" => "A identificação da máquina não foi enviada. Reinicie o Connector e tente novamente.",
            "INVALID_LICENSE_KEY_FORMAT" => "Formato de chave inválido. A chave deve seguir o formato NAEC-XXXX-XXXX-XXXX-XXXX.",
            "LICENSE_NOT_FOUND" => "Chave de licença não encontrada. Verifique a digitação.",
            "LICENSE_EXPIRED" => "Esta licença ou período de avaliação expirou.",
            "LICENSE_SUSPENDED" => "Esta licença foi suspensa administrativamente. Fale com o suporte do Node.aec.",
            "LICENSE_REVOKED" => "Esta licença foi cancelada ou reembolsada. Libere outra chave.",
            "LICENSE_INACTIVE" => "Esta licença não está ativa. Fale com o suporte do Node.aec.",
            "TRIAL_ALREADY_USED" => "O período de avaliação já foi usado neste computador. Contrate uma assinatura comercial.",
            "ACTIVATION_LIMIT_REACHED" => "Limite de assentos simultâneos atingido para esta licença. Desative o assento em outro computador ou pelo portal web.",
            "ACTIVATION_NOT_FOUND" => "Este computador ainda não está registrado nesta licença. Ative a chave primeiro.",
            "LEASE_TOKEN_REQUIRED" => "Nenhuma licença local encontrada. Clique em atualizar para baixar suas licenças.",
            "INVALID_LEASE_TOKEN" => "A licença local é inválida ou foi adulterada. Atualize suas licenças na internet.",
            "LEASE_TOKEN_EXPIRED" => "O prazo de tolerância offline expirou. Conecte-se à internet para sincronizar.",
            "MACHINE_MISMATCH" => "A licença local pertence a outro computador. Entre com sua conta para ativar este equipamento.",
            _ => null,
        };

        if (!string.IsNullOrWhiteSpace(mapped)) return mapped!;
        if (!string.IsNullOrWhiteSpace(serverMessage)) return serverMessage!;
        if (!string.IsNullOrWhiteSpace(code)) return $"O servidor recusou a solicitação ({code}).";
        return $"Servidor retornou código {statusCode}.";
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
