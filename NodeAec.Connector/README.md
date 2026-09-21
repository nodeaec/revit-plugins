# Node.aec Connector — Autodesk Revit Add-in

Add-in central de governança desktop, gerenciamento de licenças e Ribbon unificada para **Autodesk Revit 2026** (compatível com Revit 2025+).

O **Node.aec Connector** atua como o Hub no modelo **Hub & Micro-Gate**: o usuário final realiza login uma única vez no navegador (Browser SSO com loopback local RFC 8252) e tem todos os seus plugins, templates e famílias licenciados e sincronizados automaticamente na estação de trabalho com tolerância de até 30 dias offline.

---

## 🚀 Principais Recursos

- **Aba Canônica `Node.aec`**: Registra e gerencia o painel oficial `Conector` na Ribbon do Revit com botões de acesso rápido e deduplicação automática de abas via `AdWindows`.
- **Browser SSO (OAuth 2.0 Loopback Local — RFC 8252)**: Autenticação moderna e segura com suporte a login com Google e 2FA sem digitação de senhas no Revit.
- **Master Entitlements Lease**: Obtém e renova concessões consolidadas de múltiplos produtos assinadas assimetricamente com Ed25519 pela plataforma.
- **Armazenamento Seguro DPAPI**: O arquivo `%APPDATA%\NodeAec\entitlements.lease` é criptografado com `DataProtectionScope.CurrentUser`.
- **Modo Offline & Air-Gapped**: Entrada manual de chaves (`NAEC-XXXX-...`) ou importação de arquivos de concessão `.lease` assinados para estações isoladas.
- **Micro-SDK `NodeAecGate`**: Classe canônica para plugins parceiros validarem permissão de execução localmente em `< 1ms` e zero requisições de rede.

---

## 🏗️ Estrutura do Projeto

```
NodeAec.Connector/
├── NodeAec.Connector.sln
├── NodeAec.Connector.slnx
├── README.md
├── scripts/
│   └── release.ps1               # Script de build, empacotamento .zip e instalação no Revit
├── src/NodeAec.Connector/
│   ├── NodeAec.Connector.csproj  # Target net8.0-windows, Revit 2026, UseWPF=true
│   ├── NodeAec.Connector.addin   # Manifesto do Revit com AddInId e FullClassName
│   ├── App.cs                    # IExternalApplication: Ribbon Tab, hooks de ciclo de vida
│   ├── Auth/
│   │   └── DesktopAuthService.cs # Loopback listener, porta efêmera e CSRF state
│   ├── Client/
│   │   └── ConnectorApiClient.cs # Cliente HTTP para API Node.aec (/account/entitlements/lease)
│   ├── Config/
│   │   └── ConnectorConfig.cs    # URLs e constantes oficiais
│   ├── Storage/
│   │   └── LeaseStorage.cs       # Gestão do arquivo %APPDATA%\NodeAec\entitlements.lease (DPAPI)
│   ├── Hardware/
│   │   └── HardwareId.cs         # Identificador SHA-256 da máquina
│   ├── Gate/
│   │   └── NodeAecGate.cs        # Micro-SDK de validação para plugins parceiros (< 1ms)
│   ├── Models/
│   │   ├── EntitlementItem.cs    # Modelo de produto concedido
│   │   ├── MasterLeasePayload.cs # Claims do JWT Ed25519
│   │   └── SyncResult.cs         # Resultado de sincronização
│   ├── Commands/
│   │   ├── ManageConnectorCommand.cs # Abre janela do Connector
│   │   ├── LoginCommand.cs           # Dispara login via navegador
│   │   └── ExploreCatalogCommand.cs  # Abre catálogo web
│   ├── UI/
│   │   └── ConnectorWindow.cs    # Interface WPF escura e responsiva
│   └── Resources/
│       ├── nodeaec-16.png
│       └── nodeaec-32.png
└── tests/NodeAec.Connector.Tests/
    ├── NodeAec.Connector.Tests.csproj # Target net8.0 (CI-safe, sem dependência do Revit)
    ├── HardwareIdTests.cs
    ├── DesktopAuthServiceTests.cs
    ├── LeaseStorageTests.cs
    ├── ConnectorApiClientTests.cs
    ├── NodeAecGateTests.cs
    └── TestHelpers.cs
```

---

## 🛠️ Como Compilar e Testar

### Compilar a Solution:
```powershell
dotnet build NodeAec.Connector\NodeAec.Connector.sln -c Release
```

### Executar os Testes Unitários:
```powershell
dotnet test NodeAec.Connector\tests\NodeAec.Connector.Tests\NodeAec.Connector.Tests.csproj
```

### Empacotar e Instalar no Revit 2026:
```powershell
powershell -ExecutionPolicy Bypass -File NodeAec.Connector\scripts\release.ps1 -Version 1.0.0 -Install
```

---

## 🔌 Como Integrar Plugins Parceiros com o `NodeAecGate`

Em comandos do seu plugin (`IExternalCommand`):

```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Connector.Gate;

[Transaction(TransactionMode.Manual)]
public class MeuComandoRevit : IExternalCommand
{
    private const string ProductSlug = "meu-plugin";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // Validação local e instantânea (< 1ms, zero rede)
        var check = NodeAecGate.Validate(ProductSlug);
        if (!check.IsLicensed)
        {
            TaskDialog.Show("Node.aec — Licença Necessária",
                $"O produto '{ProductSlug}' não possui licença ativa nesta estação.\n\n" +
                $"Motivo: {check.Message}\n\n" +
                "Abra o Node.aec Connector na Ribbon para entrar com sua conta ou ativar sua licença.");

            NodeAecGate.OpenConnector();
            return Result.Cancelled;
        }

        // Execução normal da funcionalidade
        TaskDialog.Show("Sucesso", $"Executando com licença {check.LicenseType}.");
        return Result.Succeeded;
    }
}
```
