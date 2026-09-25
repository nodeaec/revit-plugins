using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NodeAec.Connector.Cryptography;
using NodeAec.Connector.Hardware;
using NodeAec.Connector.Models;
using NodeAec.Connector.Storage;

namespace NodeAec.Connector.Gate;

/// <summary>
/// Micro-SDK canônico de validação offline para plugins parceiros e ferramentas internas
/// do ecossistema Node.aec. Zero chamadas de rede: lê o lease mestre local (DPAPI) e confere
/// a assinatura Ed25519 com o JWKS em cache antes de confiar em qualquer claim.
/// </summary>
public static class NodeAecGate
{
    /// <summary>Tolerância de relógio (segundos) aceita para o claim `iat` estar no futuro.</summary>
    private const long ClockSkewToleranceSeconds = 300;
    public class GateResult
    {
        public bool IsLicensed { get; set; }
        public string? LicenseType { get; set; }
        public string? LicenseKey { get; set; }
        public string? ProductName { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public string Message { get; set; } = string.Empty;

        public static GateResult Success(string type, string? key, string? name, DateTimeOffset? expiresAt, string message = "Licença ativa e verificada.")
        {
            return new GateResult
            {
                IsLicensed = true,
                LicenseType = type,
                LicenseKey = key,
                ProductName = name,
                ExpiresAt = expiresAt,
                Message = message
            };
        }

        public static GateResult Failure(string message)
        {
            return new GateResult
            {
                IsLicensed = false,
                Message = message
            };
        }
    }

    /// <summary>
    /// Valida se o produto identificado por <paramref name="productSlug"/> possui
    /// concessão ativa nesta estação de trabalho. Executa localmente sem acessar a rede:
    /// confere a assinatura Ed25519 do lease e só então confia nos claims.
    /// </summary>
    public static GateResult Validate(string productSlug)
    {
        if (string.IsNullOrWhiteSpace(productSlug))
        {
            return GateResult.Failure("Slug do produto não informado para validação.");
        }

        string? jwtToken = LeaseStorage.LoadMasterLease();
        if (string.IsNullOrWhiteSpace(jwtToken))
        {
            return GateResult.Failure("Nenhuma credencial do Node.aec encontrada nesta estação. Abra o Node.aec Connector na Ribbon para entrar com sua conta ou ativar sua licença.");
        }

        try
        {
            var payload = LeaseStorage.ParseJwtPayload(jwtToken);
            if (payload == null)
            {
                return GateResult.Failure("Concessão corrompida ou estrutura inválida. Abra o Node.aec Connector para ressincronizar.");
            }

            // 0. Verificação criptográfica (Ed25519 / RFC 8032): nenhum claim acima vale
            // alguma coisa antes da assinatura conferir. Arquivo adulterado, forjado ou
            // assinado por outra chave é rejeitado aqui, em modo fechado.
            if (!LeaseSignatureVerifier.TryVerify(jwtToken, out string? signatureReason))
            {
                Diagnostics.ConnectorLog.Write("WARN", $"Lease local rejeitado: {signatureReason}.");
                return GateResult.Failure("A licença local não passou na verificação de segurança. Conecte-se à internet e clique em atualizar no Node.aec Connector.");
            }

            // 0.1 Contrato do token: apenas leases mestres emitidos pela plataforma Node.aec.
            if (!string.Equals(payload.Iss, "node-aec", StringComparison.Ordinal))
            {
                return GateResult.Failure("Origem da licença local desconhecida. Conecte-se à internet e clique em atualizar no Node.aec Connector.");
            }

            if (!string.Equals(payload.Scope, "master-lease", StringComparison.OrdinalIgnoreCase))
            {
                return GateResult.Failure("A licença local está em formato não suportado. Conecte-se à internet e clique em atualizar no Node.aec Connector.");
            }

            // 0.2 Defesa contra relógio retroagido: emissão no futuro além da tolerância de
            // 5 minutos indica data adulterada (contas geradas com iat > now + skew).
            if (payload.Iat > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ClockSkewToleranceSeconds)
            {
                return GateResult.Failure("A data da licença local é inválida. Confira a data e hora deste computador e tente novamente.");
            }

            // 1. Validação de amarração de hardware (Machine ID)
            string currentMachineId = HardwareId.GetMachineId();
            if (!string.Equals(payload.Mid, currentMachineId, StringComparison.OrdinalIgnoreCase))
            {
                return GateResult.Failure("A concessão de licenças foi emitida para outra estação de trabalho (Hardware ID divergente).");
            }

            // 2. Validação do prazo de tolerância offline (30 dias)
            if (payload.IsExpired)
            {
                return GateResult.Failure($"O prazo de tolerância offline expirou em {payload.ExpiresAt:dd/MM/yyyy}. Conecte-se à internet para sincronizar.");
            }

            // 3. Validação do produto específico na lista de concessões
            var item = payload.Entitlements?.FirstOrDefault(e =>
                string.Equals(e.Slug, productSlug.Trim(), StringComparison.OrdinalIgnoreCase));

            if (item == null)
            {
                return GateResult.Failure($"O produto '{productSlug}' não consta nas licenças ativas desta conta. Adquira ou ative no catálogo Node.aec.");
            }

            if (!item.IsActive())
            {
                if (string.Equals(item.Status, "seat_limit_reached", StringComparison.OrdinalIgnoreCase))
                {
                    return GateResult.Failure($"O limite de computadores simultâneos para '{item.Name}' foi atingido.");
                }

                if (item.ExpiresAt.HasValue && item.ExpiresAt.Value < DateTimeOffset.UtcNow)
                {
                    return GateResult.Failure($"A licença ou período de teste de '{item.Name}' expirou em {item.ExpiresAt.Value:dd/MM/yyyy}.");
                }

                return GateResult.Failure($"A licença de '{item.Name}' está com status '{item.Status}'.");
            }

            return GateResult.Success(item.Type, item.LicenseKey, item.Name, item.ExpiresAt);
        }
        catch (Exception ex)
        {
            Diagnostics.ConnectorLog.Write("ERROR", $"Erro inesperado na validação do gate: {ex.GetType().Name}.");
            return GateResult.Failure("Não foi possível verificar a licença local. Abra o Node.aec Connector para ressincronizar.");
        }
    }

    /// <summary>
    /// Invoca a janela de gerenciamento do Node.aec Connector se carregado no AppDomain.
    /// </summary>
    public static void OpenConnector()
    {
        try
        {
            var uiType = Type.GetType("NodeAec.Connector.UI.ConnectorWindow, NodeAec.Connector");
            if (uiType != null)
            {
                var openMethod = uiType.GetMethod("Open", BindingFlags.Public | BindingFlags.Static);
                openMethod?.Invoke(null, new object?[] { null });
            }
        }
        catch
        {
            // Silencioso se o add-in do connector não estiver no mesmo processo
        }
    }
}
