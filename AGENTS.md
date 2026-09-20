# Node.aec // Revit Plugins — Agent Guide

Guia de engenharia canônico para agentes autônomos de IA (Antigravity, Claude Code, Codex, Cursor, OpenCode, Copilot) que desenvolvem, mantêm ou refatoram código dentro do repositório **`nodeaec/revit-plugins`**.

Repositório oficial: [github.com/nodeaec/revit-plugins](https://github.com/nodeaec/revit-plugins)

---

## 🎯 Escopo e Missão do Repositório

O repositório `revit-plugins` hospeda códigos públicos, SDKs, add-ins de referência e ferramentas comunitárias da **Node.aec** para Autodesk Revit.
Sua meta é acelerar o ecossistema de desenvolvedores AEC/BIM, padronizando a integração com a plataforma Node.aec (licenciamento, catálogo, atualizações) e servindo de referência de engenharia para plugins profissionais.

> [!NOTE]
> Se o seu objetivo for instruir como integrar o licenciamento Node.aec em um **plugin externo de um usuário**, consulte o guia específico em [`NodeAec.Licensing.Sample/AGENTS.md`](NodeAec.Licensing.Sample/AGENTS.md). Este arquivo atual rege o desenvolvimento **interno deste repositório**.

---

## 🏗️ Stack Tecnológica e Runtimes

- **Linguagem**: C# 12
- **Framework Target**: .NET 8.0 Windows (`net8.0-windows`)
- **Host Application**: Autodesk Revit 2026 (compatível com Revit 2025+)
- **Interface Gráfica**: WPF (`UseWPF = true`), código limpo em C# com layouts nativos
- **Proteção de Dados**: Windows DPAPI (`System.Security.Cryptography.ProtectedData`)
- **Criptografia Assimétrica**: Ed25519 (EdDSA / RFC 8032) para validação offline de leases assinados
- **Build System**: .NET CLI (`dotnet build`) e scripts de empacotamento em PowerShell (`scripts/release.ps1`)

---

## 🛡️ Regras de Engenharia do Repositório

### 1. Ribbon do Revit: Aba Canônica Obrigatória `Node.aec`
- **Aba Única**: Todas as ferramentas, add-ins e componentes criados neste repositório **DEVEM** ser adicionados exclusivamente na aba **`Node.aec`** (`TabName = "Node.aec"`).
- **Sem Abas Fragmentadas**: Nunca crie abas separadas para plugins individuais. Organize os recursos em painéis temáticos dentro de `Node.aec` (ex.: `"Licenciamento"`, `"Automação"`, etc.).
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
# Compilar em modo Release
dotnet build NodeAec.Licensing.Sample\NodeAec.Licensing.Sample.sln -c Release

# Empacotar e instalar no Revit 2026 local
powershell -ExecutionPolicy Bypass -File NodeAec.Licensing.Sample\scripts\release.ps1 -Version 1.0.0 -Install
```

Critérios de Aceite para Modificações:
- Compilação limpa: **0 Erros**.
- Nenhum assembly do Revit (`RevitAPI*.dll`) dentro da pasta `release/` ou `stage/`.
- A DLL `System.Security.Cryptography.ProtectedData.dll` deve estar presente no payload final.

---

## 📦 Convenções de Git e Commits

- Utilize o padrão Conventional Commits:
  - `feat(licensing): ...`
  - `fix(ribbon): ...`
  - `docs(readme): ...`
  - `refactor(client): ...`
- Mantenha commits atômicos, focados e sem arquivos temporários de build (`bin/`, `obj/`, `.vs/`).
