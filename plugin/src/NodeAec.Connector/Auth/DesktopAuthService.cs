using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NodeAec.Connector.Config;

namespace NodeAec.Connector.Auth;

/// <summary>
/// Serviço de autenticação para desktop via Browser SSO com servidor loopback local (RFC 8252).
/// Abre o navegador padrão do sistema, captura o token via redirecionamento seguro com CSRF state
/// e encerra o listener.
/// </summary>
public class DesktopAuthService
{
    private readonly string _webAuthBaseUrl;

    public DesktopAuthService(string? webAuthBaseUrl = null)
    {
        _webAuthBaseUrl = webAuthBaseUrl ?? ConnectorConfig.WebAuthUrl;
    }

    /// <summary>
    /// Gera um token CSRF seguro de 32 bytes codificado em Base64Url.
    /// </summary>
    public static string GenerateSecureState()
    {
        byte[] bytes = new byte[32];

        // RNG instanciado (em vez de RandomNumberGenerator.Fill, que só existe em .NET 6+)
        // para valer também em .NET Framework 4.8 — Revit 2023/2024.
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);

        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    /// <summary>
    /// Localiza uma porta TCP efêmera livre no endereço de loopback (127.0.0.1).
    /// </summary>
    public static int GetAvailableLoopbackPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    /// <summary>
    /// Constrói a URL completa para iniciar o fluxo de autorização no navegador.
    /// </summary>
    public string BuildAuthUrl(int port, string state)
    {
        string sep = _webAuthBaseUrl.Contains('?') ? "&" : "?";
        return $"{_webAuthBaseUrl}{sep}port={port}&state={Uri.EscapeDataString(state)}";
    }

    /// <summary>
    /// Indica se o caminho recebido no loopback é o callback de autenticação aceito.
    /// O portal web redireciona para <c>/callback</c> (sem barra final); o listener também
    /// aceita <c>/callback/</c> e ignora qualquer outro caminho (favicon, sondagens, etc.).
    /// </summary>
    /// <param name="path">Caminho absoluto da requisição recebida.</param>
    public static bool IsCallbackPath(string? path)
    {
        return string.Equals(path, "/callback", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/callback/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compara dois textos em tempo constante. A comparação ingênua de <c>state</c>
    /// vaza o prefixo correto por timing — não é praticamente explorável aqui, mas o
    /// comparador é barato. <c>CryptographicOperations.FixedTimeEquals</c> não existe no
    /// net48 (Revit 2023/2024), então a comparação é manual e sem saída antecipada.
    /// </summary>
    /// <param name="left">Primeiro texto (pode ser nulo).</param>
    /// <param name="right">Segundo texto (pode ser nulo).</param>
    /// <returns><c>true</c> quando os textos são idênticos byte a byte (UTF-8).</returns>
    internal static bool FixedTimeEquals(string? left, string? right)
    {
        if (left is null || right is null) return left is null && right is null;

        byte[] a = Encoding.UTF8.GetBytes(left);
        byte[] b = Encoding.UTF8.GetBytes(right);
        int diff = a.Length ^ b.Length;
        int min = Math.Min(a.Length, b.Length);
        for (int i = 0; i < min; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }

    /// <summary>
    /// Sonda uma porta de loopback, faz o bind do <see cref="HttpListener"/> e o inicia,
    /// repetindo com outra porta em caso de corrida (L2): a porta é liberada entre a
    /// sondagem e o bind, então outro processo pode ocupá-la nesse intervalo. Quando todas
    /// as tentativas falham, lança <see cref="InvalidOperationException"/> com mensagem
    /// amigável — nunca uma exceção crua de rede.
    /// </summary>
    /// <param name="port">Porta efetivamente vinculada (para montar a URL de redirect).</param>
    /// <returns>Listener iniciado e pronto para <c>GetContextAsync</c>.</returns>
    private static HttpListener StartLoopbackListener(out int port)
    {
        const int maxAttempts = 3;
        Exception? lastFailure = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            port = GetAvailableLoopbackPort();
            var listener = new HttpListener();
            // Prefixo na raiz para aceitar exatamente o caminho que o portal emite (`/callback`),
            // que não termina em barra — prefixo com barra final não casaria com ele.
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");

            try
            {
                listener.Start();
                return listener;
            }
            catch (Exception ex) when (ex is HttpListenerException || ex is InvalidOperationException)
            {
                // Bind perdido na corrida (ou porta recusada) → fecha e tenta outra porta.
                lastFailure = ex;
                try { listener.Close(); } catch { }
            }
        }

        throw new InvalidOperationException(
            "Não foi possível abrir a porta local de login (a porta foi ocupada ou o acesso à rede local está restrito). Feche outras janelas de login e tente novamente.",
            lastFailure);
    }

    /// <summary>
    /// Inicia o loopback listener local e abre o navegador padrão para o usuário entrar com sua conta.
    /// Aguarda a resposta por até 120 segundos.
    /// </summary>
    public async Task<string> LoginViaBrowserAsync(CancellationToken cancellationToken = default)
    {
        using var listener = StartLoopbackListener(out int port);
        string state = GenerateSecureState();
        string authUrl = BuildAuthUrl(port, state);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            listener.Stop();
            throw new InvalidOperationException($"Não foi possível abrir o navegador padrão: {ex.Message}", ex);
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            using (linkedCts.Token.Register(() =>
            {
                try { listener.Abort(); } catch { }
            }))
            {
                while (true)
                {
                    var context = await listener.GetContextAsync().ConfigureAwait(false);
                    var request = context.Request;
                    var response = context.Response;

                    // Ignora requisições que não sejam o callback (favicon, health probes...).
                    if (!IsCallbackPath(request.Url?.AbsolutePath))
                    {
                        response.StatusCode = 404;
                        response.Close();
                        continue;
                    }

                    string? receivedState = request.QueryString["state"];
                    string? userToken = request.QueryString["token"];

                    if (string.IsNullOrEmpty(receivedState) || !FixedTimeEquals(receivedState, state) || string.IsNullOrEmpty(userToken))
                    {
                        byte[] errorBytes = Encoding.UTF8.GetBytes("Falha na autenticação: Estado inválido ou token ausente.");
                        response.StatusCode = 400;
                        response.ContentType = "text/plain; charset=utf-8";
                        await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length, cancellationToken).ConfigureAwait(false);
                        response.Close();
                        // L1: responde 400 e CONTINUA aguardando. A porta do loopback é
                        // descobrível via `netstat` — qualquer processo local pode atirar um
                        // `/callback` com `state` errado, e isso não pode encerrar a espera
                        // legítima do usuário dentro dos 120 s.
                        continue;
                    }

                    string successHtml = @"<!DOCTYPE html>
<html lang=""pt-BR"">
<head>
  <meta charset=""utf-8"">
  <title>Node.aec — Autenticado</title>
  <style>
    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #0f172a; color: #f8fafc; text-align: center; padding: 60px 20px; }
    .card { max-width: 480px; margin: 0 auto; background: #1e293b; padding: 40px; border-radius: 12px; border: 1px solid #334155; }
    h2 { color: #38bdf8; margin-top: 0; }
    p { color: #94a3b8; font-size: 15px; line-height: 1.5; }
    .check { font-size: 48px; color: #4ade80; margin-bottom: 12px; }
  </style>
</head>
<body>
  <div class=""card"">
    <div class=""check"">&#10003;</div>
    <h2>Login Concluído com Sucesso!</h2>
    <p>Sua conta foi conectada ao Autodesk Revit.<br>Você já pode fechar esta aba do navegador e voltar ao Revit.</p>
  </div>
</body>
</html>";
                    byte[] buffer = Encoding.UTF8.GetBytes(successHtml);
                    response.ContentType = "text/html; charset=utf-8";
                    response.StatusCode = 200;
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    response.Close();

                    return userToken;
                }
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Login cancelado.", cancellationToken);
        }
        catch (Exception) when (timeoutCts.IsCancellationRequested)
        {
            throw new InvalidOperationException("Tempo esgotado aguardando a confirmação do login no navegador. Verifique se a aba foi concluída e tente novamente.");
        }
        finally
        {
            if (listener.IsListening)
            {
                try { listener.Stop(); } catch { }
            }
        }
    }
}
