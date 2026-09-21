using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Licensing.Sample.Gate;

namespace NodeAec.Licensing.Sample.Commands;

/// <summary>
/// Exemplo canônico de comando comercial protegido pelo micro-SDK NodeAecGate.
/// Executa em menos de 1ms sem tráfego de rede e sem caixas de diálogo próprias de ativação.
/// Toda a gestão de contas e licenças é centralizada no Node.aec Connector.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class SampleFeatureCommand : IExternalCommand
{
    public const string ProductSlug = "revit-automator";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // 1. Validação local e instantânea de direitos via Micro-SDK (< 1ms, zero rede)
        var check = NodeAecGate.Validate(ProductSlug);
        if (!check.IsLicensed)
        {
            var dialog = new TaskDialog("Node.aec // Licença Necessária")
            {
                MainInstruction = "Licença ativa necessária para executar esta ferramenta.",
                MainContent = $"{check.Message}\n\nAbra o Node.aec Connector na Ribbon para conectar sua conta ou ativar sua licença.",
                CommonButtons = TaskDialogCommonButtons.Close
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir Node.aec Connector...");

            if (dialog.Show() == TaskDialogResult.CommandLink1)
            {
                NodeAecGate.OpenConnector();
            }

            return Result.Cancelled;
        }

        // 2. Execução normal da funcionalidade do plugin parceiro
        string expText = check.ExpiresAt.HasValue
            ? check.ExpiresAt.Value.ToString("dd/MM/yyyy")
            : "Vitalícia";

        TaskDialog.Show("Revit Automator (Sample)",
            $"Ferramenta executada com sucesso!\n\n" +
            $"• Produto: {check.ProductName ?? ProductSlug}\n" +
            $"• Tipo: {check.LicenseType}\n" +
            $"• Validade: {expText}\n" +
            $"• Status: Autorizado");

        return Result.Succeeded;
    }
}
