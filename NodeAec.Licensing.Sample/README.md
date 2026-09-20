# Node.aec // Licensing Sample & Guia de Integração

Plugin de referência e guia didático de integração com o **Sistema de Licenciamento da Node.aec** para Autodesk Revit (.NET 8 / Revit 2026).

Repositório oficial: [github.com/nodeaec/revit-plugins](https://github.com/nodeaec/revit-plugins)

---

## 🎯 Sobre Este Plugin de Exemplo

O `NodeAec.Licensing.Sample` é uma implementação mínima, limpa e funcional projetada para demonstrar como criadores de software e add-ins para Autodesk Revit podem monetizar, proteger e distribuir suas ferramentas comerciais na **Node.aec Store**.

### O que este exemplo implementa:
1. **Ribbon Canônica**: Cria a aba **`Node.aec`** > Painel **`Licenciamento`** > Botão **`Gerenciador de Licença`** com o ícone oficial da plataforma.
2. **Interface WPF Moderna**: Janela limpa com verificação em tempo real, status da licença, cotas de postos (*seats*), resolução automática do nome do produto no catálogo e link direto para o site.
3. **Criptografia Assimétrica Ed25519 (RFC 8032)**: Validação offline instantânea e segura sem dependência de internet.
4. **Proteção de Hardware via Windows DPAPI**: Cofre local criptografado para tokens e chaves atrelado ao usuário do Windows.
5. **Prevenção de Abas Duplicadas**: Tratamento de hooks do AdWindows (`ComponentManager`) para garantir estabilidade da Ribbon no Revit.

---

## 💡 Como Funciona o Modelo de Licenciamento

O modelo de licenciamento da Node.aec combina máxima segurança com a melhor experiência de uso:

- **Ativação Online Descomplicada**: O cliente insere a chave (`NAEC-XXXX-XXXX-XXXX-XXXX`). A API de produção valida a chave, controla a cota de máquinas ativas (*seats*) e retorna um **Lease Token** criptografado.
- **Tolerância Offline por 30 Dias (Ed25519)**: O lease token é assinado digitalmente pelo servidor com **Ed25519**. O plugin valida a assinatura localmente com a chave pública SPKI embutida, permitindo trabalho ininterrupto em canteiros de obra ou viagens sem conexão.
- **Machine Lock Inviolável**: O lease token é amarrado criptograficamente ao identificador de hardware exclusivo da estação (`MachineGuid` do Windows). O arquivo não pode ser clonado para outro computador.
- **Cofre Local Criptografado (Windows DPAPI)**: Os tokens e metadados são salvos em `%AppData%\NodeAec\Licenses\` protegidos pelo subsistema `ProtectedData` do Windows, acessível unicamente pelo usuário logado.
- **Heartbeat Transparente**: Em segundo plano, sem travar a navegação do Revit, o plugin renova a validade do lease periodicamente quando há conexão com a internet.
- **Liberação de Vagas**: Se o usuário precisar trocar de computador, basta clicar em **"Desativar Posto"** na janela do gerenciador para liberar o assento instantaneamente no servidor.

---

## 🤖 Como Usar Agentes de IA para Auto-Configurar a Integração

Se você utiliza assistentes de programação ou agentes autônomos de IA (**Antigravity**, **Cursor**, **Claude Code**, **GitHub Copilot**, **OpenCode**), você pode delegar a integração completa do licenciamento ao seu agente com zero esforço manual.

O arquivo [`AGENTS.md`](AGENTS.md) dentro desta pasta contém o guia canônico com a entrevista de alinhamento técnico (*Fase de Grilling*) e as receitas de código prontas.

### Prompt para Copiar e Enviar ao seu Agente de IA:

Cole o prompt abaixo no chat do seu assistente de IA dentro da pasta do seu plugin:

```text
Você é o meu engenheiro sênior de integração.
Por favor, leia as instruções e o protocolo de integração definidos em:
https://raw.githubusercontent.com/nodeaec/revit-plugins/main/NodeAec.Licensing.Sample/AGENTS.md

Antes de escrever qualquer código, execute a Fase de Grilling descrita no documento: faça-me as perguntas de alinhamento técnico (versão do Revit, runtime .NET, resolução de referências da RevitAPI, slug do produto e política de bloqueio) fornecendo sua recomendação para cada item.

Lembre-se das regras inegociáveis:
1. Use SEMPRE a API oficial de produção da Node.aec (https://api.nodeaec.com.br).
2. Integre o plugin OBRIGATORIAMENTE na aba "Node.aec" do Revit (se o meu plugin já possuir painéis ou abas próprias, certifique-se de que tudo fique alocado na aba "Node.aec").

Após o meu alinhamento, integre o cliente de licenciamento Node.aec no meu plugin de forma limpa, não-bloqueante e segura.
```

O agente fará uma breve entrevista focada nas particularidades do seu build e aplicará as alterações de forma determinística no seu `.csproj`, `App.cs` e nos comandos a proteger.

---

## 🚀 Integração Manual Passo a Passo (para Desenvolvedores)

Se você preferir realizar a integração manualmente no seu projeto C#, siga o roteiro abaixo:

### 1. Copie os Arquivos Essenciais
Copie da pasta [`src/NodeAec.Licensing.Sample/`](src/NodeAec.Licensing.Sample/) para o seu projeto:
- `Client/NodeAecLicenseClient.cs`: Motor de comunicação HTTP, assinatura Ed25519 e persistência DPAPI.
- `Config/LicenseConfig.cs`: Configuração central (URL da API de produção, chaves públicas e caminhos).
- `Commands/ManageLicenseCommand.cs`: Comando que abre a janela de gerenciamento.
- `UI/LicenseManagerWindow.cs`: Interface WPF pronta com status, nome do produto e links.
- `Resources/`: Ícones oficiais da Node.aec para a Ribbon e barra de título.

### 2. Configure o Arquivo de Projeto (`.csproj`)
Adicione a dependência do Windows DPAPI e a cópia de dependências locais:

```xml
<ItemGroup>
  <!-- Necessário para proteção do lease via Windows DPAPI -->
  <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" />
</ItemGroup>

<PropertyGroup>
  <!-- Garante que todas as DLLs de dependências acompanhem o plugin no build -->
  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
</PropertyGroup>
```

### 3. Inicialização na Aba `Node.aec` (`App.cs`)
No método `OnStartup` da sua classe `IExternalApplication`:

```csharp
using Autodesk.Revit.UI;
using NodeAec.Licensing.Client;
using NodeAec.Licensing.Sample.Config;

public class App : IExternalApplication
{
    private NodeAecLicenseClient? _licenseClient;

    public Result OnStartup(UIControlledApplication application)
    {
        // 1. OBRIGATÓRIO: Utilizar sempre a aba oficial "Node.aec"
        var tabName = "Node.aec";
        try { application.CreateRibbonTab(tabName); } catch { }

        // Cria o painel de Licenciamento
        var panel = application.CreateRibbonPanel(tabName, "Licenciamento");
        var btnManage = new PushButtonData(
            "NodeAec_ManageLicense",
            "Gerenciador\nde Licença",
            typeof(App).Assembly.Location,
            "MeuNamespace.Commands.ManageLicenseCommand"
        );
        panel.AddItem(btnManage);

        // 2. Heartbeat e validação transparente em segundo plano (não bloqueia o Revit)
        _licenseClient = LicenseConfig.CreateClient();
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await _licenseClient.ValidateLicenseAsync(allowOffline: true);
            }
            catch { }
        });

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        _licenseClient?.Dispose();
        return Result.Succeeded;
    }
}
```

### 4. Protegendo Comandos Comerciais (`IExternalCommand`)
Em cada comando comercial que você deseja restringir:

```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Licensing.Sample.Config;
using NodeAec.Licensing.Sample.UI;

[Transaction(TransactionMode.Manual)]
public class MeuComandoComercial : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // Validação rápida (prioriza cache local assinado offline)
        using var client = LicenseConfig.CreateClient();
        var check = client.ValidateLicenseAsync(allowOffline: true).GetAwaiter().GetResult();

        if (!check.IsValid)
        {
            var dialog = new TaskDialog("Node.aec // Licença Necessária")
            {
                MainInstruction = "Licença ativa necessária para executar este recurso.",
                MainContent = check.ErrorMessage ?? "Ative o produto para utilizar as ferramentas completas.",
                CommonButtons = TaskDialogCommonButtons.Close
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Abrir Gerenciador de Licença...");

            if (dialog.Show() == TaskDialogResult.CommandLink1)
            {
                LicenseManagerWindow.Open(commandData.Application);
            }

            return Result.Cancelled;
        }

        // --- EXECUÇÃO NORMAL DO SEU COMANDO ---
        TaskDialog.Show("Executando", $"Ferramenta licenciada para: {check.ProductName}");
        return Result.Succeeded;
    }
}
```

---

## 🛠️ Compilação e Instalação no Revit Local

Para compilar e testar este sample diretamente na sua máquina Windows:

```powershell
# 1. Compilação da Solution em Release (.NET 8)
dotnet build NodeAec.Licensing.Sample.sln -c Release

# 2. Empacotamento em arquivo zip distribuível
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 1.0.0

# 3. Empacotamento + Instalação automática no Revit 2026 local
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 1.0.0 -Install
```
