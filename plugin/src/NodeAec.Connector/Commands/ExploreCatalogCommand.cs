using System;
using System.Diagnostics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Connector.Config;

namespace NodeAec.Connector.Commands;

/// <summary>
/// Comando Revit para abrir o catálogo oficial de soluções BIM no navegador.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ExploreCatalogCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ConnectorConfig.CatalogUrl,
                UseShellExecute = true
            });
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            TaskDialog.Show("Node.aec Catálogo", $"Não foi possível abrir o navegador: {ex.Message}");
            return Result.Failed;
        }
    }
}
