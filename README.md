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
├── NodeAec.Connector/                 # Add-in Hub central de governança desktop e Ribbon unificada
│   ├── README.md                      # Documentação completa do Connector
│   ├── NodeAec.Connector.sln          # Solution (.NET 8 / Revit 2026)
│   ├── scripts/
│   │   └── release.ps1                # Script de compilação, empacotamento e deploy local
│   ├── src/NodeAec.Connector/
│   │   ├── Auth/                      # DesktopAuthService (Browser SSO Loopback RFC 8252)
│   │   ├── Client/                    # ConnectorApiClient (Master Entitlements Lease)
│   │   ├── Gate/                      # NodeAecGate (Micro-SDK de validação local < 1ms)
│   │   ├── Storage/                   # LeaseStorage (Persistência DPAPI %APPDATA%\NodeAec)
│   │   ├── UI/                        # ConnectorWindow (Interface WPF moderna)
│   │   └── App.cs                     # IExternalApplication (Ribbon Node.aec e deduplicação)
│   └── tests/NodeAec.Connector.Tests/ # Testes unitários (net8.0, CI-safe)
│
└── NodeAec.Licensing.Sample/          # Exemplo canônico de integração de licenciamento para plugins
    ├── AGENTS.md                      # Guia do agente para integrar licenciamento em plugins externos
    ├── README.md                      # Guia passo a passo de integração para humanos
    ├── NodeAec.Licensing.Sample.sln   # Solution (.NET 8 / Revit 2026)
    ├── scripts/
    │   └── release.ps1                # Script de compilação, empacotamento e deploy local
    └── src/
        └── NodeAec.Licensing.Sample/
            ├── Client/                # NodeAecLicenseClient (HTTP, Ed25519, DPAPI)
            ├── Commands/              # IExternalCommand (ManageLicenseCommand)
            ├── Config/                # LicenseConfig (endpoints, chaves públicas, paths)
            ├── Resources/             # Ícones oficiais Node.aec
            ├── UI/                    # LicenseManagerWindow (WPF)
            └── App.cs                 # IExternalApplication (Aba Node.aec e Ribbon)
```

---

## 🚀 Projetos e Módulos

### 1. `NodeAec.Connector` (Hub Desktop Central & Governança)
Add-in centralizador de governança e Ribbon unificada `Node.aec` para Autodesk Revit.
- **Browser SSO (RFC 8252)**: Login seguro no navegador padrão com Google OAuth e retorno por loopback local.
- **Master Entitlements Lease**: Sincronização consolidada de todos os produtos do usuário em um único token assinado com Ed25519.
- **Micro-SDK `NodeAecGate`**: Validação de autorização em plugins parceiros em menos de 1ms sem acessar rede.
- **Tolerância Offline de 30 Dias**: Operação contínua desconectada e suporte a estações isoladas (*air-gapped*).
- 📖 [Acessar Guia do Node.aec Connector (README.md)](NodeAec.Connector/README.md)

### 2. `NodeAec.Licensing.Sample` (Acelerador de Licenciamento)
Implementação de referência completa para proteção e distribuição de add-ins comerciais no Revit.
- **Segurança Criptográfica**: Assinatura digital assimétrica Ed25519 (RFC 8032) permitindo validação offline por até 30 dias.
- **Proteção de Hardware (Machine Lock)**: Vinculação de token ao GUID da máquina via Windows DPAPI.
- **Interface Pronta em WPF**: Janela minimalista e elegante para ativação de chaves e gestão de postos de trabalho (*seats*).
- **Aba Canônica**: Consolidação na aba oficial `Node.aec` da Ribbon do Revit.
- 📖 [Acessar Guia do Desenvolvedor (README.md)](NodeAec.Licensing.Sample/README.md)
- 🤖 [Acessar Guia de Agentes de IA para Integração (AGENTS.md)](NodeAec.Licensing.Sample/AGENTS.md)

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
# Navegar até o projeto desejado
Set-Location NodeAec.Licensing.Sample

# Compilar via .NET CLI
dotnet build NodeAec.Licensing.Sample.sln -c Release

# Empacotar em .zip e instalar automaticamente no Revit 2026
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 1.0.0 -Install
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
