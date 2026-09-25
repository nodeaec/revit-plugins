# Node.aec Connector — Autodesk Revit Add-in

Add-in central de governança desktop, gerenciamento de licenças e Ribbon unificada para **Autodesk Revit 2026** (compatível com Revit 2025+).

O **Node.aec Connector** atua como o Hub no modelo **Hub & Micro-Gate**: o usuário final realiza login uma única vez no navegador (Browser SSO com loopback local RFC 8252) e tem todos os seus plugins, templates e famílias licenciados e sincronizados automaticamente na estação de trabalho com tolerância de até 30 dias offline.

📖 **Documentação**: [Manual do Usuário](docs/USER_MANUAL.md) · [Contrato da API de Licenciamento](docs/licensing-api.md)

---

## 🚀 Principais Recursos

- **Aba Canônica `Node.aec`**: Registra e gerencia o painel oficial `Conector` na Ribbon do Revit com botões de acesso rápido e deduplicação automática de abas via `AdWindows`.
- **Browser SSO (OAuth 2.0 Loopback Local — RFC 8252)**: Autenticação moderna e segura com suporte a login com Google e 2FA sem digitação de senhas no Revit.
- **Master Entitlements Lease**: Obtém e renova concessões consolidadas de múltiplos produtos, com verificação Ed25519 (RFC 8032) da assinatura **antes** de confiar em qualquer claim.
- **Verificação com JWKS**: A chave pública é obtida de `GET /license/jwks`, cacheada em `%APPDATA%\NodeAec\license-jwks.json` e opcionalmente fixada via `NODEAEC_LICENSE_PUBLIC_KEY_SPKI`. Sem chave disponível, o gate falha fechado.
- **Armazenamento Seguro DPAPI**: O arquivo `%APPDATA%\NodeAec\entitlements.lease` é criptografado com `DataProtectionScope.CurrentUser`; falha de DPAPI em Windows não degrada para texto puro.
- **Modo Offline & Air-Gapped**: Entrada manual de chaves (`NAEC-XXXX-...`). A importação de arquivos `.lease` está **adiada para uma iteração futura** e o link correspondente foi **removido da UI** (o formato de exportação/troca ainda não é um contrato estável).
- **Micro-SDK `NodeAecGate`**: Classe canônica para plugins parceiros validarem permissão de execução localmente — sem requisições de rede no caminho crítico, em poucos milissegundos.
- **Diagnóstico Local**: Erros de API mapeados para códigos estáveis e log sanitizado em `%APPDATA%\NodeAec\connector.log` (rotação de 512 KB, sem tokens).

---

## 🏛️ Arquitetura: Hub & Micro-Gate

Em vez de cada plugin parceiro implementar um cliente HTTP próprio, apresentar telas de
ativação, solicitar chaves individuais (`NAEC-XXXX-...`) e gerenciar criptografia de máquina,
o Node.aec concentra tudo num **Hub** e entrega aos plugins um **Micro-Gate** local:

```
+--------------------------------------------------------------------------+
|                              Autodesk Revit                              |
|                                                                          |
|  [ Aba "Node.aec" ]                                                      |
|                                                                          |
|  +---------------------------+      +----------------------------------+ |
|  |   Node.aec Connector      |      |      Plugins Parceiros           | |
|  |      (Hub central)        |      |   (Revit Automator, Portas, ...) | |
|  |                           |      |                                  | |
|  |  - Browser SSO (loopback) |      |    public Result Execute(...)    | |
|  |  - Master Entitlements    |      |    {                             | |
|  |    Lease + heartbeat      |      |      var r = NodeAecGate         | |
|  |  - Armazenamento DPAPI    |      |              .Validate(slug);    | |
|  |  - JWKS / Ed25519         |      |      if (!r.IsLicensed)          | |
|  |  - Dedup. de abas         |      |          return Result.Cancelled;| |
|  +---------------------------+      |      }                           | |
|                                     +----------------------------------+ |
|   ^                                 | leitura local do plugin, < 1 ms    |
|   | lease assinado, em DPAPI        | sem nenhuma chamada de rede        |
|   | em %APPDATA%\NodeAec\           |                                    |
|   | entitlements.lease              |                                    |
|                                                                          |
|   ^                                 |                                    |
|   | HTTPS                           | abre o navegador padrão            |
|   v                                 v                                    |
|   https://api.nodeaec.com.br        https://nodeaec.com.br               |
+--------------------------------------------------------------------------+
```

**O que o Hub absorve para o plugin parceiro**

1. **Nenhuma infraestrutura de rede no plugin** — o parceiro não escreve um cliente HTTP nem
   conhece endpoints, tokens de sessão ou formatos de resposta.
2. **Nenhuma UI de ativação própria** — login, chave manual e gestão de assentos vivem nas
   janelas *Minha Conta* e *Meus Plugins*.
3. **Nenhuma gestão de criptografia** — o Hub grava o lease com DPAPI `CurrentUser` e falha
   fechado; o plugin só lê.
4. **Uma única autenticação** — o usuário entra uma vez e todos os produtos da conta são
   sincronizados juntos.
5. **Validação local em < 1 ms** — `NodeAecGate` é puro CPU, sem I/O de rede no caminho
   crítico, com tolerância de 30 dias offline.
6. **Ribbon unificada** — tudo acontece na aba canônica `Node.aec`, sem abas fragmentadas.

> Contrato HTTP consumido pelo Hub (endpoints, payloads, claims e códigos de erro):
> [docs/licensing-api.md](docs/licensing-api.md).

---

## 🔄 Fluxo de Licença e Heartbeat

| # | Quando | O que acontece |
|---|---|---|
| 1 | Usuário clica em **Entrar com minha conta** | O Connector escolhe uma porta efêmera livre, escuta em `127.0.0.1` e abre `https://nodeaec.com.br/auth/desktop?port=…&state=…` no navegador padrão (120 s de timeout, `state` anti-CSRF). |
| 2 | Login concluído no navegador | O portal redireciona para `http://127.0.0.1:<porta>/callback?token=…&state=…`; o listener valida o `state` e devolve a página *Login Concluído*. |
| 3 | Janela dispara a sincronização | `POST /account/entitlements/lease` devolve o **Master Entitlements Lease** assinado em Ed25519. |
| 4 | Resposta recebida | `GET /license/jwks` atualiza o cache de chaves públicas, depois o lease é gravado em `%APPDATA%\NodeAec\entitlements.lease` com DPAPI. Gravação falhou ⇒ erro ao usuário, sem texto puro. |
| 5 | Abertura do Revit (sempre) | Heartbeat em `Task.Run`: `POST /license/validate` renova o lease; falha de rede é silenciosa e fica em `connector.log`. |
| 6 | Plugin parceiro executa | `NodeAecGate.Validate(slug)` verifica assinatura → `iss` → `scope` → `iat` → `mid` → `exp` → `slug` e libera ou bloqueia — **sem rede**. |
| 7 | Sem internet | O lease local vale até o `exp` emitido pela API (padrão de 30 dias); a cada abertura do Revit a validade é tentativamente estendida. |

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
│   ├── NodeAec.Connector.csproj  # Matriz RevitYear (Directory.Build.props), UseWPF=true
│   ├── NodeAec.Connector.addin   # Manifesto do Revit com AddInId e FullClassName
│   ├── App.cs                    # IExternalApplication: Ribbon Tab, hooks de ciclo de vida
│   ├── Auth/
│   │   ├── DesktopAuthService.cs # Loopback listener (rota /callback), porta efêmera e CSRF state
│   │   └── LoginRequirement.cs   # Verificação headless de sessão (botão Meus Plugins)
│   ├── Client/
│   │   ├── ConnectorApiClient.cs # Cliente HTTP: lease, sync, validate, activate + refresh JWKS
│   │   └── ProductLinks.cs       # Links públicos dos produtos (nodeaec.com.br/products/{slug})
│   ├── Config/
│   │   └── ConnectorConfig.cs    # URLs, slug do produto-mãe e chave pública SPKI (env)
│   ├── Cryptography/
│   │   └── LeaseSignatureVerifier.cs # Verificação Ed25519 + decodificação SPKI (BouncyCastle)
│   ├── Diagnostics/
│   │   └── ConnectorLog.cs        # Log local sanitizado com rotação (512 KB)
│   ├── Storage/
│   │   ├── LeaseStorage.cs        # Gestão do arquivo %APPDATA%\NodeAec\entitlements.lease (DPAPI)
│   │   └── SigningKeyStore.cs     # Cache atômico do JWKS (%APPDATA%\NodeAec\license-jwks.json)
│   ├── Hardware/
│   │   └── HardwareId.cs          # Identificador SHA-256 da máquina
│   ├── Gate/
│   │   └── NodeAecGate.cs         # Micro-SDK: assinatura → issuer → scope → iat/exp → claims
│   ├── Models/
│   │   ├── EntitlementItem.cs     # Modelo de produto concedido
│   │   ├── MasterLeasePayload.cs  # Claims do JWT Ed25519
│   │   ├── SyncResult.cs          # Resultado de sincronização
│   │   └── UserTokenPayload.cs    # Claims do token de usuário (id, email, name)
│   ├── Commands/
│   │   ├── ManageConnectorCommand.cs  # Abre a janela Minha Conta
│   │   ├── ManagePluginsCommand.cs    # Abre a janela Meus Plugins
│   │   ├── RequiresLoginAvailability.cs # Desabilita Meus Plugins antes do login
│   │   └── ExploreCatalogCommand.cs   # Abre catálogo web
│   ├── UI/
│   │   ├── UiTheme.cs           # Paleta light mode da web Node.aec
│   │   ├── ConnectorWindow.cs   # Janela Minha Conta (conta, licenças, chave recolhida)
│   │   └── PluginsWindow.cs     # Janela Meus Plugins (lista + links dos produtos)
│   └── Resources/
│       ├── nodeaec-16.png
│       └── nodeaec-32.png
└── tests/NodeAec.Connector.Tests/
    ├── NodeAec.Connector.Tests.csproj # Target net8.0 (CI-safe, sem dependência do Revit)
    ├── HardwareIdTests.cs
    ├── DesktopAuthServiceTests.cs
    ├── LeaseStorageTests.cs
    ├── LoginRequirementTests.cs
    ├── ProductLinksTests.cs
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
powershell -ExecutionPolicy Bypass -File NodeAec.Connector\scripts\release.ps1 -Version 0.1.1 -Install
```

O script também gera `release/NodeAec.Connector-<versão>-Setup.exe` (instalador com duplo clique para usuários finais, compilado via `scripts/installer.iss`) quando o [Inno Setup 6](https://jrsoftware.org/isdl.php) está instalado; sem ele, apenas o `.zip` é produzido.

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
