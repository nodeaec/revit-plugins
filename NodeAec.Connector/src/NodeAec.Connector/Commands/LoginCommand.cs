using System;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Connector.Auth;
using NodeAec.Connector.Client;
using NodeAec.Connector.Storage;
using NodeAec.Connector.UI;

namespace NodeAec.Connector.Commands;

/// <summary>
/// Comando Revit para iniciar autenticação direta via SSO no navegador e sincronizar licenças.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class LoginCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var authService = new DesktopAuthService();
        Task.Run(async () =>
        {
            try
            {
                string token = await authService.LoginViaBrowserAsync().ConfigureAwait(false);
                using var client = new ConnectorApiClient();
                var result = await client.SyncMasterEntitlementsAsync(token).ConfigureAwait(false);

                if (result.Success)
                {
                    var payload = LeaseStorage.ParseJwtPayload(result.LeaseToken ?? string.Empty);
                    LeaseStorage.SaveSession(payload?.Sub, token);
                }
            }
            catch
            {
                // Silencioso ou tratado pelo fluxo da UI
            }
        });

        // Abre a janela para feedback visual imediato
        ConnectorWindow.Open(commandData.Application);

        return Result.Succeeded;
    }
}
