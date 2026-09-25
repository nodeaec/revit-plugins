using System;
using System.Collections.Generic;
using System.Linq;

namespace NodeAec.Connector.UI;

/// <summary>
/// Ação planejada para os dois botões pequenos empilhados do painel "Conector"
/// (M7): traduz o conjunto de nomes já presentes no painel em um comando único.
/// </summary>
internal enum StackedInsertion
{
    /// <summary>Os dois já existem — nada a fazer (idempotência).</summary>
    None,

    /// <summary>Nenhum existe — empilhar os dois com <c>AddStackedItems</c>.</summary>
    StackBoth,

    /// <summary>Somente o primeiro falta — adicioná-lo avulso.</summary>
    AddFirstOnly,

    /// <summary>Somente o segundo falta — adicioná-lo avulso.</summary>
    AddSecondOnly,
}

/// <summary>
/// Decisões puras de idempotência da Ribbon (M7): quais botões adicionar, dados os itens
/// já presentes no painel. Sem tipos do Revit/WPF para que rodem no projeto de testes
/// headless — <c>App.AddButtonIfMissing</c>/<c>AddStackedButtonsIfMissing</c> consomem
/// apenas o resultado aqui e executam os comandos de UI.
/// </summary>
internal static class RibbonDecisions
{
    /// <summary>
    /// Verifica presença de um nome no painel com a mesma semântica do Ribbon
    /// (<see cref="StringComparison.OrdinalIgnoreCase"/>), usada nos testes de idempotência.
    /// </summary>
    /// <param name="existingNames">Nomes dos itens já no painel.</param>
    /// <param name="candidate">Nome procurado.</param>
    /// <returns><c>true</c> quando já existe (não deve ser recriado).</returns>
    internal static bool ContainsName(IEnumerable<string> existingNames, string candidate)
    {
        return existingNames.Any(name => string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Planeja a inserção dos dois botões empilhados: ambos ausentes → empilhar;
    /// apenas um ausente → adicioná-lo avulso; nenhum ausente → nada. A comparação de
    /// nomes ignora caixa, como o painel do Revit.
    /// </summary>
    /// <param name="existingNames">Nomes dos itens já no painel.</param>
    /// <param name="firstName">Nome do primeiro botão (empilhado).</param>
    /// <param name="secondName">Nome do segundo botão (empilhado).</param>
    /// <returns>Comando a executar no painel.</returns>
    internal static StackedInsertion PlanStackedInsertion(
        IEnumerable<string> existingNames,
        string firstName,
        string secondName)
    {
        bool firstExists = ContainsName(existingNames, firstName);
        bool secondExists = ContainsName(existingNames, secondName);

        if (firstExists && secondExists) return StackedInsertion.None;
        if (!firstExists && !secondExists) return StackedInsertion.StackBoth;
        return firstExists ? StackedInsertion.AddSecondOnly : StackedInsertion.AddFirstOnly;
    }
}
