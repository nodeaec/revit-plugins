using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NodeAec.Connector.Models;

namespace NodeAec.Connector.Storage;

/// <summary>
/// Gerencia a persistência local do Master Entitlements Lease (%APPDATA%\NodeAec\entitlements.lease)
/// criptografado via Windows DPAPI (DataProtectionScope.CurrentUser).
/// </summary>
public static class LeaseStorage
{
    private static string? _customBasePath;

    /// <summary>
    /// Permite injetar um diretório base alternativo (útil para testes unitários isolados).
    /// </summary>
    public static void SetCustomBasePath(string? path)
    {
        _customBasePath = path;
    }

    public static string GetBaseDirectory()
    {
        if (!string.IsNullOrEmpty(_customBasePath))
        {
            if (!Directory.Exists(_customBasePath)) Directory.CreateDirectory(_customBasePath);
            return _customBasePath;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "NodeAec");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    public static string GetLeaseFilePath() => Path.Combine(GetBaseDirectory(), "entitlements.lease");
    public static string GetSessionFilePath() => Path.Combine(GetBaseDirectory(), "session.json");

    /// <summary>
    /// Salva o token JWT do lease mestre criptografado com DPAPI.
    /// Em Windows, a falha do DPAPI <b>não</b> degrada para texto puro: nada é gravado e o
    /// método retorna <c>false</c> (modo fechado). Em sistemas não-Windows (apenas
    /// desenvolvimento/testes — o Revit é Windows-only) grava texto puro.
    /// A gravação é atômica (arquivo temporário + troca) para nunca deixar lease pela metade.
    /// </summary>
    /// <param name="jwtToken">Token JWT do lease mestre.</param>
    /// <returns><c>true</c> quando o lease foi persistido com segurança.</returns>
    public static bool SaveMasterLease(string jwtToken)
    {
        if (string.IsNullOrWhiteSpace(jwtToken)) return false;

        byte[] rawBytes = Encoding.UTF8.GetBytes(jwtToken);
        if (!TryProtect(rawBytes, out byte[] bytesToWrite, out string? failure))
        {
            Diagnostics.ConnectorLog.Write("ERROR", $"Não foi possível proteger o lease local: {failure}.");
            return false;
        }

        try
        {
            WriteAllBytesAtomic(GetLeaseFilePath(), bytesToWrite);
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.ConnectorLog.Write("ERROR", $"Falha ao gravar o lease local: {ex.GetType().Name}.");
            return false;
        }
    }

    /// <summary>
    /// Lê e descriptografa o token JWT do lease mestre local.
    /// Em Windows, um arquivo que não descriptografa é tratado como corrompido/alheio e
    /// descartado (retorna <c>null</c>) em vez de ser aceito como texto puro.
    /// </summary>
    public static string? LoadMasterLease()
    {
        var path = GetLeaseFilePath();
        if (!File.Exists(path)) return null;

        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) return null;

            if (!TryUnprotect(bytes, out byte[] plain, out string? failure))
            {
                Diagnostics.ConnectorLog.Write("WARN", $"Lease local ilegível descartado: {failure}.");
                return null;
            }

            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Remove o lease local (desativação / logout).
    /// </summary>
    public static void ClearMasterLease()
    {
        var path = GetLeaseFilePath();
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }

    /// <summary>
    /// Salva dados de sessão de usuário (nome, email, token) criptografados com DPAPI,
    /// sem degradação para texto puro em Windows. Gravação atômica.
    /// </summary>
    /// <param name="userEmail">Email da conta (exibição).</param>
    /// <param name="userToken">Token JWT de sessão do usuário.</param>
    /// <param name="userName">Nome exibido do usuário, quando disponível.</param>
    /// <returns><c>true</c> quando a sessão foi persistida com segurança.</returns>
    public static bool SaveSession(string? userEmail, string? userToken, string? userName = null)
    {
        var path = GetSessionFilePath();
        var json = JsonSerializer.Serialize(new
        {
            name = userName ?? string.Empty,
            email = userEmail ?? string.Empty,
            token = userToken ?? string.Empty
        });
        byte[] rawBytes = Encoding.UTF8.GetBytes(json);

        if (!TryProtect(rawBytes, out byte[] bytesToWrite, out string? failure))
        {
            Diagnostics.ConnectorLog.Write("ERROR", $"Não foi possível proteger a sessão local: {failure}.");
            return false;
        }

        try
        {
            WriteAllBytesAtomic(path, bytesToWrite);
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.ConnectorLog.Write("ERROR", $"Falha ao gravar a sessão local: {ex.GetType().Name}.");
            return false;
        }
    }

    /// <summary>
    /// Lê a sessão do usuário salva (nome, email, token). Em Windows, conteúdo que não
    /// descriptografa é descartado (retorna <c>null</c> — o usuário apenas entra novamente).
    /// Sessões antigas gravaram o id público no campo "email"; quando o token de usuário
    /// salvo contém as claims reais, elas têm prioridade e corrigem o valor armazenado
    /// silenciosamente.
    /// </summary>
    public static (string? Name, string? Email, string? Token)? LoadSession()
    {
        var path = GetSessionFilePath();
        if (!File.Exists(path)) return null;

        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) return null;

            if (!TryUnprotect(bytes, out byte[] plain, out string? failure))
            {
                Diagnostics.ConnectorLog.Write("WARN", $"Sessão local ilegível descartada: {failure}.");
                return null;
            }

            string json = Encoding.UTF8.GetString(plain);
            using var doc = JsonDocument.Parse(json);
            string? name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
            string? email = doc.RootElement.TryGetProperty("email", out var e) ? e.GetString() : null;
            string? token = doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;

            var claims = ParseUserSessionClaims(token);
            if (claims != null)
            {
                if (!string.IsNullOrWhiteSpace(claims.Email)) email = claims.Email;
                if (!string.IsNullOrWhiteSpace(claims.Name)) name = claims.Name;
            }

            return (name, email, token);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Limpa a sessão do usuário.
    /// </summary>
    public static void ClearSession()
    {
        var path = GetSessionFilePath();
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }

    /// <summary>
    /// Apaga o estado de conta inteiro — lease mestre + sessão. É o primitivo do logout da
    /// <c>ConnectorWindow</c>: sair da conta nunca pode deixar o lease de produtos para
    /// trás, senão o gate continuaria valendo com a sessão encerrada (M7 — coberto por teste).
    /// </summary>
    public static void ClearAll()
    {
        ClearMasterLease();
        ClearSession();
    }

    /// <summary>
    /// Decodifica o payload de um token JWT <b>sem</b> verificar assinatura criptográfica.
    /// Uso restrito a exibição (saudação, datas) — decisões de licença passam
    /// obrigatoriamente por <c>Gate.NodeAecGate</c>, que verifica a assinatura Ed25519.
    /// </summary>
    public static MasterLeasePayload? ParseJwtPayload(string token)
    {
        var json = DecodeJwtPayloadJson(token);
        if (json == null) return null;

        try
        {
            return JsonSerializer.Deserialize<MasterLeasePayload>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Decodifica as claims de identidade (id, email, name) do token de sessão do usuário,
    /// sem verificar assinatura (material de exibição vindo do próprio loopback).
    /// Retorna <c>null</c> para tokens ausentes, malformados ou que não carreguem essas
    /// claims (ex.: o lease mestre, que só traz o id técnico em <c>sub</c>).
    /// </summary>
    public static UserSessionClaims? ParseUserSessionClaims(string? token)
    {
        var json = DecodeJwtPayloadJson(token);
        if (json == null) return null;

        try
        {
            var claims = JsonSerializer.Deserialize<UserSessionClaims>(json);
            if (claims == null) return null;
            return string.IsNullOrWhiteSpace(claims.Email) && string.IsNullOrWhiteSpace(claims.Name)
                ? null
                : claims;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Retorna o payload (segmento base64url do meio) de um JWT como JSON,
    /// ou <c>null</c> quando o token não é um JWT utilizável.
    /// </summary>
    private static string? DecodeJwtPayloadJson(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var parts = token.Split('.');
        if (parts.Length < 2) return null;

        var base64 = parts[1].Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
            case 1: return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Protege bytes com DPAPI (CurrentUser) em Windows. Fora de Windows apenas
    /// repassa o texto puro (ambiente de desenvolvimento/teste; o Revit é Windows-only).
    /// </summary>
    /// <param name="plain">Bytes originais.</param>
    /// <param name="protectedBytes">Bytes a gravar quando o retorno é <c>true</c>.</param>
    /// <param name="reason">Motivo legível da falha quando o retorno é <c>false</c>.</param>
    /// <returns><c>false</c> somente quando o DPAPI falhou em Windows (nunca grava puro lá).</returns>
    private static bool TryProtect(byte[] plain, out byte[] protectedBytes, out string? reason)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            protectedBytes = plain;
            reason = null;
            return true;
        }

        try
        {
            protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            reason = null;
            return true;
        }
        catch (Exception ex)
        {
            protectedBytes = Array.Empty<byte>();
            reason = $"DPAPI indisponível ({ex.GetType().Name})";
            return false;
        }
    }

    /// <summary>
    /// Desprotege bytes gravados. Em Windows, conteúdo que não descriptografa é rejeitado
    /// (<c>false</c>) — nunca interpretado como texto puro. Fora de Windows, texto puro.
    /// </summary>
    /// <param name="stored">Bytes lidos do disco.</param>
    /// <param name="plain">Bytes originais quando o retorno é <c>true</c>.</param>
    /// <param name="reason">Motivo legível da falha quando o retorno é <c>false</c>.</param>
    private static bool TryUnprotect(byte[] stored, out byte[] plain, out string? reason)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            plain = stored;
            reason = null;
            return true;
        }

        try
        {
            plain = ProtectedData.Unprotect(stored, null, DataProtectionScope.CurrentUser);
            reason = null;
            return true;
        }
        catch (Exception ex)
        {
            plain = Array.Empty<byte>();
            reason = $"conteúdo não DPAPI ou de outro usuário ({ex.GetType().Name})";
            return false;
        }
    }

    /// <summary>
    /// Lock de processo sobre a gravação atômica. Escritores concorrentes (heartbeat em
    /// background, sync e login disparados pela UI) atingem os mesmos arquivos: sem a
    /// trava, um <c>promote</c> no meio da escrita de outro thread deixa o arquivo
    /// truncado — DPAPI falha ao ler e todos os plugins ficam bloqueados até o próximo
    /// sync bem-sucedido.
    /// </summary>
    private static readonly object WriteLock = new();

    /// <summary>
    /// Grava bytes de forma atômica: escreve em um temporário de nome único
    /// (<c>{path}.{guid}.tmp</c>) no mesmo diretório e promove o arquivo ao destino
    /// (<c>File.Replace</c> quando já existe, que é um rename atômico em Windows;
    /// <c>File.Move</c> na primeira gravação), sempre sob <see cref="WriteLock"/>.
    /// Em sistemas sem <c>File.Replace</c> (FAT32/exFAT/alguns shares) o promote cai para
    /// apagar+mover, senão toda gravação falharia e a ativação ficaria impossível.
    /// Evita que uma queda de energia/processo deixe um lease/sessão pela metade no disco
    /// e que escritores concorrentes corrompam o arquivo um ao outro. Usa apenas APIs
    /// presentes tanto no .NET Framework 4.8 (Revit 2023/2024) quanto no .NET 8/10.
    /// </summary>
    /// <param name="path">Arquivo de destino.</param>
    /// <param name="bytes">Conteúdo a gravar.</param>
    public static void WriteAllBytesAtomic(string path, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        // Nome único por gravação: writers concorrentes jamais compartilham o mesmo temp.
        string tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            lock (WriteLock)
            {
                File.WriteAllBytes(tmp, bytes);

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tmp, path, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // L4: `File.Replace` não existe em FAT32/exFAT e alguns shares de
                        // rede. Fallback: apagar + mover sob o WriteLock — nesses sistemas
                        // de arquivo não há rename atômico de qualquer forma. O finally
                        // ainda limpa o temp se o mover falhar.
                        File.Delete(path);
                        File.Move(tmp, path);
                    }
                }
                else
                {
                    File.Move(tmp, path);
                }
            }
        }
        finally
        {
            // Promote bem-sucedido renomeia o temp (ele deixa de existir); qualquer falha
            // limpa o resíduo aqui. Best-effort: nunca mascarar a exceção original.
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch
            {
            }
        }
    }
}
