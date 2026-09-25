using System;
using System.IO;
using System.Text;
using NodeAec.Connector.Storage;

namespace NodeAec.Connector.Diagnostics;

/// <summary>
/// Log local sanitizado do Connector (<c>%APPDATA%\NodeAec\connector.log</c>).
/// Serve para tornar visíveis falhas que antes eram engolidas por <c>catch</c> vazios.
/// Regra de ouro (ver skill de segurança): chamadas NUNCA devem incluir tokens JWT,
/// chaves de licença, e-mails ou identificadores de hardware — apenas tipo de erro
/// e mensagem legível já pronta para exibição. O arquivo é rotacionado ao ultrapassar
/// 512 KB e nenhuma falha de escrita pode derrubar o chamador.
/// </summary>
public static class ConnectorLog
{
    /// <summary>Nome do arquivo de log no diretório base do Connector.</summary>
    public const string LogFileName = "connector.log";

    /// <summary>Tamanho máximo (bytes) antes da rotação por truncagem.</summary>
    private const long MaxBytes = 512 * 1024;

    private static readonly object Sync = new();

    /// <summary>Retorna o caminho absoluto do arquivo de log.</summary>
    public static string GetLogFilePath() => Path.Combine(LeaseStorage.GetBaseDirectory(), LogFileName);

    /// <summary>
    /// Registra uma linha <c>ISO8601 [NÍVEL] mensagem</c> no log local.
    /// </summary>
    /// <param name="level">Nível curto: <c>INFO</c>, <c>WARN</c> ou <c>ERROR</c>.</param>
    /// <param name="message">Mensagem já sanitizada (sem tokens, chaves ou dados pessoais).</param>
    public static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                string path = GetLogFilePath();
                RotateIfNeeded(path);

                string line = $"{DateTimeOffset.Now:yyyy-MM-dd'T'HH:mm:sszzz} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnóstico nunca pode quebrar o fluxo principal.
        }
    }

    /// <summary>Trunca o log quando ele cresce além do limite.</summary>
    private static void RotateIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length > MaxBytes)
            {
                File.WriteAllText(path, string.Empty, Encoding.UTF8);
            }
        }
        catch
        {
            // Ignora falhas de rotação; a próxima escrita tenta de novo.
        }
    }
}
