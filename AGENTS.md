# AGENTS.md — Diretrizes de Engenharia e Governança para Agentes de IA

Guia de engenharia canônico para agentes autônomos de IA (Antigravity, Claude Code, Cursor, OpenCode, Copilot) que desenvolvem, mantêm ou refatoram código dentro do repositório **`nodeaec/revit-plugins`**.

Repositório oficial: [github.com/nodeaec/revit-plugins](https://github.com/nodeaec/revit-plugins)

---

## 🎯 Escopo e Missão do Repositório

O repositório `revit-plugins` hospeda códigos públicos, SDKs, add-ins de referência e ferramentas comunitárias da **Node.aec** para Autodesk Revit.
Sua meta é acelerar o ecossistema de desenvolvedores AEC/BIM, padronizando a integração com a plataforma Node.aec (licenciamento, catálogo, atualizações) e servindo de referência de engenharia para plugins profissionais.

### Projetos Principais no Repositório

1. **`NodeAec.Connector`**:
   - O **Hub central de governança desktop** e Ribbon unificado da Node.aec para o Autodesk Revit.
   - Gerencia autenticação SSO via navegador (RFC 8252 loopback), sincronização do lease mestre de entitlements (`entitlements.lease`), interface de usuário para ativação manual de chaves e importação de leases offline.
   - Fornece o micro-SDK `NodeAecGate` (`NodeAecGate.Validate(slug)`), permitindo que plugins de terceiros validem direitos em `< 1ms` de forma segura, local e sem chamadas de rede bloqueantes.
   - Gerencia a aba canônica **`Node.aec`** e deduplicação via `Autodesk.Windows.ComponentManager`.

2. **`NodeAec.Licensing.Sample`**:
   - Add-in de demonstração e referência prática para desenvolvedores de plugins Revit.
   - Exemplifica como proteger comandos comerciais (`IExternalCommand`), validar licenças no backend e gerenciar fluxo offline de licenciamento pontual.

> [!NOTE]
> Se o seu objetivo for instruir como integrar o licenciamento Node.aec em um **plugin externo de um usuário**, consulte o guia específico em [`NodeAec.Licensing.Sample/AGENTS.md`](NodeAec.Licensing.Sample/AGENTS.md) ou a skill [`.agents/skills/licensing-integrate`](.agents/skills/licensing-integrate/SKILL.md). Este arquivo atual rege o desenvolvimento **interno deste repositório**.

---

## 🧩 Habilidades Modulares (.agents/skills/)

Este repositório disponibiliza habilidades modulares especializadas para agentes autônomos de IA. Invoque a skill correspondente ao objetivo da tarefa:

### 1. Domínio & Ferramentas Revit
| Skill | Escopo e Gatilhos de Ativação | Caminho Canônico |
|---|---|---|
| **`licensing-integrate`** | Integrar licenciamento Node.aec em plugins novos ou existentes, rodar Fase de Grilling, proteger comandos comerciais (`IExternalCommand`). | [`.agents/skills/licensing-integrate`](.agents/skills/licensing-integrate/SKILL.md) |
| **`ribbon-guard`** | Criar/modificar painéis e botões da Ribbon, garantir aba `Node.aec`, ícones não-bloqueantes e deduplicação via AdWindows. | [`.agents/skills/ribbon-guard`](.agents/skills/ribbon-guard/SKILL.md) |
| **`revit-build-validate`** | Compilar via `dotnet build`, garantir 0 erros, isolar DLLs do RevitAPI e validar dependências do runtime. | [`.agents/skills/revit-build-validate`](.agents/skills/revit-build-validate/SKILL.md) |
| **`release-pack`** | Empacotar releases `.zip` via `release.ps1`, verificar SHA-256 e instalar no Revit local (`%ProgramData%`). | [`.agents/skills/release-pack`](.agents/skills/release-pack/SKILL.md) |

### 2. Engenharia de Software, Qualidade & Workflow
| Skill | Escopo e Gatilhos de Ativação | Caminho Canônico |
|---|---|---|
| **`clean-code-and-oop`** | Padrões de Clean Code em C#, SOLID, SRP, early returns e desacoplamento entre UI do Revit e regras de domínio. | [`.agents/skills/clean-code-and-oop`](.agents/skills/clean-code-and-oop/SKILL.md) |
| **`document-touched-code`** | Documentação XML (`/// <summary>`, `<param>`, `<returns>`) em membros C# e comentários explicativos de intenção. | [`.agents/skills/document-touched-code`](.agents/skills/document-touched-code/SKILL.md) |
| **`security-defense-and-mitigation`** | Criptografia Ed25519 SPKI, DPAPI (`CurrentUser`), machine lock SHA-256, HTTPS e proteção fail-closed. | [`.agents/skills/security-defense-and-mitigation`](.agents/skills/security-defense-and-mitigation/SKILL.md) |
| **`code-review`** | Revisão de código em dois eixos (Padrões do Revit + Especificação funcional) com subagentes auditores. | [`.agents/skills/code-review`](.agents/skills/code-review/SKILL.md) |
| **`test-first-delivery`** | Desenvolvimento orientado a testes (IV-TDD) em C# para lógica headless sem dependência da UI do Revit. | [`.agents/skills/test-first-delivery`](.agents/skills/test-first-delivery/SKILL.md) |
| **`git-change-workflow`** | Estratégia de branches (Fast Track vs Planned Track), commits atômicos e inspeção antes de staging. | [`.agents/skills/git-change-workflow`](.agents/skills/git-change-workflow/SKILL.md) |
| **`semantic-commit`** | Formatar e executar commits semânticos padronizados com escopos de plugins Revit (`<type>(<scope>): <summary>`). | [`.agents/skills/semantic-commit`](.agents/skills/semantic-commit/SKILL.md) |

---

## 🏗️ Stack Tecnológica e Runtimes

- **Linguagem**: C# 12
- **Framework Target**: .NET 8.0 Windows (`net8.0-windows`)
- **Host Application**: Autodesk Revit 2026 (compatível com Revit 2025+)
- **Interface Gráfica**: WPF (`UseWPF = true`), código limpo em C# com layouts nativos
- **Proteção de Dados**: Windows DPAPI (`System.Security.Cryptography.ProtectedData`)
- **Criptografia Assimétrica**: Ed25519 (EdDSA / RFC 8032) para validação offline de leases assinados
- **Build System**: .NET CLI (`dotnet build`, `dotnet test`) e scripts de empacotamento em PowerShell (`scripts/release.ps1`)

---

## 🛡️ Regras de Engenharia do Repositório

### 1. Ribbon do Revit: Aba Canônica Obrigatória `Node.aec`
- **Aba Única**: Todas as ferramentas, add-ins e componentes criados neste repositório **DEVEM** ser adicionados exclusivamente na aba **`Node.aec`** (`TabName = "Node.aec"`).
- **Sem Abas Fragmentadas**: Nunca crie abas separadas para plugins individuais. Organize os recursos em painéis temáticos dentro de `Node.aec` (ex.: `"Conector"`, `"Licenciamento"`, etc.).
- **Deduplicação de Abas**: Utilize os hooks do `Autodesk.Windows.ComponentManager` (AdWindows) para evitar abas duplicadas ou painéis fantasmas ao recarregar add-ins.

### 2. Dependências e Binários do Revit
- **Nunca Copiar DLLs do Revit**: Referências para `RevitAPI.dll`, `RevitAPIUI.dll` e `AdWindows.dll` devem ter sempre `<Private>false</Private>`.
- **Assemblies de Dependência**: Pacotes NuGet adicionais (como `System.Security.Cryptography.ProtectedData.dll`) devem ser empacotados no diretório do add-in usando `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>`.
- **KISS & Zero Inchaço**: Priorize bibliotecas da BCL do .NET 8 e namespaces padrão do Revit. Evite bibliotecas pesadas de terceiros (como Newtonsoft.Json — utilize `System.Text.Json`).

### 3. Integração com a Plataforma Node.aec
- **Endpoint Fixo de Produção**: Todas as chamadas para a API Node.aec utilizam o endpoint oficial de produção: `https://api.nodeaec.com.br`.
- **Segurança de Chaves**: NUNCA armazene chaves privadas no código do cliente. O repositório lida apenas com a chave pública SPKI (`DefaultPublicKeyPem`) para verificação de assinaturas Ed25519.
- **Não-Bloqueante (UI Thread Safe)**: Nenhuma chamada de rede ou I/O pesado deve ser executada de forma síncrona na thread principal do Revit (`OnStartup` ou início de comando). Inicializações de background devem ser assíncronas e à prova de falhas de rede.

### 4. Estrutura de Código e Estilo
- **Clean Code & OOP**: Classes coesas, métodos concisos com responsabilidade única e tratamento explícito de exceções.
- **Tipos Anuláveis**: `<Nullable>enable</Nullable>` ativado. Resolva warnings de possível referência nula de forma segura.
- **Documentação de Código**: Mantenha comentários XML (`/// <summary>`) em classes públicas, métodos de extensão e interfaces.

---

## 🛠️ Comandos de Build e Validação

Todas as alterações devem ser validadas compilando a solution relevante e verificando ausência de erros:

```powershell
# 1. Node.aec Connector (Hub Central)
# Compilar e rodar testes unitários headless
dotnet build NodeAec.Connector\NodeAec.Connector.sln -c Release
dotnet test NodeAec.Connector\NodeAec.Connector.sln -c Release

# Empacotar e instalar no Revit 2026 local
powershell -ExecutionPolicy Bypass -File NodeAec.Connector\scripts\release.ps1 -Version 0.1.1 -Install

# 2. NodeAec.Licensing.Sample (Add-in de Exemplo)
dotnet build NodeAec.Licensing.Sample\NodeAec.Licensing.Sample.sln -c Release
powershell -ExecutionPolicy Bypass -File NodeAec.Licensing.Sample\scripts\release.ps1 -Version 1.0.0 -Install
```

Critérios de Aceite para Modificações:
- Compilação limpa: **0 Erros**.
- Testes unitários: 100% passando.
- Nenhum assembly do Revit (`RevitAPI*.dll`) dentro da pasta `release/` ou `stage/`.
- A DLL `System.Security.Cryptography.ProtectedData.dll` deve estar presente no payload final do add-in.

---

## 📦 Convenções de Git e Commits

- Utilize o padrão Conventional Commits:
  - `feat(connector): ...`
  - `feat(licensing): ...`
  - `fix(ribbon): ...`
  - `docs(readme): ...`
  - `refactor(client): ...`
- Mantenha commits atômicos, focados e sem arquivos temporários de build (`bin/`, `obj/`, `.vs/`).
