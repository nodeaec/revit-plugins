# AGENTS.md — Protocolo Canônico de Integração de Licenciamento Node.aec para Agentes de IA

Este guia orienta agentes autônomos de IA (**Antigravity**, **Cursor**, **Claude Code**, **GitHub Copilot**, **OpenCode**) a integrar com precisão cirúrgica o licenciamento do ecossistema **Node.aec** em qualquer plugin para Autodesk Revit.

---

## 🛑 Fase 0: Protocolo Obrigatório de Entrevista Técnica (Grilling Phase)

> [!IMPORTANT]
> **NUNCA gere código ou altere o projeto do usuário antes de realizar esta entrevista.**
> Apresente cada questão de forma clara, contextualizada e com sua **recomendação explícita**, aguardando o alinhamento do usuário.

### Invariantes Inegociáveis (NÃO Pergunte ao Usuário):
1. **Arquitetura Hub & Micro-Gate**: Plugins parceiros **NUNCA** devem implementar clientes HTTP, gerenciar tokens na nuvem ou apresentar telas próprias de login/ativação. A validação de direitos é feita localmente em `< 1ms` via `NodeAecGate`.
2. **Ribbon Exclusiva `Node.aec`**: O plugin DEVE residir na aba canônica **`Node.aec`** do Revit. Se o plugin possuir abas próprias ou botões dispersos, migre todos para a aba `Node.aec` sob um painel temático do plugin.
3. **Zero Tráfego de Rede em Comandos**: Comandos `IExternalCommand` nunca devem realizar chamadas HTTP de validação durante o `Execute` ou no `OnStartup`.
4. **Governança via Node.aec Connector**: Toda autenticação de conta, ativação de chaves e renovação de lease é de responsabilidade exclusiva do add-in **Node.aec Connector**.

---

### Perguntas da Fase de Grilling

#### 1. Versão do Revit & Framework .NET Alvo
- **Pergunta**: *"Quais versões do Autodesk Revit este plugin tem como alvo?"*
- **Contexto**: Revit 2025 e 2026+ exigem **.NET 8** (`net8.0-windows`). Revit 2020 a 2024 utilizam **.NET Framework 4.8** (`net48`).
- **Opções**:
  - `A` (Recomendado para add-ins modernos): .NET 8 exclusivo (`<TargetFramework>net8.0-windows</TargetFramework>`).
  - `B`: .NET Framework 4.8 exclusivo (`<TargetFramework>net48</TargetFramework>`).
  - `C`: Multi-targeting (`<TargetFrameworks>net8.0-windows;net48</TargetFrameworks>`).

#### 2. Slug do Produto no Catálogo Node.aec
- **Pergunta**: *"Qual é o slug cadastrado para este produto no catálogo Node.aec (`https://nodeaec.com.br/products/{slug}`)? Se ainda não estiver cadastrado, qual slug identificador devemos adotar?"*
- **Recomendação**: Usar o identificador kebab-case canônico (ex: `"revit-automator"`, `"parametric-doors"`).

#### 3. Política de Bloqueio em Comandos Não Licenciados
- **Pergunta**: *"Como os comandos comerciais devem reagir caso a estação não possua licença ativa?"*
- **Opções**:
  - `A` (Recomendado): **Hard Gate** — Exibir `TaskDialog` informativo com link direto para abrir o Node.aec Connector e cancelar a execução (`Result.Cancelled`).
  - `B`: **Modo Demonstração** — Permitir execução parcial ou limitada com aviso ao usuário.

---

## 📋 Receitas de Código para o Agente

### Receita 1: Dependências no `.csproj`

Garanta que o `.csproj` do plugin do usuário contenha a biblioteca do DPAPI e a cópia de assemblies:

```xml
<ItemGroup>
  <!-- Necessário para decriptografia do lease mestre via Windows DPAPI -->
  <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" />
</ItemGroup>

<PropertyGroup>
  <!-- Garante cópia de dependências locais do NuGet sem incluir DLLs do Revit -->
  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
</PropertyGroup>
```

### Receita 2: Arquivos a Copiar do Repositório Canônico

Copie da pasta `NodeAec.Licensing.Sample/src/NodeAec.Licensing.Sample/Gate/`:
1. `Gate/NodeAecGate.cs` → Adicionar na pasta `Gate/` do projeto alvo.
2. `Gate/HardwareId.cs` → Adicionar na pasta `Gate/` do projeto alvo.

### Receita 3: Boilerplate de Ribbon na Aba `Node.aec` (`App.cs`)

```csharp
using System;
using System.Linq;
using Autodesk.Revit.UI;

namespace MeuNamespace;

public class App : IExternalApplication
{
    public const string TabName = "Node.aec";
    public const string PanelName = "Meu Produto";

    public Result OnStartup(UIControlledApplication application)
    {
        // 1. Garante que a aba Node.aec exista
        try { application.CreateRibbonTab(TabName); } catch { }

        // 2. Obtém ou cria o painel temático do produto
        var panel = application.GetRibbonPanels(TabName)
            .FirstOrDefault(p => string.Equals(p.Name, PanelName, StringComparison.OrdinalIgnoreCase))
            ?? application.CreateRibbonPanel(TabName, PanelName);

        // 3. Adiciona os botões comerciais do produto
        var btn = new PushButtonData(
            "MeuProduto_ComandoPrincipal",
            "Executar\nFerramenta",
            typeof(App).Assembly.Location,
            "MeuNamespace.Commands.MeuComandoComercial"
        );
        panel.AddItem(btn);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}
```

### Receita 4: Protegendo um Comando Comercial (`IExternalCommand`)

```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MeuNamespace.Gate;

namespace MeuNamespace.Commands;

[Transaction(TransactionMode.Manual)]
public class MeuComandoComercial : IExternalCommand
{
    private const string ProductSlug = "meu-produto-slug";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // 1. Validação local ultrarrápida (< 1ms, zero rede)
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

        // 2. Execução normal da regra de negócio do comando
        // ...
        return Result.Succeeded;
    }
}
```

---

## ✅ Checklist de Validação Final para o Agente

- [ ] Compilação limpa via `dotnet build` (**0 erros**).
- [ ] Apenas o micro-SDK `NodeAecGate` e `HardwareId` foram incluídos (sem clientes HTTP ou formulários duplicados de login/ativação).
- [ ] O painel e os comandos do produto estão localizados na aba **`Node.aec`**.
- [ ] Ao invocar o comando sem licença ativa na máquina, o `TaskDialog` é exibido com o botão direcionando para o **Node.aec Connector**.
- [ ] Ao ter o lease ativo com o produto correspondente, o comando executa sem atrasos de rede.
