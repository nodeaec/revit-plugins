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
    /// Inicia o loopback listener local e abre o navegador padrão para o usuário entrar com sua conta.
    /// Aguarda a resposta por até 120 segundos.
    /// </summary>
    public async Task<string> LoginViaBrowserAsync(CancellationToken cancellationToken = default)
    {
        int port = GetAvailableLoopbackPort();
        string redirectPrefix = $"http://127.0.0.1:{port}/callback/";
        string state = GenerateSecureState();

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectPrefix);
        listener.Start();

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
            var getContextTask = listener.GetContextAsync();
            using (linkedCts.Token.Register(() =>
            {
                try { listener.Abort(); } catch { }
            }))
            {
                var context = await getContextTask.ConfigureAwait(false);
                var request = context.Request;
                var response = context.Response;

                string? receivedState = request.QueryString["state"];
                string? userToken = request.QueryString["token"];

                if (string.IsNullOrEmpty(receivedState) || receivedState != state || string.IsNullOrEmpty(userToken))
                {
                    byte[] errorBytes = Encoding.UTF8.GetBytes("Falha na autenticação: Estado inválido ou token ausente.");
                    response.StatusCode = 400;
                    response.ContentType = "text/plain; charset=utf-8";
                    await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length, cancellationToken).ConfigureAwait(false);
                    response.Close();
                    throw new InvalidOperationException("Falha na validação CSRF do login.");
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
        finally
        {
            if (listener.IsListening)
            {
                try { listener.Stop(); } catch { }
            }
        }
    }
}
