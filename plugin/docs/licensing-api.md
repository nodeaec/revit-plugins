# Contrato da API de Licenciamento Node.aec (Wire Protocol)

Este documento descreve o protocolo HTTP **consumido pelo Node.aec Connector**: endpoints,
payloads, claims do lease, retorno do Browser SSO e mapeamento de erros.

> **Escopo**: somente o que este repositório efetivamente chama. Os campos documentados são
> exatamente os que o cliente lê — a API pode devolver campos adicionais sem quebra de
> compatibilidade.
>
> **Fonte de verdade** (alterou este documento, altere o código junto):
> `Client/ConnectorApiClient.cs`, `Storage/SigningKeyStore.cs`,
> `Auth/DesktopAuthService.cs`, `Gate/NodeAecGate.cs`, `Models/MasterLeasePayload.cs`.

Para a visão de usuário final (instalação, janelas, mensagens) veja o
[Manual do Usuário](USER_MANUAL.md). Para proteger um plugin parceiro com o Micro-SDK, veja
[Como Integrar com `NodeAecGate`](../README.md#-como-integrar-plugins-parceiros-com-o-nodeaecgate).

---

## 1. Base URL e configuração

| Variável de ambiente | Padrão | Uso |
|---|---|---|
| `NODEAEC_API_URL` | `https://api.nodeaec.com.br` | Base de todos os endpoints `/license/*` e `/account/*` |
| `NODEAEC_AUTH_URL` | `https://nodeaec.com.br/auth/desktop` | Página de login aberta no navegador |
| `NODEAEC_CATALOG_URL` | `https://nodeaec.com.br/products` | Catálogo aberto pelo botão *Explorar Catálogo* |
| `NODEAEC_LICENSE_PUBLIC_KEY_SPKI` | *(ausente)* | Âncora pública Ed25519 fixa. Quando presente, tem prioridade sobre o JWKS em cache |

Endpoint fixo de produção: **`https://api.nodeaec.com.br`**. Não há chave privada neste
repositório — o cliente só possui a chave pública usada para **verificar** assinaturas.

---

## 2. Endpoints consumidos

| Método | Caminho | Autenticação | Chamado por |
|---|---|---|---|
| `POST` | `/account/entitlements/lease` | `Bearer <token de usuário>` | Sincronização (`SyncMasterEntitlementsAsync`) |
| `POST` | `/license/validate` | `Bearer <leaseToken>` | Heartbeat de abertura do Revit e *Atualizar minhas licenças* |
| `POST` | `/license/activate` | *(pública)* — identifica por `licenseKey` + `machineId` | Ativação manual de chave `NAEC-XXXX-...` |
| `POST` | `/license/deactivate` | *(pública)* — identifica por `licenseKey` + `machineId` | Liberação de assento |
| `GET` | `/license/jwks` | *(pública)* | Cache de chaves públicas (`SigningKeyStore`) |

Endpoints do portal (`/workspace#licenses`) e a troca de sessão web por token de desktop são
executados **no servidor**, não pelo Connector — ficam fora deste contrato.

---

## 3. `POST /account/entitlements/lease`

Obtém o **Master Entitlements Lease**: um único JWT Ed25519 com todas as concessões da
conta, emitido para a máquina informada.

- **Cabeçalho**: `Authorization: Bearer <token de usuário>`, `Content-Type: application/json`
- **Corpo**:

```json
{
  "machineId": "3b7c89f1a0e4d2...",
  "deviceName": "ESTACAO-PROJETO-01",
  "platform": "Windows / Revit 2026",
  "connectorVersion": "0.1.1"
}
```

`platform` é `ConnectorConfig.PlatformDescription` e `connectorVersion` é
`ConnectorConfig.Version`; `machineId` é o hash SHA-256 de
`MachineGuid:MachineName` (ver [HardwareId.cs](../src/NodeAec.Connector/Hardware/HardwareId.cs)).

- **Resposta** (campos lidos pelo cliente):

```json
{
  "success": true,
  "leaseToken": "eyJhbGciOiJFZERTQSI...",
  "expiresAt": "2026-10-20T15:30:00.000Z",
  "grantedCount": 3,
  "totalCount": 3,
  "entitlements": [
    { "slug": "meu-plugin", "name": "Meu Plugin", "licenseKey": "NAEC-A2C4-...",
      "type": "perpetual", "status": "active", "granted": true, "expiresAt": null }
  ]
}
```

**Sequência obrigatória pós-resposta**, nesta ordem:

1. `GET /license/jwks` — atualiza o cache de chaves públicas **antes** de confiar no lease;
2. grava `leaseToken` em `%APPDATA%\NodeAec\entitlements.lease` via DPAPI;
3. falha de gravação ⇒ a sincronização inteira falha (*fail-closed*, nunca texto puro).

---

## 4. `POST /license/validate` (heartbeat)

Renova o lease e atualiza o heartbeat da máquina. Chamado silenciosamente na abertura do
Revit (`App.OnStartup`, em `Task.Run`) e sob demanda pelo botão *Atualizar minhas licenças*.

- **Cabeçalho**: `Authorization: Bearer <leaseToken>`
- **Corpo**: `{ "machineId": "3b7c89f1a0e4d2..." }`
- **Resposta** (campos lidos pelo cliente):

```json
{
  "valid": true,
  "leaseToken": "eyJhbGciOiJFZERTQSI...",
  "entitlements": [ { "slug": "meu-plugin", "status": "active", "expiresAt": null } ]
}
```

- `valid: false` ⇒ o Connector descarta o lease e pede novo login;
- `leaseToken` presente ⇒ é salvo com DPAPI (a gravação pode falhar ⇒ erro ao usuário);
- `entitlements` presente tem prioridade sobre o payload do lease local (permite status
  granular mais fresco, ex.: `seat_released`).

---

## 5. `POST /license/activate`

Ativa uma chave manual `NAEC-XXXX-XXXX-XXXX-XXXX`.

- **Corpo**:

```json
{
  "licenseKey": "NAEC-A2C4-E6G8-H2K4-M6P8",
  "machineId": "3b7c89f1a0e4d2...",
  "deviceName": "ESTACAO-PROJETO-01",
  "platform": "Windows / Revit 2026",
  "clientVersion": "0.1.1"
}
```

- **Resposta**: `{ "success": true, "leaseToken": "eyJhbGciOi..." }` (o cliente lê apenas
  `leaseToken`).

> ⚠️ **O lease devolvido aqui é de produto único** (sem claim `entitlements`) e **nunca**
> substitui o Master Entitlements Lease local — gravá-lo apagaria as demais concessões e o
> gate passaria a negar tudo. Por isso a ativação só libera de fato quando a conta
> ressincroniza o lease mestre; a janela força essa ressincronização logo após o retorno.

---

## 6. `POST /license/deactivate`

Libera o assento ocupado por esta máquina.

- **Corpo**: `{ "licenseKey": "NAEC-A2C4-...", "machineId": "3b7c89f1a0e4d2..." }`
- O cliente considera apenas o status HTTP de sucesso.

---

## 7. `GET /license/jwks`

Fornece as chaves públicas usadas na verificação Ed25519 (RFC 7517 / RFC 8032).

```json
{
  "keys": [
    { "kty": "OKP", "crv": "Ed25519", "x": "....", "kid": "node-aec-license-1",
      "use": "sig", "alg": "EdDSA" }
  ]
}
```

O cliente só aceita chaves com `kty = OKP`, `crv = Ed25519` e `x` decodificando para 32
bytes. O resultado é cacheado em `%APPDATA%\NodeAec\license-jwks.json` (escrita atômica) e
revalidado contra a âncora `NODEAEC_LICENSE_PUBLIC_KEY_SPKI`, quando definida. Sem chave
utilizável, **o gate falha fechado**.

---

## 8. Retorno do Browser SSO (loopback RFC 8252)

O Connector abre `https://nodeaec.com.br/auth/desktop?port=<porta>&state=<csrf>` no
navegador padrão e escuta em `127.0.0.1`:

| Item | Comportamento real |
|---|---|
| Prefixo do listener | `http://127.0.0.1:<porta>/` (prefixo **raiz**) |
| Caminho aceito | `/callback` **ou** `/callback/` — qualquer outro caminho responde `404` (favicon, sondagens) |
| Query string esperada | `state` (deve ser idêntico ao enviado) e `token` (token de usuário) |
| Falha de validação | `400` + `Estado inválido ou token ausente.` |
| Timeout | **120 segundos**; ao expirar o listener é abortado |
| Transporte | Somente loopback — nada trafega para servidor externo na ida de volta |

Não há PKCE nem `code_verifier`: o portal devolve o token diretamente na query string do
redirecionamento local, protegido pelo `state` anti-CSRF.

---

## 9. Claims do Master Entitlements Lease

| Claim | Significado | Validado pelo gate |
|---|---|---|
| `iss` | Emissor — deve ser `node-aec` | ✔ |
| `scope` | Deve ser `master-lease` (leases de produto único são recusados) | ✔ |
| `aud` | Audiência (string ou array) — deve incluir `node-aec-desktop` ou `node-aec-plugin` | ✔ |
| `mid` | Machine ID de emissão, case-insensitive | ✔ |
| `iat` | Emissão (Unix seconds) — rejeitado se `iat > agora + 5 min` (relógio retroagido) | ✔ |
| `exp` | Fim da tolerância offline — é a fonte do prazo de 30 dias | ✔ |
| `entitlements[]` | `slug`, `name`, `licenseKey`, `type`, `status`, `granted`, `expiresAt`, `maxActivations`, `activeActivations` | ✔ (por `slug`) |

**Ordem de verificação em `NodeAecGate.Validate(slug)`** — nenhuma etapa avança se a
anterior falhar:

1. assinatura Ed25519 (JWKS/âncora) → 2. `iss` → 3. `scope` → 4. `aud` → 5. `iat`
(skew de 5 min) → 6. `mid` (amarração de hardware) → 7. `exp` (tolerância offline) →
8. presença e status do `slug` pedido.

---

## 10. Códigos de erro → mensagem exibida

A API responde `{ "error": true, "status": ..., "type": ..., "code": ..., "message": ... }`.
O Connector **decide pelo campo `code`**, com esta precedência: código mapeado → `message`
do servidor → código cru → status HTTP. Nunca lança exceção (corpo não-JSON cai no status).

| `code` | Mensagem exibida pelo Connector |
|---|---|
| `BAD_REQUEST` | Os dados enviados foram recusados. Revise e tente novamente. |
| `UNAUTHORIZED` | Sua sessão expirou. Entre com sua conta novamente. |
| `FORBIDDEN` | Você não tem permissão para esta ação. |
| `NOT_FOUND` | Serviço não encontrado. Verifique a conexão com a plataforma Node.aec. |
| `RATE_LIMITED` / `LICENSE_RATE_LIMITED` | Muitas tentativas em pouco tempo. Aguarde alguns minutos e tente novamente. |
| `ACCOUNT_INACTIVE` | Sua conta não está ativa. Fale com o suporte do Node.aec. |
| `LICENSE_KEY_REQUIRED` | Informe a chave de licença (formato NAEC-XXXX-XXXX-XXXX-XXXX). |
| `MACHINE_ID_REQUIRED` | A identificação da máquina não foi enviada. Reinicie o Connector e tente novamente. |
| `INVALID_LICENSE_KEY_FORMAT` | Formato de chave inválido. A chave deve seguir o formato NAEC-XXXX-XXXX-XXXX-XXXX. |
| `LICENSE_NOT_FOUND` | Chave de licença não encontrada. Verifique a digitação. |
| `LICENSE_EXPIRED` | Esta licença ou período de avaliação expirou. |
| `LICENSE_SUSPENDED` | Esta licença foi suspensa administrativamente. Fale com o suporte do Node.aec. |
| `LICENSE_REVOKED` | Esta licença foi cancelada ou reembolsada. Libere outra chave. |
| `LICENSE_INACTIVE` | Esta licença não está ativa. Fale com o suporte do Node.aec. |
| `TRIAL_ALREADY_USED` | O período de avaliação já foi usado neste computador. Contrate uma assinatura comercial. |
| `ACTIVATION_LIMIT_REACHED` | Limite de assentos simultâneos atingido para esta licença. Desative o assento em outro computador ou pelo portal web. |
| `ACTIVATION_NOT_FOUND` | Este computador ainda não está registrado nesta licença. Ative a chave primeiro. |
| `LEASE_TOKEN_REQUIRED` | Nenhuma licença local encontrada. Clique em atualizar para baixar suas licenças. |
| `INVALID_LEASE_TOKEN` | A licença local é inválida ou foi adulterada. Atualize suas licenças na internet. |
| `LEASE_TOKEN_EXPIRED` | O prazo de tolerância offline expirou. Conecte-se à internet para sincronizar. |
| `MACHINE_MISMATCH` | A licença local pertence a outro computador. Entre com sua conta para ativar este equipamento. |

---

## 11. Offline e estações isoladas

- A **tolerância offline** é o campo `exp` do lease emitido pela API (padrão de 30 dias) —
  não existe variável de ambiente client-side que a altere.
- Todo o ciclo de renovação é *best-effort*: se o Revit abre sem internet, o heartbeat falha
  silenciosamente e a causa fica registrada em `%APPDATA%\NodeAec\connector.log`.
- **Air-gapped**: a ativação de chave manual exige internet uma única vez; depois disso o
  lease local sustenta o funcionamento pelo prazo de `exp`.
- A **importação de arquivos `.lease`** não está disponível na versão 0.1 — o formato de
  exportação/troca ainda não é um contrato estável da plataforma, e um arquivo de origem
  desconhecida seria recusado na verificação de assinatura. Veja as
  [alternativas no manual](USER_MANUAL.md#9-importar-um-arquivo-de-licença-lease).
