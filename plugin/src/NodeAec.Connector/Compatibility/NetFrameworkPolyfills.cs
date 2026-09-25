#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Polyfill mínimo para permitir <c>record</c> / <c>record struct</c> em
    /// .NET Framework 4.8 (Revit 2023 e 2024), onde o compilador exige este tipo
    /// para gerar os acessores <c>init</c> e ele não existe na BCL.
    /// Em .NET 8/10 o tipo real da runtime é usado e este arquivo é ignorado
    /// na íntegra pelo <c>#if</c>.
    /// </summary>
    internal sealed class IsExternalInit
    {
    }
}
#endif
