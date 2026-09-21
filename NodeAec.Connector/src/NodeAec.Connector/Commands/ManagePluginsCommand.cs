using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Connector.UI;

namespace NodeAec.Connector.Commands;

/// <summary>
/// Comando Revit para abrir a janela "Meus Plugins" com os produtos vinculados à conta.
/// Fica indisponível na Ribbon até o login (ver <see cref="RequiresLoginAvailability"/>).
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ManagePluginsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        PluginsWindow.Open(commandData.Application);
        return Result.Succeeded;
    }
}
