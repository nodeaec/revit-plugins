# Manual do Usuário — Node.aec Connector **v0.1**

> **Produto:** Node.aec Connector — Add-in de governança e licenças para o Autodesk Revit
> **Versão do add-in:** 0.1 · **Versão deste manual:** 0.1 · **Data:** setembro de 2026
> **Plataforma:** Windows 10/11 (64-bit) · Autodesk Revit 2026
> **Idioma da interface:** Português (Brasil)

---

## 📖 Sumário

1. [O que é o Node.aec Connector](#1-o-que-é-o-nodeaec-connector)
2. [Requisitos do sistema](#2-requisitos-do-sistema)
3. [Instalação](#3-instalação)
4. [Primeiros passos: a aba Node.aec](#4-primeiros-passos-a-aba-nodeaec)
5. [Janela “Minha Conta”](#5-janela-minha-conta)
6. [Entrar com sua conta (login no navegador)](#6-entrar-com-sua-conta-login-no-navegador)
7. [Atualizar suas licenças](#7-atualizar-suas-licenças)
8. [Ativação com chave manual (NAEC-…)](#8-ativação-com-chave-manual-naec-)
9. [Importação de arquivo `.lease` (adiada — ver limitações)](#9-importar-um-arquivo-de-licença-lease)
10. [Janela “Meus Plugins”](#10-janela-meus-plugins)
11. [Explorar o catálogo](#11-explorar-o-catálogo)
12. [Modo offline e tolerância de 30 dias](#12-modo-offline-e-tolerância-de-30-dias)
13. [Segurança e privacidade](#13-segurança-e-privacidade)
14. [Sair da conta e desinstalar](#14-sair-da-conta-e-desinstalar)
15. [Solução de problemas](#15-solução-de-problemas)
16. [Perguntas frequentes (FAQ)](#16-perguntas-frequentes-faq)
17. [Notas da versão 0.1 e limitações conhecidas](#17-notas-da-versão-01-e-limitações-conhecidas)
18. [Suporte](#18-suporte)

---

## 1. O que é o Node.aec Connector

O **Node.aec Connector** é o aplicativo central da Node.aec dentro do Autodesk Revit. Ele concentra, em um único lugar, o **login da sua conta**, a **ativação das suas licenças** e a **lista dos plugins liberados** para o seu computador.

Funciona segundo o modelo **Hub & Micro-Gate**:

- Você faz **login uma única vez** no navegador (sem digitar senha dentro do Revit).
- O Connector baixa e salva localmente **todas as suas licenças** de uma vez.
- Os plugins Node.aec e parceiros **verificam a licença instantaneamente**, sem internet, a cada comando.
- Você pode trabalhar **até 30 dias desconectado** antes de precisar sincronizar novamente.

### Principais benefícios

| Benefício | O que isso significa para você |
|---|---|
| **Login único (SSO)** | Entra com Google/2FA no seu navegador padrão; nenhuma senha é digitada no Revit. |
| **Licenças em um só lugar** | Todos os seus produtos listados e com data de validade visível. |
| **Funciona offline** | Plugins abrem e validam licença mesmo sem internet (janelas de 30 dias). |
| **Estações isoladas (air-gapped)** | Ativação por chave `NAEC-…` (a importação de arquivo `.lease` está adiada). |
| **Ribbon organizada** | Tudo na aba oficial **Node.aec**, sem abas duplicadas ou fantasmas. |

---

## 2. Requisitos do sistema

- **Sistema operacional:** Windows 10 ou Windows 11 (64-bit).
- **Autodesk Revit:** 2026 (instalação padrão em `C:\Program Files\Autodesk\Revit 2026`).
- **Conexão com a internet:** necessária **apenas** para o primeiro login, atualização de licenças e ativação de chaves. O uso cotidiano dos plugins não exige internet.
- **Navegador padrão:** qualquer navegador (Chrome, Edge, Firefox…) para concluir o login.
- **Permissões:** nenhum acesso de administrador é necessário para usar o Connector.

> 📦 O pacote de instalação já inclui todas as dependências (incluindo a biblioteca de proteção de dados do Windows). Nada precisa ser instalado separadamente.

---

## 3. Instalação

A instalação é normalmente feita pela equipe de TI/manager da sua empresa, mas o procedimento é simples:

1. **Feche o Autodesk Revit** (se estiver aberto).
2. Execute o instalador `NodeAec.Connector-0.1-Setup.exe` (duplo clique → Avançar → Concluir). Ele detecta automaticamente os Revit instalados e registra o desinstalador. Alternativa para equipes de TI: extrair o `.zip` manualmente para a pasta de add-ins.
3. Confirme que os arquivos foram copiados para a pasta de add-ins:
   - Pasta do add-in: `C:\ProgramData\Autodesk\Revit\Addins\2026\NodeAec.Connector\`
   - Manifesto (`.addin`): `C:\ProgramData\Autodesk\Revit\Addins\2026\NodeAec.Connector.addin`
4. **Abra o Autodesk Revit 2026.** A aba **Node.aec** aparece automaticamente na Ribbon.

> ✅ **Verificação:** ao abrir o Revit, procure a aba **Node.aec** no topo da Ribbon. Se ela não aparecer, feche o Revit por completo (inclusive processos em segundo plano) e abra novamente. Em caso persistente, verifique [Solução de problemas](#15-solução-de-problemas).

> 🔄 **Atualização:** para instalar uma nova versão, basta substituir os arquivos na pasta do add-in e reiniciar o Revit. Sua conta e licenças são preservadas.

---

## 4. Primeiros passos: a aba Node.aec

Ao abrir o Revit, clique na aba **Node.aec**. Você verá o painel **Conector** com três botões:

```
┌─────────────────────────────────────────────────────────┐
│  Node.aec                                               │
│  ┌──────────────┐  ┌──────────────┐                     │
│  │              │  │ Meus         │  ← desabilitado     │
│  │   Minha      │  │ Plugins      │    até o login      │
│  │   Conta      │  ├──────────────┤                     │
│  │              │  │ Explorar     │                     │
│  │              │  │ Catálogo     │                     │
│  └──────────────┘  └──────────────┘                     │
│         painel "Conector"                               │
└─────────────────────────────────────────────────────────┘
```

| Botão | O que faz | Disponibilidade |
|---|---|---|
| **Minha Conta** (botão grande) | Abre a janela de conta, licenças e ativação. | Sempre disponível. |
| **Meus Plugins** | Abre a lista de plugins vinculados à sua conta. | **Desabilitado até você fazer login.** |
| **Explorar Catálogo** | Abre o catálogo de produtos no seu navegador. | Sempre disponível. |

**Fluxo recomendado pela primeira vez:**

1. Clique em **Minha Conta**.
2. Clique em **Entrar com minha conta** e conclua o login no navegador.
3. Volte ao Revit — suas licenças já estarão liberadas.
4. Agora clique em **Meus Plugins** para conferir o que foi liberado.

> 🧹 O Connector limpa automaticamente abas e botões legados (por exemplo, abas antigas chamadas “License” ou “Licensing” e o botão “Conectar Conta”), além de eliminar abas **Node.aec** duplicadas. Você não precisa fazer nada para isso.

---

## 5. Janela “Minha Conta”

A janela **Minha Conta — Node.aec** é o coração do Connector. Ela é dividida em cartões:

### 5.1 Cabeçalho

- **“Minha Conta”** com a slogan *“Suas licenças da Node.aec em um só lugar.”*
- Link **“Ver catálogo ↗”** — abre o catálogo de produtos no navegador.

### 5.2 Cartão “Sua conta”

Dois estados possíveis:

| Estado | Texto exibido | Botões |
|---|---|---|
| **Desconectado** | *“Você ainda não entrou.”* + *“Entre com sua conta para liberar seus plugins neste computador.”* | **Entrar com minha conta** |
| **Conectado** | *“Olá! Você está conectado como:”* + seu **e-mail** | **Sair da conta** |

### 5.3 Cartão “Neste computador”

Mostra o estado das suas licenças salvas nesta máquina e permite atualizá-las. Mensagens possíveis:

| Mensagem | Significado |
|---|---|
| *“Tudo certo — suas licenças estão atualizadas até DD/MM/AAAA.”* ✅ | Tudo em ordem; trabalhe tranquilo até essa data. |
| *“Nenhuma licença encontrada neste computador ainda.”* | Você ainda não ativou nem sincronizou nada aqui. |
| *“Suas licenças estão desatualizadas desde DD/MM/AAAA. Conecte-se à internet e clique em atualizar.”* ⚠️ | O prazo offline venceu; é preciso sincronizar. |
| *“Não conseguimos ler as licenças salvas. Tente atualizar.”* ⚠️ | Arquivo local ilegível — clique em atualizar. |

Botão: **Atualizar minhas licenças** — busca as licenças mais recentes da sua conta (ou renova as atuais, se você não estiver logado).

### 5.4 Expansor “Tenho uma chave de ativação”

Fechado por padrão, para não poluir a tela. Clique sobre o título **“Tenho uma chave de ativação”** para abrir. Dentro você encontra:

- **Campo de chave** + botão **Ativar** — para digitar uma chave enviada pela sua empresa (formato `NAEC-…`).
- **Identificação desta máquina (para o suporte):** um código longo em fonte monoespaçada. **Guarde/copie esse código ao pedir suporte** — ele identifica unicamente este computador.

### 5.5 Área de mensagens e rodapé

- Logo abaixo dos cartões aparecem as **mensagens de retorno** (sucesso ou erro) das ações que você executar.
- No rodapé: **“Node.aec Connector 0.1”** e o botão **Fechar**.

---

## 6. Entrar com sua conta (login no navegador)

O login usa **Browser SSO**: você nunca digita senha dentro do Revit.

**Passo a passo:**

1. Em **Minha Conta**, clique em **Entrar com minha conta**.
2. A mensagem *“Abrindo o navegador para você entrar com segurança…”* aparece e seu **navegador padrão** abre a página de login da Node.aec.
3. Faça login normalmente (conta Google, e-mail/senha, **2FA**, etc.).
4. Ao concluir, o navegador mostra a tela **“Login Concluído com Sucesso!”** com o aviso *“Você já pode fechar esta aba do navegador e voltar ao Revit.”*
5. Volte ao Revit: a janela mostra *“Pronto! Buscando suas licenças…”* e depois
   **“Tudo pronto! N plugin(s) liberado(s) neste computador.”**

**O que você precisa saber:**

- ⏱️ Você tem **120 segundos (2 minutos)** para concluir o login no navegador. Se expirar, basta clicar em **Entrar com minha conta** novamente.
- A conexão entre o navegador e o Revit acontece apenas **no seu próprio computador** (endereço local `127.0.0.1`), com proteção contra falsificação (CSRF).
- Após o login, sua sessão fica salva: **não é preciso entrar toda vez** que abrir o Revit.
- Se aparecer *“Algo não saiu como esperado: …”*, repita o login; se persistir, veja [Solução de problemas](#15-solução-de-problemas).

---

## 7. Atualizar suas licenças

Clique em **Atualizar minhas licenças** quando:

- Uma nova licença for liberada para a sua conta;
- A mensagem indicar que as licenças estão **desatualizadas**;
- Você quiser conferir a data de validade mais recente.

**Comportamento:**

- **Estando logado:** o Connector baixa novamente todas as licenças da sua conta → *“Licenças atualizadas com sucesso.”*
- **Sem login (só com licença ativa):** ele apenas **renova** a licença local → *“Licenças atualizadas com sucesso.”*
- **Sem internet:** aparece *“Sem conexão no momento: …”* ou *“Não foi possível atualizar agora: …”*. Suas licenças anteriores continuam válidas até a data mostrada no cartão.

> 💡 **Renovação automática:** toda vez que o Revit é aberto, o Connector renova suas licenças em segundo plano, silenciosamente e sem travar a interface. Se estiver offline, essa tentativa falha em silêncio — nada de erros atrapalhando seu trabalho.

---

## 8. Ativação com chave manual (NAEC-…)

Útil quando sua empresa fornece uma chave de licença em vez de login.

1. Abra **Minha Conta**.
2. Clique no expansor **“Tenho uma chave de ativação”**.
3. Digite a chave no campo indicado. Formato correto:
   `NAEC-XXXX-XXXX-XXXX-XXXX`
4. Clique em **Ativar**.
5. Mensagens possíveis:
   - ✅ *“Chave ativada! Seus plugins foram liberados.”*
   - ⚠️ *“Digite a chave enviada para você (começa com NAEC-...).”* — o campo estava vazio.
   - ⚠️ Mensagem de erro específica (ver [Solução de problemas](#15-solução-de-problemas)).

> 🌐 A ativação por chave **requer internet**, pois valida a chave com o servidor Node.aec. Para máquinas sem internet, fale com o suporte Node.aec: a importação de arquivo `.lease` ainda não está disponível nesta versão.

---

## 9. Importar um arquivo de licença (.lease)

**Esta função não está disponível na versão 0.1.**

O link **“ou importar um arquivo de licença (.lease)”** foi removido da janela **Minha Conta** porque o formato de exportação/troca de arquivos `.lease` ainda não é um contrato estável da plataforma. Um arquivo de origem desconhecida seria recusado na validação de assinatura (Ed25519) e não liberaria nenhum plugin.

**Alternativas para uma estação isolada (air-gapped):**

1. Ative uma chave manual `NAEC-XXXX-XXXX-XXXX-XXXX` — a ativação em si não exige que o lease venha da internet, mas a sincronização das demais licenças sim.
2. Entre com sua conta em uma máquina com internet para sincronizar as licenças e, em seguida, reproduza o mesmo fluxo nesta estação.

A importação de arquivos `.lease` assinados deve voltar em uma iteração futura, quando o formato for oficialmente definido.

---

## 10. Janela “Meus Plugins”

Aberta pelo botão **Meus Plugins** da Ribbon (após o login). Lista **tudo o que a sua conta liberou para este computador**.

**Conteúdo:**

- Um **cartão por plugin**, com:
  - **Nome do plugin**;
  - **Situação da licença**:
    - *“Liberado até DD/MM/AAAA”* ✅ — ativa e válida;
    - *“Liberado — sem data para expirar”* ✅ — licença permanente;
    - *“Expirado em DD/MM/AAAA”* ⚠️ — vencida;
    - outro status em caixa alta (ex.: `SUSPENDED`) ⚠️;
  - Link **“Abrir página do produto ↗”** — abre o site do produto no navegador.
- Plugins **ativos aparecem primeiro** na lista.

**Botões e estados:**

| Elemento | Comportamento |
|---|---|
| **Entrar com minha conta** | Visível apenas quando não há login. Executa o mesmo [login por navegador](#6-entrar-com-sua-conta-login-no-navegador). |
| **Atualizar lista** | Sincroniza novamente → *“Lista atualizada.”* |
| Lista vazia | *“Nenhum plugin vinculado à sua conta ainda.”* + link **“Conhecer o catálogo de plugins ↗”**. |
| Sem login | *“Entre com sua conta para ver seus plugins aqui.”* |
| Rodapé | “Node.aec Connector 0.1” + botão **Fechar**. |

---

## 11. Explorar o catálogo

O botão **Explorar Catálogo** (e o link “Ver catálogo ↗” da janela Minha Conta) abre no seu navegador o catálogo oficial:

**https://nodeaec.com.br/products**

Lá você pode conhecer plugins, famílias e templates disponíveis para a sua conta. Comprar/ativar um produto novo e depois voltar ao Revit e clicar em **Atualizar minhas licenças** para liberá-lo.

> Se o navegador não abrir, o Revit exibe: *“Node.aec Catálogo — Não foi possível abrir o navegador: …”*. Verifique se há um navegador padrão definido no Windows.

---

## 12. Modo offline e tolerância de 30 dias

O Connector foi desenhado para **funcionar sem internet** no dia a dia:

| Conceito | Explicação |
|---|---|
| **Licença local (lease)** | Suas licenças ficam salvas e criptografadas neste computador após a primeira sincronização. |
| **Tolerância offline de 30 dias** | A licença local é válida por até **30 dias** sem contato com o servidor. Dentro desse período, tudo funciona normalmente. |
| **Renovação automática** | Ao abrir o Revit (ou ao clicar em *Atualizar minhas licenças*), a validade é estendida. |
| **Vencimento do prazo** | Aparece *“Suas licenças estão desatualizadas desde DD/MM/AAAA…”*. Conecte-se à internet e clique em **Atualizar minhas licenças**. |

**Estações totalmente isoladas (air-gapped):**

- Use [chave manual](#8-ativação-com-chave-manual-naec-) em uma máquina com internet e sincronize a conta.
- A identificação da máquina é fixa; o lease só funciona no computador para o qual foi emitido.

---

## 13. Segurança e privacidade

| Aspecto | Como o Connector protege você |
|---|---|
| **Senhas** | **Nunca** são digitadas no Revit. O login acontece no seu navegador, com todos os recursos de segurança dele (2FA, verificação em duas etapas). |
| **Conexão local** | O navegador devolve o login ao Revit apenas via `127.0.0.1` (loopback local), com token anti-falsificação (CSRF). Nenhum servidor externo intercepta esse retorno. |
| **Armazenamento local** | Licenças (`entitlements.lease`) e sessão (`session.json`) ficam em `%APPDATA%\NodeAec\`, **criptografados com o Windows DPAPI**, protegidos ao seu usuário do Windows. Outros usuários da máquina não conseguem ler. |
| **Identificação da máquina** | Código irreversível (hash) derivado do registro do Windows + nome do computador. Não representa dados pessoais nem é enviado sem contexto de licença. |
| **Integridade das licenças** | Cada licença é emitida assinada digitalmente (Ed25519) e vinculada a este computador e ao seu prazo de validade. |
| **Comunicação** | Somente com os servidores oficiais `https://api.nodeaec.com.br` e `https://nodeaec.com.br`. |

**Arquivos criados no seu computador:**

```
%APPDATA%\NodeAec\
├── entitlements.lease   ← suas licenças (criptografado)
└── session.json         ← sua sessão de login (criptografado)
```

---

## 14. Sair da conta e desinstalar

### Sair da conta

1. **Minha Conta** → botão **Sair da conta**.
2. Confirme no aviso: *“Deseja sair da sua conta neste computador? Seus plugins ficarão bloqueados até o próximo login.”*
3. Mensagem final: *“Você saiu da conta.”*

> ⚠️ Ao sair, as licenças locais são **removidas** e os plugins Node.aec ficam **bloqueados** até você entrar novamente. Faça isso ao prestar o computador a outra pessoa.

### Desinstalar

1. Feche o Autodesk Revit.
2. Apague a pasta `C:\ProgramData\Autodesk\Revit\Addins\2026\NodeAec.Connector\` e o arquivo `C:\ProgramData\Autodesk\Revit\Addins\2026\NodeAec.Connector.addin`.
3. (Opcional) Apague a pasta `%APPDATA%\NodeAec\` para remover os dados locais de licença e sessão.
4. Abra o Revit — a aba **Node.aec** (do Connector) não aparecerá mais.

> ℹ️ Desinstalar o Connector não cancela suas licenças na conta. Elas continuam disponíveis para reativação em outra instalação.

---

## 15. Solução de problemas

### Mensagens e como agir

| Mensagem exibida | Causa provável | O que fazer |
|---|---|---|
| *“Abrindo o navegador…”* e nada acontece | Navegador padrão não definido / bloqueado | Defina um navegador padrão no Windows e repita o login. |
| *“Falha na validação CSRF do login.”* / login expirado | Login não concluído em 120 s ou retorno inválido | Clique novamente em **Entrar com minha conta** e conclua em até 2 minutos. |
| *“Não foi possível buscar suas licenças: …”* | Servidor indisponível ou sessão expirada | Verifique a internet e entre com sua conta novamente. |
| *“Algo não saiu como esperado: …”* | Erro inesperado no fluxo de login | Repita o login; se persistir, reinicie o Revit. |
| *“Sem conexão no momento: …”* | Sem internet | Conecte-se e clique em **Atualizar minhas licenças**. Suas licenças atuais seguem válidas até a data exibida. |
| *“Não foi possível atualizar agora: …”* | Falha de servidor ou sessão expirada | Entre com sua conta novamente e atualize. |
| *“Digite a chave enviada para você (começa com NAEC-…”* | Campo de chave vazio | Digite a chave no formato `NAEC-XXXX-XXXX-XXXX-XXXX`. |
| *“Formato de chave inválido. A chave deve seguir o formato NAEC-XXXX-XXXX-XXXX-XXXX.”* | Chave incompleta ou com erros | Copie e cole a chave exatamente como foi enviada. |
| *“Chave de licença não encontrada. Verifique a digitação.”* | Chave inexistente | Confirme a chave com quem a enviou. |
| *“Limite de assentos simultâneos atingido para esta licença. Desative o assento em outro computador ou pelo portal web.”* | Todos os postos em uso | Libere um posto pelo **portal web da Node.aec** ou em outro computador. |
| *“Esta licença ou período de avaliação expirou.”* | Vencimento da licença | Renove ou ative uma nova chave. |
| *“Esta licença foi suspensa administrativamente.”* | Suspensão pela plataforma | Fale com o administrador da sua conta/suporte. |
| *“O prazo de tolerância offline (30 dias) expirou. Conecte-se à internet para sincronizar.”* | 30 dias sem sincronizar | Conecte-se à internet e clique em **Atualizar minhas licenças**. |
| *“O identificador da máquina não corresponde ao registro da concessão.”* | Licença de outra máquina | Gere/ative a licença **para este computador** (use o código de identificação da máquina). |
| *“Não foi possível abrir o navegador: …”* | Sem navegador padrão | Configure um navegador padrão no Windows. |

### Problemas comuns

**A aba Node.aec não aparece no Revit**
1. Confirme que os arquivos estão em `C:\ProgramData\Autodesk\Revit\Addins\2026\`.
2. Encerre o Revit por completo (verifique o gerenciador de tarefas) e abra novamente.
3. Verifique mensagens de erro do Revit em *Exibir → Navegador de erros*.

**O botão “Meus Plugins” está cinza (desabilitado)**
→ Comportamento esperado: ele só habilita **após o login**. Clique em **Minha Conta** e entre com sua conta.

**Um plugin parceiro mostra “Node.aec — Licença Necessária”**
→ A licença daquele produto não está liberada nesta máquina. Motivos possíveis:
- *“Nenhuma credencial do Node.aec encontrada nesta estação…”* → abra **Minha Conta** e entre com sua conta (ou ative uma chave).
- *“A concessão de licenças foi emitida para outra estação de trabalho…”* → a licença é de outro computador; ative nesta máquina.
- *“O produto ‘…’ não consta nas licenças ativas desta conta…”* → adquira/ative o produto no catálogo.
- *“O limite de computadores simultâneos para ‘…’ foi atingido.”* → libere um posto pelo portal web.
- *“A licença ou período de teste de ‘…’ expirou em DD/MM/AAAA.”* → renove.

**Plugins bloqueados após “Sair da conta”**
→ Esperado. Entre com sua conta novamente para restaurar as licenças.

**Licenças sumiram depois de trocar de computador/arquivador**
→ Licenças ficam salvas **por usuário do Windows** em `%APPDATA%\NodeAec\`. Em máquina nova, basta [entrar com sua conta](#6-entrar-com-sua-conta-login-no-navegador) novamente.

---

## 16. Perguntas frequentes (FAQ)

**Preciso entrar com minha conta toda vez que abrir o Revit?**
Não. Após o primeiro login, a sessão fica salva (criptografada) neste computador.

**Preciso de internet para trabalhar?**
Não, desde que as licenças estejam sincronizadas e dentro do prazo de **30 dias**. Internet é necessária apenas para login, atualização e ativação de chaves.

**Posso usar o mesmo login em vários computadores?**
Sim, respeitando o limite de postos (assentos) definido para cada licença.

**Onde vejo até quando minhas licenças são válidas?**
Em **Minha Conta** → cartão **“Neste computador”** (data geral) e em **Meus Plugins** (data por produto).

**Esqueci minha senha / não consigo logar**
A autenticação é feita na página da Node.aec pelo seu navegador — use “Esqueci minha senha” lá ou fale com o administrador da sua conta.

**Como sei qual é o ID desta máquina para o suporte?**
Em **Minha Conta** → expansor **“Tenho uma chave de ativação”** → linha *“Identificação desta máquina (para o suporte): …”*. Copie e envie ao suporte.

**Meus dados são vendidos ou enviados para terceiros?**
Não. O Connector conversa apenas com os servidores oficiais da Node.aec para validar licenças.

**O Connector modifica meus arquivos de projeto (.RVT)?**
Não. Ele não altera modelos do Revit — apenas Ribbon, licenças e janelas próprias.

---

## 17. Notas da versão 0.1 e limitações conhecidas

**Versão 0.1 — primeira versão pública**

### O que está incluído

- Aba canônica **Node.aec** com painel **Conector** e deduplicação automática de abas.
- Janela **Minha Conta**: login, logout, status de licenças, atualização e ativação manual.
- Login **SSO por navegador** (loopback local, proteção CSRF, janela de 120 s).
- Janela **Meus Plugins** com cartões, validades e links para cada produto.
- Botão **Explorar Catálogo**.
- Tolerância offline de **30 dias** + renovação silenciosa ao abrir o Revit.
- Armazenamento local criptografado (DPAPI) e vinculação à máquina.

### Limitações conhecidas desta versão

- **“Meus Plugins” fica desabilitado antes do login** (por design).
- **Não há botão de desativação de posto (seat) dentro do Revit** — para liberar um posto, use o **portal web da Node.aec**.
- O Connector **não instala nem atualiza automaticamente** os plugins: ele libera a licença; a entrega e a atualização dos add-ins são feitas pelo instalador do próprio produto.
- Não há notificação visual pop-up quando a tolerância offline vence — o aviso aparece ao abrir **Minha Conta**.
- A janela de login pode não voltar o foco automaticamente ao Revit; basta alternar de janela.
- **A importação de arquivos `.lease` não está disponível nesta versão** — o link foi removido da janela **Minha Conta**; a função deve voltar quando o formato de troca for um contrato estável da plataforma (ver [seção 9](#9-importar-um-arquivo-de-licença-lease)).
- O contador “N plugin(s) liberado(s)” refere-se à última sincronização.

---

## 18. Suporte

| Canal | Como usar |
|---|---|
| **Suporte técnico** | Tenha em mãos: o **ID da máquina** (Minha Conta → expansor de chave → “Identificação desta máquina”), a versão (**Node.aec Connector 0.1**) e a mensagem de erro exata. |
| **Portal / catálogo** | [https://nodeaec.com.br/products](https://nodeaec.com.br/products) |
| **Área da conta** | [https://nodeaec.com.br](https://nodeaec.com.br) |
| **Repositório e issue tracker** | [github.com/nodeaec/revit-plugins](https://github.com/nodeaec/revit-plugins) — abra uma *issue* descrevendo o passo a passo do problema. |

---

*Manual do Usuário — Node.aec Connector v0.1 · Node.aec (https://nodeaec.com.br) · Setembro de 2026*
