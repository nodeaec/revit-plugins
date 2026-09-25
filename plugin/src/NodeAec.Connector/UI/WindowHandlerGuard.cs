using System;
using System.Threading.Tasks;
using NodeAec.Connector.Diagnostics;

namespace NodeAec.Connector.UI;

/// <summary>
/// Ponto de entrada único para handlers de janela (delegates de clique e métodos
/// <c>async void</c>). Qualquer exceção que escape de um handler é engolida e registrada
/// aqui: no WPF, a exceção de um <c>async void</c> é reapresentada no dispatcher como
/// <c>DispatcherUnhandledException</c> — e dentro do Revit isso encerra o processo
/// hospedeiro, não apenas o add-in. Handlers de UI devem ser noexcept de topo.
/// </summary>
internal static class WindowHandlerGuard
{
    /// <summary>
    /// Executa um handler assíncrono da janela com captura de exceções de topo.
    /// </summary>
    /// <param name="action">Handler a executar.</param>
    /// <param name="onError">Callback (no dispatcher) para reportar a falha à UI de forma segura; nunca propaga.</param>
    public static async Task RunAsync(Func<Task> action, Action<Exception> onError)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Report(ex, onError);
        }
    }

    /// <summary>
    /// Executa um handler síncrono da janela com captura de exceções de topo.
    /// </summary>
    /// <param name="action">Handler a executar.</param>
    /// <param name="onError">Callback (no dispatcher) para reportar a falha à UI de forma segura; nunca propaga.</param>
    public static void Run(Action action, Action<Exception> onError)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Report(ex, onError);
        }
    }

    /// <summary>
    /// Registra a falha (texto fixo e sanitizado — nunca conteúdo de exceção em log de
    /// tokens/PII) e a reporta à UI; o próprio report é protegido para que uma janela já
    /// descartada jamais repropague a exceção.
    /// </summary>
    private static void Report(Exception ex, Action<Exception> onError)
    {
        ConnectorLog.Write("ERROR", $"Erro não tratado em handler de janela: {ex.GetType().Name}.");

        try
        {
            onError(ex);
        }
        catch
        {
            // UI pode estar descartada (janela fechada); nunca repropagar daqui.
        }
    }
}
