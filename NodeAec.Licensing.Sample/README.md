# Node.aec // Exemplo Canônico de Integração de Licenciamento

Implementação de referência para desenvolvedores e parceiros que desejam proteger e comercializar add-ins para **Autodesk Revit** (compatível com Revit 2025/2026+ em .NET 8) utilizando o ecossistema **Node.aec**.

---

## 🏛️ Nova Arquitetura: Hub & Micro-Gate

A partir da versão 2.0 da plataforma Node.aec, os plugins parceiros **não precisam mais** implementar clientes HTTP, gerenciar tokens na nuvem, lidar com formulários de login ou exibir janelas complexas de ativação de chaves.

Toda a governança de contas, login SSO via navegador (Google/Email), ativações de estações e sincronização de concessões (*leases*) é realizada de forma centralizada pelo **Node.aec Connector**.

```
+-----------------------------------------------------------------------------+
|                               Autodesk Revit                                |
|                                                                             |
|  [ Ribbon Tab: "Node.aec" ]                                                 |
|                                                                             |
|  +---------------------------+       +-----------------------------------+  |
|  |     Node.aec Connector    |       |        Plugins Parceiros          |  |
|  |     (Hub Centralizado)    |       |     (ex: Revit Automator)         |  |
|  |                           |       |                                   |  |
|  | - Browser SSO (RFC 8252)  |       |  public Result Execute(...)       |  |
|  | - Gestão de assentos/PC   |       |  {                                |  |
|  | - Heartbeat em background |       |      var chk = NodeAecGate        |  |
|  | - Criptografia DPAPI      |       |                .Validate(slug);   |  |
|  +-------------+-------------+       |      if (!chk.IsLicensed)         |  |
|                |                     |          return Result.Cancelled; |  |
|                | Grava Master Lease  |  }                                |  |
|                v (DPAPI + Ed25519)   +-----------------+-----------------+  |
|         +----------------------------------------------+                    |
|         | %APPDATA%\NodeAec\entitlements.lease                              |
+---------+-------------------------------------------------------------------+
          |                                               
   HTTPS  | Heartbeat / Master Lease Sync         
   (Sync) | (quando conectado à internet)                 
          v                                               
+-----------------------------------------------------------------------------+
|                        API Node.aec (Nuvem)                                 |
|                     https://api.nodeaec.com.br                              |
+-----------------------------------------------------------------------------+
```

### Principais Vantagens para o Desenvolvedor:
- **Zero Tráfego de Rede (< 1ms)**: A validação em comandos do Revit é instantânea e local. Não há latência ou risco de travar a interface gráfica do Revit durante a inicialização ou execução de ferramentas.
- **Tolerância Offline por 30 Dias**: O token mestre armazenado em `%APPDATA%\NodeAec\entitlements.lease` garante que o usuário trabalhe desconectado sem interrupções.
- **Machine Binding Inviolável**: Vinculação de hardware via SHA-256 (`MachineGuid` do Windows).
- **Sem Telas Duplicadas**: O usuário tem uma única central de login e ativação na Ribbon (o painel `Conector` da aba `Node.aec`), garantindo experiência uniforme e profissional.

---

## 🤖 Como Usar Agentes de IA para Integrar no seu Plugin

Se você utiliza agentes de IA (**Antigravity**, **Cursor**, **Claude Code**, **GitHub Copilot**, **OpenCode**), basta apontar para o guia canônico deste repositório:

Cole o prompt abaixo no seu assistente dentro da pasta do seu plugin:

```text
Você é meu engenheiro sênior de integração AEC.
Por favor, consulte o guia canônico de integração em:
https://raw.githubusercontent.com/nodeaec/revit-plugins/main/NodeAec.Licensing.Sample/AGENTS.md

Integre o licenciamento Node.aec no meu plugin utilizando a arquitetura simplificada Hub & Micro-Gate (NodeAecGate).
Regras inegociáveis:
1. Copie o micro-SDK NodeAecGate e HardwareId para o meu projeto.
2. Posicione todos os comandos comerciais na aba canônica "Node.aec" do Revit.
3. Proteja os comandos usando NodeAecGate.Validate(slug), cancelando a execução e direcionando para o Node.aec Connector se a licença não estiver ativa.
4. Não crie clientes HTTP nem janelas de login próprias.
```

---

## 🚀 Integração Manual Passo a Passo

### 1. Dependências no `.csproj`
Adicione o pacote do Windows DPAPI e a diretiva de cópia de dependências locais:

```xml
<ItemGroup>
  <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" />
</ItemGroup>

<PropertyGroup>
  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
</PropertyGroup>
```

### 2. Copie o Micro-SDK
Copie os arquivos da pasta [`src/NodeAec.Licensing.Sample/Gate/`](src/NodeAec.Licensing.Sample/Gate/) para o seu plugin:
- `NodeAecGate.cs`: Classe estática com o método `Validate(string productSlug)`.
- `HardwareId.cs`: Gerador do identificador estável de hardware via SHA-256.

### 3. Registro na Ribbon na Aba Canônica `Node.aec` (`App.cs`)
No método `OnStartup` da sua classe `IExternalApplication`, adicione seus comandos sob a aba **`Node.aec`** em um painel temático do seu produto:

```csharp
using Autodesk.Revit.UI;

public class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        const string tabName = "Node.aec";
        try { application.CreateRibbonTab(tabName); } catch { }

        // Cria o painel próprio do seu produto
        var panel = application.CreateRibbonPanel(tabName, "Minhas Ferramentas");
        
        var btn = new PushButtonData(
            "MeuPlugin_Comando",
            "Executar\nComando",
            typeof(App).Assembly.Location,
            "MeuNamespace.Commands.MeuComandoComercial"
        );
        panel.AddItem(btn);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}
```

### 4. Protegendo um Comando Comercial (`IExternalCommand`)
No método `Execute` de cada comando protegido:

```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Licensing.Sample.Gate;

[Transaction(TransactionMode.Manual)]
public class MeuComandoComercial : IExternalCommand
{
    private const string ProductSlug = "meu-produto-slug";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // 1. Validação local instantânea (< 1ms, zero rede)
        var check = NodeAecGate.Validate(ProductSlug);
        if (!check.IsLicensed)
        {
            var dialog = new TaskDialog("Node.aec // Licença Necessária")
            {
                MainInstruction = "Licença ativa necessária para executar esta ferramenta.",
                MainContent = $"{check.Message}\n\nAbra o Node.aec Connector na Ribbon para entrar com sua conta ou ativar sua licença.",
                CommonButtons = TaskDialogCommonButtons.Close
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir Node.aec Connector...");

            if (dialog.Show() == TaskDialogResult.CommandLink1)
            {
                NodeAecGate.OpenConnector();
            }

            return Result.Cancelled;
        }

        // 2. Execução normal do seu comando
        TaskDialog.Show("Sucesso", $"Executando recurso comercial ({check.LicenseType}).");
        return Result.Succeeded;
    }
}
```

---

## 🛠️ Compilação e Testes

```powershell
# Compilar a solution
dotnet build NodeAec.Licensing.Sample.sln -c Release

# Executar testes unitários headless (CI-safe)
dotnet test NodeAec.Licensing.Sample.sln -c Release

# Empacotar zip e instalar no Revit local
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 1.0.0 -Install
```
