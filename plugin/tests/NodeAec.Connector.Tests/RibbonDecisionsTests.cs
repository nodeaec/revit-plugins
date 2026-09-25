using NodeAec.Connector.UI;
using Xunit;

namespace NodeAec.Connector.Tests;

/// <summary>
/// Idempotência da Ribbon (M7): quais comandos executar no painel "Conector" dados os
/// itens já presentes — incluindo o ramo parcial em que só um dos dois botões empilhados
/// está faltando (regressão do <c>AddStackedButtonsIfMissing</c>).
/// </summary>
public class RibbonDecisionsTests
{
    private const string First = "NodeAec_ManagePlugins";
    private const string Second = "NodeAec_ExploreCatalog";

    [Fact]
    public void PlanStackedInsertion_BothPresent_DoesNothing()
    {
        var plan = RibbonDecisions.PlanStackedInsertion(new[] { First, Second }, First, Second);

        Assert.Equal(StackedInsertion.None, plan);
    }

    [Fact]
    public void PlanStackedInsertion_BothMissing_StacksBoth()
    {
        var plan = RibbonDecisions.PlanStackedInsertion(new[] { "NodeAec_ManageConnector" }, First, Second);

        Assert.Equal(StackedInsertion.StackBoth, plan);
    }

    [Fact]
    public void PlanStackedInsertion_OnlyFirstMissing_AddsFirstOnly()
    {
        var plan = RibbonDecisions.PlanStackedInsertion(new[] { Second }, First, Second);

        Assert.Equal(StackedInsertion.AddFirstOnly, plan);
    }

    [Fact]
    public void PlanStackedInsertion_OnlySecondMissing_AddsSecondOnly()
    {
        var plan = RibbonDecisions.PlanStackedInsertion(new[] { First }, First, Second);

        Assert.Equal(StackedInsertion.AddSecondOnly, plan);
    }

    [Fact]
    public void PlanStackedInsertion_NothingInPanel_StacksBoth()
    {
        var plan = RibbonDecisions.PlanStackedInsertion(System.Array.Empty<string>(), First, Second);

        Assert.Equal(StackedInsertion.StackBoth, plan);
    }

    [Fact]
    public void PlanStackedInsertion_IgnoresCase()
    {
        // O painel do Revit pode preservar a caixa original em recarregamentos.
        var plan = RibbonDecisions.PlanStackedInsertion(
            new[] { "nodeaec_manageplugins", "NODEAEC_EXPLORECATALOG" }, First, Second);

        Assert.Equal(StackedInsertion.None, plan);
    }

    [Fact]
    public void ContainsName_MatchesIgnoringCase()
    {
        Assert.True(RibbonDecisions.ContainsName(new[] { First }, "nodeaec_manageplugins"));
        Assert.False(RibbonDecisions.ContainsName(new[] { First }, "NodeAec_Other"));
    }
}
