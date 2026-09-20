# Node.aec Licensing — Guia para Agentes de IA

Este documento é a instrução canônica para agentes autônomos de IA (Antigravity, Cursor, Claude Code, Copilot, OpenCode, etc.) encarregados de integrar o **Sistema de Licenciamento da Node.aec** em plugins Autodesk Revit existentes (.NET 8 para Revit 2025–2026+ ou .NET Framework 4.8 para Revit 2020–2024).

Repositório oficial: [github.com/nodeaec/revit-plugins](https://github.com/nodeaec/revit-plugins)

---

## 🛑 PROTOCOLO OBRIGATÓRIO: Fase de Grilling Pré-Integração

> [!IMPORTANT]
> **NUNCA gere código ou altere o projeto do usuário antes de realizar a Fase de Grilling.**
> Ambientes de desenvolvimento Revit variam drasticamente entre máquinas, versões de API e políticas de build. O agente DEVE conduzir a entrevista abaixo com o usuário, apresentando cada ponto com a resposta recomendada, e aguardar alinhamento antes de modificar arquivos.
>
> **REGRAS FIXAS (NÃO PERGUNTE AO USUÁRIO):**
> 1. **Ambiente da API**: SEMPRE produção (`https://api.nodeaec.com.br`). NUNCA pergunte sobre ambiente dev/local.
> 2. **Aba da Ribbon**: O plugin DEVE SEMPRE ser integrado na aba **`Node.aec`**. NUNCA pergunte onde posicionar o botão ou aba. Inclusive, se o agente tocar no código existente do usuário, **ele deve se certificar de que os painéis e comandos do plugin estejam alocados na aba `Node.aec`** (criando-a caso não exista ou reutilizando-a).

### Roteiro de Perguntas da Entrevista (Grilling)

#### 1. Versões do Revit e Target Frameworks (.NET)
- **Pergunta**: *"Quais versões do Autodesk Revit este plugin suporta atualmente?"*
- **Contexto**: Revit 2025 e 2026 rodam em **.NET 8** (`net8.0-windows`). Revit 2020 a 2024 rodam em **.NET Framework 4.8** (`net48`).
- **Opções**:
  - `A` (Recomendado para plugins novos): .NET 8 exclusivo (Revit 2025/2026).
  - `B`: .NET Framework 4.8 exclusivo (Revit 2020 a 2024).
  - `C`: Multi-targeting (`<TargetFrameworks>net8.0-windows;net48</TargetFrameworks>`).
- **Ação do Agente**: Configurar o `.csproj` com as diretivas condicionais apropriadas para o runtime escolhido.

#### 2. Resolução das Referências da RevitAPI
- **Pergunta**: *"Como as referências da `RevitAPI.dll` e `RevitAPIUI.dll` estão resolvidas no seu repositório?"*
- **Opções**:
  - `A`: Caminho local padrão (ex.: `C:\Program Files\Autodesk\Revit 2026\RevitAPI.dll` com `<Private>false</Private>`).
  - `B` (Recomendado para CI/CD e equipes): Pacotes NuGet da comunidade (ex.: `Autodesk.Revit.SDK` ou `Revit_All_Main_Versions_API_x64`).
  - `C`: Variável de ambiente compartilhada de build (ex.: `$(RevitInstallDir)`).

#### 3. Slug do Produto na Plataforma Node.aec
- **Pergunta**: *"Qual é o slug cadastrado para este produto no portal Node.aec (`https://nodeaec.com.br/products/{slug}`)? Se ainda não registrou o produto, deseja usar resolução automática de catálogo?"*
- **Recomendação**: Usar o slug exato cadastrado. Caso ainda não exista, configurar fallback universal para que o cliente consulte dinamicamente `GET /products` e identifique o produto.

#### 4. Política de Bloqueio e Proteção de Comandos Comerciais
- **Pergunta**: *"Como os comandos comerciais devem se comportar na ausência de licença ativa?"*
- **Opções**:
  - `A` (Recomendado): **Hard Gate** — O comando exibe um `TaskDialog` amigável com botão direto para "Abrir Gerenciador de Licença..." e cancela a execução.
  - `B`: **Trial / Avaliação** — O comando executa com limitações (ex.: marca d'água, limite de elementos ou dias restantes de avaliação).
  - `C`: **Feature Flagging** — Apenas recursos específicos ou de automação pesada exigem licença ativa.

#### 5. Escopo de Armazenamento Local da Licença (DPAPI)
- **Pergunta**: *"O plugin será usado principalmente em computadores de uso pessoal ou em laboratórios compartilhados com múltiplos logins do Windows?"*
- **Opções**:
  - `A` (Recomendado para estações individuais): `%APPDATA%\NodeAec\Licenses\` com DPAPI `DataProtectionScope.CurrentUser`.
  - `B` (Para laboratórios / múltiplos usuários na mesma máquina): `%PROGRAMDATA%\NodeAec\Licenses\` com DPAPI `DataProtectionScope.LocalMachine`.

---

## 🛡️ Regras Inegociáveis de Arquitetura e Segurança

Ao implementar a integração, o agente DEVE seguir estritamente:

1. **Nunca embutir chaves privadas**: O código do cliente transporta unicamente a **chave pública Ed25519 SPKI** (`DefaultPublicKeyPem`). A chave privada reside exclusivamente no backend da Node.aec.
2. **Nunca travar a thread de interface do Revit**: Nunca utilize chamadas HTTP bloqueantes (`.Result`, `.Wait()`) na thread de UI do Revit, especialmente no `OnStartup`. Inicializações devem ser executadas via `Task.Run` com tratamento silencioso de falhas de rede.
3. **Resiliência Offline-First**: Na verificação dos comandos diários, SEMPRE use `allowOffline: true`. O usuário não pode ser impedido de projetar por instabilidade momentânea na internet. A tolerância offline do lease token Ed25519 é de 30 dias.
4. **Proteção de Tokens com Windows DPAPI**: O lease assinado deve ser persistido criptografado com `ProtectedData.Protect` para impedir extração ou replicação indevida entre usuários.
5. **Machine ID Imutável**: O identificador de máquina deve ser derivado do registro do Windows (`MachineGuid`) concatenado ao `MachineName` via hash SHA-256.
6. **Aba Exclusiva `Node.aec` OBRIGATÓRIA**: O plugin DEVE residir na aba oficial `Node.aec`. Se o código existente do usuário criar abas próprias separadas ou botões em abas genéricas, o agente DEVE migrar e consolidar todos os painéis e comandos do plugin sob a aba `Node.aec`. Nunca crie abas separadas.
7. **Endpoint de Produção Fixo**: A comunicação deve sempre apontar para o endpoint de produção (`https://api.nodeaec.com.br`). Nunca configure nem pergunte ao usuário sobre ambientes de teste ou localhost.

---

## 📋 Receitas de Código para o Agente

### Receita 1: Dependências no `.csproj`

Garanta que o `.csproj` do plugin do usuário contenha:

```xml
<ItemGroup>
  <!-- Necessário para proteção do token criptografado via Windows DPAPI -->
  <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" />

  <!-- IMPORTANTE: Se usar pacotes NuGet da RevitAPI, adicione PrivateAssets e ExcludeAssets
       para evitar que o CopyLocalLockFileAssemblies copie as DLLs do Revit para a saída -->
  <!-- Exemplo:
  <PackageReference Include="Revit_All_Main_Versions_API_x64" Version="2026.0.0" PrivateAssets="all" ExcludeAssets="runtime" />
  -->
</ItemGroup>

<PropertyGroup>
  <!-- Garante que ProtectedData.dll seja copiada para o diretório final do addin -->
  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
</PropertyGroup>
```

### Receita 2: Arquivos a Copiar do Repositório Canônico

Copie a partir de `github.com/nodeaec/revit-plugins` (`NodeAec.Licensing.Sample/src/NodeAec.Licensing.Sample/`):
1. `Client/NodeAecLicenseClient.cs` → Adicionar na pasta `Client/` do projeto alvo.
2. `Config/LicenseConfig.cs` → Adicionar na pasta `Config/` do projeto alvo.
3. `Commands/ManageLicenseCommand.cs` → Adicionar na pasta `Commands/` do projeto alvo.
4. `UI/LicenseManagerWindow.cs` → Adicionar na pasta `UI/` do projeto alvo.
5. `Resources/` → Ícones oficiais da Node.aec para os botões.

### Receita 3: Boilerplate de Inicialização (`App.cs`)

```csharp
using System;
using System.Linq;
using Autodesk.Revit.UI;
using NodeAec.Licensing.Client;
using NodeAec.Licensing.Sample.Config;

public class App : IExternalApplication
{
    private NodeAecLicenseClient? _licenseClient;

    public Result OnStartup(UIControlledApplication application)
    {
        // 1. OBRIGATÓRIO: Utilizar sempre a aba canônica "Node.aec"
        // Se o plugin existente do usuário possuir uma aba própria, migre os comandos/painéis para a aba "Node.aec"
        const string tabName = "Node.aec";
        try { application.CreateRibbonTab(tabName); } catch { }

        // Obtenção defensiva do painel (evita exceções se o painel já tiver sido criado)
        var panel = application.GetRibbonPanels(tabName)
            .FirstOrDefault(p => string.Equals(p.Name, "Licenciamento", StringComparison.OrdinalIgnoreCase))
            ?? application.CreateRibbonPanel(tabName, "Licenciamento");

        const string buttonId = "NodeAec_ManageLicense";
        if (!panel.GetItems().Any(i => i.Name == buttonId))
        {
            var btnManage = new PushButtonData(
                buttonId,
                "Gerenciador\nde Licença",
                typeof(App).Assembly.Location,
                "MeuNamespace.Commands.ManageLicenseCommand"
            )
            {
                ToolTip = "Gerencia sua licença Node.aec (ativação, status e liberação de vagas)."
            };
            panel.AddItem(btnManage);
        }

        // 2. Validação transparente em segundo plano (sem travar a UI)
        _licenseClient = LicenseConfig.CreateClient();
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await _licenseClient.ValidateLicenseAsync(allowOffline: true);
            }
            catch
            {
                // Silencioso em caso de falha de rede
            }
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

### Receita 4: Protegendo um Comando (`IExternalCommand`)

> [!NOTE]
> **Segurança de Thread STA**: O método `Execute` de um `IExternalCommand` roda na thread principal (STA) do Revit. Janelas WPF e `TaskDialog` exigem execução em thread STA. Caso valide licenças em segundo plano (`Task.Run`), faça o despacho para a thread principal de UI antes de exibir qualquer diálogo.

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
        // 1. Verificação rápida da licença (offline-first)
        using var client = LicenseConfig.CreateClient();
        var check = client.ValidateLicenseAsync(allowOffline: true).GetAwaiter().GetResult();

        if (!check.IsValid)
        {
            var dialog = new TaskDialog("Node.aec // Licença Necessária")
            {
                MainInstruction = "Este recurso requer uma licença ativa.",
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

        // 2. Execução da lógica principal do comando comercial
        // ...
        return Result.Succeeded;
    }
}
```

---

## ✅ Checklist de Validação Final para o Agente

Antes de dar a tarefa por concluída, o agente deve validar:

- [ ] Compilação limpa via `dotnet build` (**0 erros**).
- [ ] A DLL `System.Security.Cryptography.ProtectedData.dll` foi empacotada no diretório do add-in.
- [ ] A URL da API aponta para `https://api.nodeaec.com.br` (produção).
- [ ] Todos os comandos e painéis do plugin foram integrados na aba **`Node.aec`** (sem abas separadas, duplicadas ou painéis fantasmas).
- [ ] Ao clicar no comando sem chave ativa, o diálogo abre e direciona para a janela de ativação.
- [ ] Ao ativar com chave válida, o nome real do produto e o link `↗ Ver no site` aparecem corretamente.
- [ ] Desconectar da rede (modo offline) mantém o plugin funcionando normalmente via Ed25519.
