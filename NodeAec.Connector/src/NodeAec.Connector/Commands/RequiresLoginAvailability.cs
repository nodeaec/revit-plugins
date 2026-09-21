using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Connector.Auth;

namespace NodeAec.Connector.Commands;

/// <summary>
/// Disponibilidade de comando: habilita o botão somente quando há uma conta conectada.
/// Usada pelo botão "Meus Plugins" para ficar desabilitado antes do login.
/// Leitura local, sem rede (segura para chamadas frequentes da Ribbon).
/// </summary>
public class RequiresLoginAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
    {
        return LoginRequirement.IsLoggedIn();
    }
}
