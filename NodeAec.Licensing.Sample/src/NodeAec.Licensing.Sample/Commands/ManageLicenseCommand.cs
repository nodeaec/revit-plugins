using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Licensing.Sample.UI;

namespace NodeAec.Licensing.Sample.Commands;

/// <summary>
/// Opens the interactive Node.aec License Manager WPF window.
/// Allows viewing status, activating keys, copying Machine ID, validating heartbeats, and deactivating seats.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ManageLicenseCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            LicenseManagerWindow.Open(commandData.Application);
            return Result.Succeeded;
        }
        catch (System.Exception ex)
        {
            TaskDialog.Show("Node.aec", $"Erro ao abrir o Gerenciador de Licença:\n{ex.Message}");
            return Result.Failed;
        }
    }
}
