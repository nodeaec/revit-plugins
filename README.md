# Node.aec // Revit Plugins & Developer Hub

Repositório público oficial da [Node.aec](https://nodeaec.com.br) para a comunidade AEC/BIM.

Este repositório é o ponto de encontro de desenvolvedores, engenheiros de automação e criadores de software para Autodesk Revit. Seu propósito é **hospedar códigos públicos da Node.aec para a comunidade e acelerar o desenvolvimento de ferramentas, add-ins e softwares prontos para hospedagem e monetização na Node.aec Store e integração com a nossa plataforma**.

---

## 🎯 Missão e Objetivos

1. **Códigos Públicos e Confiáveis**: Fornecer bibliotecas, ferramentas e componentes abertos desenvolvidos com as melhores práticas da Revit API e padrões modernos de C#/.NET.
2. **Acelerador para Desenvolvedores**: Eliminar o trabalho repetitivo de infraestrutura (licenciamento, ribbon, telemetria, autenticação e empacotamento) para criadores que desejam publicar seus add-ins na **Node.aec Store**.
3. **Padrão de Integração Node.aec**: Manter implementações de referência e guias para conexão com os serviços da plataforma (catálogo de produtos, API de licenciamento offline-first, webhooks e atualizações).

---

## 📁 Estrutura do Repositório

```text
revit-plugins/
├── AGENTS.md                          # Diretrizes e regras para agentes de IA neste repositório
├── README.md                          # Este documento (visão geral do repositório)
│
└── plugin/                           # Add-in Hub central de governança desktop e Ribbon unificada
    ├── README.md                      # Documentação completa do Connector
    ├── docs/                          # Manual do usuário e contrato da API de licenciamento
    ├── NodeAec.Connector.sln          # Solution (.NET 8 / Revit 2026)
    ├── scripts/
    │   └── release.ps1                # Script de compilação, empacotamento e deploy local
    ├── src/NodeAec.Connector/
    │   ├── Auth/                      # DesktopAuthService (Browser SSO Loopback RFC 8252)
    │   ├── Client/                    # ConnectorApiClient (Master Entitlements Lease)
    │   ├── Gate/                      # NodeAecGate (Micro-SDK de validação local < 1ms)
    │   ├── Storage/                   # LeaseStorage (Persistência DPAPI %APPDATA%\NodeAec)
    │   ├── UI/                        # ConnectorWindow (Interface WPF moderna)
    │   └── App.cs                     # IExternalApplication (Ribbon Node.aec e deduplicação)
    └── tests/NodeAec.Connector.Tests/ # Testes unitários (net8.0, CI-safe)
```

---

## 🚀 Projeto Principal

### `NodeAec.Connector` (Hub Desktop Central & Governança)
Add-in centralizador de governança e Ribbon unificada `Node.aec` para Autodesk Revit.
- **Browser SSO (RFC 8252)**: Login seguro no navegador padrão com Google OAuth e retorno por loopback local.
- **Master Entitlements Lease**: Sincronização consolidada de todos os produtos do usuário em um único token assinado com Ed25519.
- **Micro-SDK `NodeAecGate`**: Validação de autorização em plugins parceiros em menos de 1ms sem acessar rede.
- **Tolerância Offline de 30 Dias**: Operação contínua desconectada e suporte a estações isoladas (*air-gapped*).
- 📖 [Acessar Guia do Node.aec Connector (README.md)](plugin/README.md)
- 📖 [Acessar o Manual do Usuário](plugin/docs/USER_MANUAL.md) e o [Contrato da API de Licenciamento](plugin/docs/licensing-api.md)

---

## 🛠️ Ambiente e Pré-requisitos

Para compilar e contribuir com os projetos deste repositório:

- **Sistema Operacional**: Windows 10 ou 11 (64-bit)
- **Autodesk Revit**: 2026 instalado no caminho padrão (`C:\Program Files\Autodesk\Revit 2026`) ou 2025+
- **SDK .NET**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Shell**: PowerShell 5.1 ou PowerShell 7+

---

## 💻 Compilação e Deploy Rápido

Para compilar a solution e instalar o add-in diretamente no Revit local:

```powershell
# Navegar até o projeto
Set-Location plugin

# Compilar via .NET CLI
dotnet build NodeAec.Connector.sln -c Release

# Empacotar em .zip e instalar automaticamente no Revit 2026
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.1.1 -Install
```

---

## 🤝 Como Contribuir

Contribuições da comunidade AEC são muito bem-vindas!
1. Crie uma branch a partir de `main` (`feature/sua-melhoria`).
2. Siga as diretrizes de arquitetura e código descritas em [`AGENTS.md`](AGENTS.md).
3. Certifique-se de que a compilação execute com **0 erros** e **0 warnings desnecessários**.
4. Abra um Pull Request detalhando as alterações e o propósito para o ecossistema.

---

## 🌐 Ecossistema Node.aec

- **Portal Oficial**: [nodeaec.com.br](https://nodeaec.com.br)
- **Catálogo de Ferramentas**: [nodeaec.com.br/products](https://nodeaec.com.br/products)
- **Área do Desenvolvedor**: [nodeaec.com.br/workspace](https://nodeaec.com.br/workspace)
- **Suporte & Comunidade**: Abra uma issue neste repositório ou contate o time Node.aec.
