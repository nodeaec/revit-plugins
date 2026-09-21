using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Connector.UI;

namespace NodeAec.Connector.Commands;

/// <summary>
/// Comando Revit para abrir o painel unificado do Node.aec Connector.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ManageConnectorCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        ConnectorWindow.Open(commandData.Application);
        return Result.Succeeded;
    }
}
