using System;
using System.IO;
using NodeAec.Connector.Diagnostics;
using NodeAec.Connector.Storage;
using Xunit;

namespace NodeAec.Connector.Tests;

public class ConnectorLogTests : IDisposable
{
    private readonly string _tempDir;

    public ConnectorLogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ConnectorLogTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        LeaseStorage.SetCustomBasePath(_tempDir);
    }

    public void Dispose()
    {
        LeaseStorage.SetCustomBasePath(null);
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Write_FormatsLineAsIso8601WithLevel()
    {
        ConnectorLog.Write("INFO", "mensagem de teste");

        string content = ReadShared(ConnectorLog.GetLogFilePath());

        Assert.Matches(
            @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[+-]\d{2}:\d{2} \[INFO\] mensagem de teste",
            content);
    }

    [Fact]
    public void Write_PastSizeLimit_RotatesToBackupInsteadOfTruncating()
    {
        string path = ConnectorLog.GetLogFilePath();
        string backup = path + ".1";

        // Linhas de ~1 KB: 600 linhas ≈ 600 KB ultrapassa o limite de 512 KB.
        string filler = new string('x', 1024);
        for (int i = 0; i < 600; i++)
        {
            ConnectorLog.Write("INFO", filler);
        }

        // Rotação real: o histórico vai para connector.log.1 — nada é truncado fora.
        Assert.True(File.Exists(backup));
        Assert.True(new FileInfo(backup).Length > 0);
        Assert.Contains(filler, ReadShared(backup));

        // Após rotacionar, o arquivo ativo volta a ficar abaixo do limite.
        Assert.True(new FileInfo(path).Length <= 512 * 1024);
    }

    [Fact]
    public void Write_WithUnwritableBasePath_DoesNotThrow()
    {
        // Caminho de base inválido (aspas não existem em paths Windows): o log é
        // fail-soft por contrato — nunca pode lançar para o chamador.
        LeaseStorage.SetCustomBasePath(Path.Combine(_tempDir, "in\"valido"));

        ConnectorLog.Write("ERROR", "deve ser silencioso");
    }

    /// <summary>
    /// Lê um arquivo possivelmente aberto pelo writer do log — <c>File.ReadAllText</c>
    /// compartilha só leitura e recusaria o handle de escrita ativo.
    /// </summary>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
