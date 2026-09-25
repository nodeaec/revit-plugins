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

            var entitlements = new List<EntitlementItem>();
            if (root.TryGetProperty("entitlements", out var entArray) && entArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in entArray.EnumerateArray())
                {
                    var ent = JsonSerializer.Deserialize<EntitlementItem>(item.GetRawText());
                    if (ent != null) entitlements.Add(ent);
                }
            }

            // Salva o token mestre em disco protegido com DPAPI
            LeaseStorage.SaveMasterLease(leaseToken);

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

            // Persiste o lease emitido
            LeaseStorage.SaveMasterLease(leaseToken);

            var payload = LeaseStorage.ParseJwtPayload(leaseToken);
            var entitlements = payload?.Entitlements ?? new List<EntitlementItem>();

            return SyncResult.Succeeded(
                leaseToken,
                entitlements,
                payload?.ExpiresAt,
                entitlements.Count,
                entitlements.Count,
                "Licença ativada com sucesso!");
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

            string? renewedToken = root.TryGetProperty("leaseToken", out var lt) ? lt.GetString() : null;
            if (!string.IsNullOrWhiteSpace(renewedToken))
            {
                LeaseStorage.SaveMasterLease(renewedToken);
                token = renewedToken;
            }

            var payload = LeaseStorage.ParseJwtPayload(token);
            var entitlements = payload?.Entitlements ?? new List<EntitlementItem>();

            return SyncResult.Succeeded(
                token,
                entitlements,
                payload?.ExpiresAt,
                entitlements.Count,
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

    private static string ParseApiErrorMessage(string responseBody, int statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("code", out var codeElem))
            {
                string code = codeElem.GetString() ?? string.Empty;
                return code switch
                {
                    "ACTIVATION_LIMIT_REACHED" => "Limite de assentos simultâneos atingido para esta licença. Desative o assento em outro computador ou pelo portal web.",
                    "INVALID_LICENSE_KEY_FORMAT" => "Formato de chave inválido. A chave deve seguir o formato NAEC-XXXX-XXXX-XXXX-XXXX.",
                    "LICENSE_NOT_FOUND" => "Chave de licença não encontrada. Verifique a digitação.",
                    "LICENSE_EXPIRED" => "Esta licença ou período de avaliação expirou.",
                    "LICENSE_SUSPENDED" => "Esta licença foi suspensa administrativamente.",
                    "LEASE_TOKEN_EXPIRED" => "O prazo de tolerância offline (30 dias) expirou. Conecte-se à internet para sincronizar.",
                    "MACHINE_MISMATCH" => "O identificador da máquina não corresponde ao registro da concessão.",
                    _ => doc.RootElement.TryGetProperty("error", out var errElem) ? errElem.GetString() ?? $"Erro {statusCode}" : $"Erro {statusCode}: {code}"
                };
            }

            if (doc.RootElement.TryGetProperty("error", out var directErr))
            {
                return directErr.GetString() ?? $"Erro {statusCode}";
            }
        }
        catch
        {
            // Silencioso se não for JSON válido
        }

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
