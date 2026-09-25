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
/// e mensagem legível já pronta para exibição. Ao ultrapassar 512 KB o arquivo rotaciona
/// para <c>connector.log.1</c> (histórico preservado, não truncado) e nenhuma falha de
/// escrita pode derrubar o chamador.
/// </summary>
public static class ConnectorLog
{
    /// <summary>Nome do arquivo de log no diretório base do Connector.</summary>
    public const string LogFileName = "connector.log";

    /// <summary>Tamanho máximo (bytes) antes da rotação.</summary>
    private const long MaxBytes = 512 * 1024;

    private static readonly object Sync = new();

    // Writer cacheado do arquivo ativo: reabre só quando o caminho muda (testes trocam
    // a base via SetCustomBasePath), em vez de abrir/fechar o arquivo a cada linha.
    private static StreamWriter? _writer;
    private static string? _writerPath;

    /// <summary>Retorna o caminho absoluto do arquivo de log.</summary>
    public static string GetLogFilePath() => Path.Combine(LeaseStorage.GetBaseDirectory(), LogFileName);

    /// <summary>
    /// Registra uma linha <c>ISO8601 [NÍVEL] mensagem</c> no log local.
    /// </summary>
    /// <param name="level">Nível curto: <c>INFO</c>, <c>WARN</c> ou <c>ERROR</c>.</param>
    /// <param name="message">Mensagem já sanitizada (sem tokens, chaves ou dados pessoais).</param>
    public static void Write(string level, string message)
    {
        lock (Sync)
        {
            try
            {
                string path = GetLogFilePath();
                RotateIfNeeded(path);

                StreamWriter? writer = _writer;
                if (writer == null || !string.Equals(_writerPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    writer = OpenWriter(path);
                }

                writer.Write($"{DateTimeOffset.Now:yyyy-MM-dd'T'HH:mm:sszzz} [{level}] {message}{Environment.NewLine}");
                writer.Flush();
            }
            catch
            {
                // Diagnóstico nunca pode quebrar o fluxo principal: solta o writer para
                // a próxima escrita tentar reabrir do zero.
                CloseWriter();
            }
        }
    }

    /// <summary>
    /// Abre (ou reabre) o writer do caminho indicado. <c>FileShare.ReadWrite|Delete</c>
    /// permite que outras instâncias do Revit appendem ao mesmo arquivo e que o arquivo
    /// seja movido/apagado mesmo com o handle aberto.
    /// </summary>
    private static StreamWriter OpenWriter(string path)
    {
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        CloseWriter();
        var writer = new StreamWriter(stream, Encoding.UTF8);
        _writer = writer;
        _writerPath = path;
        return writer;
    }

    /// <summary>Fecha o writer atual (best-effort) e limpa o cache.</summary>
    private static void CloseWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
            // Liberação de handle não pode lançar para fora.
        }

        _writer = null;
        _writerPath = null;
    }

    /// <summary>
    /// Rotaciona o log quando ele cresce além do limite: o arquivo atual vira
    /// <c>connector.log.1</c> (o backup anterior é descartado) em vez de ser truncado.
    /// </summary>
    private static void RotateIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= MaxBytes)
            {
                return;
            }

            string backup = path + ".1";
            CloseWriter();
            File.Delete(backup);
            File.Move(path, backup);
        }
        catch
        {
            // Ignora falhas de rotação; a próxima escrita tenta de novo.
        }
    }
}
