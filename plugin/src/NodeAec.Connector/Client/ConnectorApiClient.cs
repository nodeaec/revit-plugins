using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NodeAec.Connector.Config;
using NodeAec.Connector.Cryptography;
using NodeAec.Connector.Hardware;
using NodeAec.Connector.Models;
using NodeAec.Connector.Storage;

namespace NodeAec.Connector.Client;

/// <summary>
/// Cliente HTTP para a API oficial do Node.aec.
/// Executa sincronização do Master Entitlements Lease, ativação de chaves avulsas,
/// renovação periódica (heartbeat) e desativação de assentos.
/// </summary>
public class ConnectorApiClient
{
    /// <summary>
    /// Tempo explícito por chamada (M6). O default do <see cref="HttpClient"/> é 100 s —
    /// um clique de "Atualizar" poderia pendurar a UI por minutos em rede ruim.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Cliente HTTP único de processo (M6): handshake TCP+TLS e resolução DNS uma vez por
    /// sessão do Revit, em vez de um por ação do usuário — churn de sockets é
    /// particularmente caro em net48/Revit 2023-2024 (HTTP.sys + DNS caching). É seguro
    /// compartilhar entre chamadas concorrentes desde que os cabeçalhos (Authorization,
    /// etc.) fiquem na <see cref="HttpRequestMessage"/> de cada requisição, como já ocorre.
    /// Nunca é descartado pelos consumidores.
    /// </summary>
    private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    /// <summary>
    /// Cria o cliente de API. Sem <paramref name="httpClient"/>, usa o
    /// <see cref="SharedHttpClient"/> de processo (compartilhado e nunca descartado por
    /// esta instância); um cliente injetado continua sendo responsabilidade do chamador.
    /// </summary>
    /// <param name="baseUrl">Base da API; quando nula usa <c>ConnectorConfig.ApiBaseUrl</c>.</param>
    /// <param name="httpClient">Cliente HTTP alternativo (testes/mocks); não é possuído nem descartado aqui.</param>
    public ConnectorApiClient(string? baseUrl = null, HttpClient? httpClient = null)
    {
        _baseUrl = (baseUrl ?? ConnectorConfig.ApiBaseUrl).TrimEnd('/');
        _httpClient = httpClient ?? SharedHttpClient;
    }

    /// <summary>
    /// Monta o <see cref="HttpClient"/> de processo com timeout explícito e
    /// <c>User-Agent</c> identificando versão do add-in (facilita diagnóstico no servidor).
    /// </summary>
    private static HttpClient CreateSharedHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler())
        {
            Timeout = RequestTimeout,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"NodeAec.Connector/{ConnectorConfig.Version}");
        return client;
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

            // Atualiza o cache de chaves públicas (JWKS) para verificação offline do lease.
            // O retorno NÃO é descartado: é propagado ao chamador em `JwksRefreshed`.
            bool jwksRefreshed = await SigningKeyStore.RefreshAsync(_baseUrl, _httpClient, cancellationToken).ConfigureAwait(false);

            // M1: assinatura + scope + mid verificados ANTES de persistir. Lease que não
            // passa na verificação jamais toca o disco (o lease anterior permanece intacto);
            // sem chave disponível, salva-se com aviso de "não verificado" para a UI, em vez
            // de reportar um sucesso que o gate rejeitaria de forma opaca depois.
            LeaseVerdict verdict = VerifyLeaseBeforeSave(leaseToken, out string? verdictReason);
            if (verdict == LeaseVerdict.Rejected)
            {
                Diagnostics.ConnectorLog.Write("WARN", $"Lease recebido recusado antes de salvar: {verdictReason}.");
                return SyncResult.Failed("A licença recebida não passou na verificação de segurança e não foi salva. Atualize novamente; se o problema persistir, contate o suporte Node.aec.");
            }

            // Salva o token mestre em disco protegido com DPAPI (falha = modo fechado, sem texto puro)
            if (!LeaseStorage.SaveMasterLease(leaseToken))
            {
                return SyncResult.Failed("Suas licenças foram recebidas, mas não puderam ser salvas neste computador. Verifique as permissões do usuário e tente novamente.");
            }

            SyncResult synced = SyncResult.Succeeded(leaseToken, entitlements, expiresAt, granted, total);
            synced.KeysVerified = verdict == LeaseVerdict.Verified;
            synced.JwksRefreshed = jwksRefreshed;
            return synced;
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
            // O lease de ativação nunca é persistido aqui (produto único), mas o refresh do
            // JWKS é propagado em `JwksRefreshed`: sem chave em cache, a sincronização mestre
            // seguinte não terá como verificar a assinatura.
            bool jwksRefreshed = await SigningKeyStore.RefreshAsync(_baseUrl, _httpClient, cancellationToken).ConfigureAwait(false);

            var payload = LeaseStorage.ParseJwtPayload(leaseToken);

            SyncResult activated = SyncResult.Succeeded(
                leaseToken,
                new List<EntitlementItem>(),
                payload?.ExpiresAt,
                0,
                0,
                "Chave ativada com sucesso!");
            activated.JwksRefreshed = jwksRefreshed;
            return activated;
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

        // Estado das chaves de verificação para propagar no resultado. Sem lease renovado
        // nada é persistido nem atualizado, então ambos permanecem no estado pleno.
        bool keysVerified = true;
        bool jwksRefreshed = true;

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
                jwksRefreshed = await SigningKeyStore.RefreshAsync(_baseUrl, _httpClient, cancellationToken).ConfigureAwait(false);

                // M1 (mesma política do sync): assinatura + scope + mid verificadas ANTES de
                // sobrescrever o lease local; rejeição preserva o lease anterior intacto.
                LeaseVerdict verdict = VerifyLeaseBeforeSave(renewedToken, out string? verdictReason);
                if (verdict == LeaseVerdict.Rejected)
                {
                    Diagnostics.ConnectorLog.Write("WARN", $"Lease renovado recusado antes de salvar: {verdictReason}.");
                    return SyncResult.Failed("A licença renovada não passou na verificação de segurança e não foi salva. Atualize novamente; se o problema persistir, contate o suporte Node.aec.");
                }

                if (!LeaseStorage.SaveMasterLease(renewedToken))
                {
                    return SyncResult.Failed("A licença foi renovada, mas não puderam ser salvas neste computador. Verifique as permissões do usuário.");
                }

                token = renewedToken;
                keysVerified = verdict == LeaseVerdict.Verified;
            }

            // A resposta pode trazer status granulares mais frescos que o token local
            // (ex.: `seat_released`); quando presente, ela tem prioridade sobre o payload.
            var entitlements = ParseEntitlements(root) ?? LeaseStorage.ParseJwtPayload(token)?.Entitlements
                ?? new List<EntitlementItem>();
            int activeCount = entitlements.FindAll(e => e.IsActive()).Count;
            var expiresAt = LeaseStorage.ParseJwtPayload(token)?.ExpiresAt;

            SyncResult renewed = SyncResult.Succeeded(
                token,
                entitlements,
                expiresAt,
                activeCount,
                entitlements.Count,
                "Validação concluída com sucesso.");
            renewed.KeysVerified = keysVerified;
            renewed.JwksRefreshed = jwksRefreshed;
            return renewed;
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
    /// Veredito da verificação de um lease recebido da API <b>antes</b> de persisti-lo.
    /// </summary>
    private enum LeaseVerdict
    {
        /// <summary>Assinatura, scope e mid conferem: pode ser salvo como verificado.</summary>
        Verified,

        /// <summary>Sem chave disponível para conferir a assinatura: salva apenas com aviso de "não verificado".</summary>
        Unverifiable,

        /// <summary>Não passou na verificação: jamais pode tocar o disco (modo fechado).</summary>
        Rejected,
    }

    /// <summary>
    /// Verifica um lease recebido da API ANTES de persisti-lo: assinatura Ed25519 (com a
    /// chave disponível), <c>scope</c> <c>master-lease</c> e amarração de hardware
    /// (<c>mid</c>) desta máquina. Nenhuma etapa confia nas anteriores: qualquer falha
    /// impede a gravação e mantém o lease anterior intacto.
    /// </summary>
    /// <param name="leaseToken">Lease JWT retornado pela API.</param>
    /// <param name="reason">Motivo legível para log quando o desfecho é <see cref="LeaseVerdict.Rejected"/> (nunca exibir ao usuário).</param>
    /// <returns>
    /// <see cref="LeaseVerdict.Verified"/> (tudo confere), <see cref="LeaseVerdict.Unverifiable"/>
    /// (sem chave disponível — gravar somente com aviso) ou <see cref="LeaseVerdict.Rejected"/>.
    /// </returns>
    private static LeaseVerdict VerifyLeaseBeforeSave(string leaseToken, out string? reason)
    {
        // 1. Assinatura primeiro: sem ela nenhum claim merece confiança. A ausência de chave
        //    não é rejeição — é impossibilidade de verificar (o chamador propaga "não verificado").
        LeaseSignatureVerifier.VerificationOutcome outcome = LeaseSignatureVerifier.Evaluate(leaseToken, out reason);
        if (outcome == LeaseSignatureVerifier.VerificationOutcome.Rejected)
        {
            return LeaseVerdict.Rejected;
        }

        // 2. Claims estruturais: verificáveis mesmo sem chave (não dependem de criptografia).
        //    Motivos fixos para o log — nunca ecoar claims de origem desconhecida.
        var payload = LeaseStorage.ParseJwtPayload(leaseToken);
        if (payload == null)
        {
            reason = "payload do lease ilegível";
            return LeaseVerdict.Rejected;
        }

        if (!string.Equals(payload.Scope, "master-lease", StringComparison.OrdinalIgnoreCase))
        {
            reason = "scope do lease não é master-lease";
            return LeaseVerdict.Rejected;
        }

        if (!string.Equals(payload.Mid, HardwareId.GetMachineId(), StringComparison.OrdinalIgnoreCase))
        {
            reason = "mid do lease divergente desta máquina";
            return LeaseVerdict.Rejected;
        }

        return outcome == LeaseSignatureVerifier.VerificationOutcome.Verified
            ? LeaseVerdict.Verified
            : LeaseVerdict.Unverifiable;
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

}
