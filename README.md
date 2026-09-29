[![codecov](https://codecov.io/github/vinybisol/bills-backend/graph/badge.svg?token=ISA68KJ8DQ)](https://codecov.io/github/vinybisol/bills-backend)
![CI/CD](https://github.com/vinybisol/bills-backend/actions/workflows/ci.yml/badge.svg)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![Last Commit](https://img.shields.io/github/last-commit/vinybisol/bills-backend)
![Issues](https://img.shields.io/github/issues/vinybisol/bills-backend)
![Top Language](https://img.shields.io/github/languages/top/vinybisol/bills-backend)
![License](https://img.shields.io/github/license/vinybisol/bills-backend)


# Bills Backend

Um backend simples e robusto para gerenciamento de orçamento pessoal, construído com .NET 10 e PostgreSQL.

## ✨ O que ele faz

- Gera projeções automáticas de lançamentos recorrentes a partir de moldes ativos.
- Mantém histórico de cobranças e pagamentos com controle de imutabilidade para entradas pagas/recebidas.
- Recalcula valores a partir de um mês definido, atualizando apenas lançamentos futuros não pagos.
- Apresenta dashboards mensais e anuais com saldos previstos, realizados e valores a receber.
- Controla valores "a receber" de terceiros diretamente nos lançamentos, com marcação em lote e histórico.
- Integra autenticação via JWT do Firebase e provisiona o usuário interno no primeiro acesso.

## 🚀 Por que usar

- Foco em fluxo financeiro realista: pagamentos, recebimentos e projeções são tratados separadamente.
- Regras de negócio preservam histórico e evitam alterações indevidas em lançamentos concluídos.
- Projetado para ser usado com um frontend leve ou automações que precisem de dados financeiros confiáveis.

## 🧪 Testes

- NUnit 4 + NSubstitute no Microsoft.Testing.Platform (MTP, via `global.json`); versões centralizadas em `Directory.Packages.props`.
- Unitários (sem banco): um projeto por camada em `tests/UnitTests/` (`Domain.UnitTests`, `Application.UnitTests`, `Api.UnitTests`, `Data.UnitTests`). Ex.: `dotnet test --project tests/UnitTests/Application.UnitTests`.
- Integração (PostgreSQL real, Respawn por fixture, sem paralelismo): projeto único `tests/IntegrationTests/Api.IntegrationTests`. Ex.: `dotnet test --project tests/IntegrationTests/Api.IntegrationTests --filter FullyQualifiedName~MeEndpointTests` (setup do banco em `CLAUDE.md`, seção "Setup local de testes").
- Com MTP, use sempre `--project` para filtrar; `dotnet test` na raiz roda a suíte completa.

## 📁 Arquitetura

Clean Architecture — dependências apontam para dentro (`Api` → `Application` → `Domain`; `Data` implementa as abstrações de `Application`):

- `src/Domain` – entidades, enums, `Result`/`Error` e cálculos puros (`Calculations/`).
- `src/Application` – serviços (regras de negócio) que retornam `Result`, abstrações de repositório/`IUnitOfWork` e DTOs.
- `src/Data` – EF Core (`AppDbContext` com filtro global por owner), repositórios, `UnitOfWork` e migrations.
- `src/Api` – endpoints Minimal API finos, contratos de request, `UserEndpointFilter` (JWT do Firebase → `app_user` provisionado just-in-time) e o mapeamento `Result` → HTTP (`ProblemDetails`/`ValidationProblem`).

Detalhes e o padrão para novas features em `CLAUDE.md` (seção "Arquitetura").

## 📌 Documentação

- `docs/arquitetura.md` — camadas, árvore de pastas, fluxo da requisição, mapa feature → endpoint/serviço/repositório, como adicionar uma feature.
- `docs/testes.md` — estrutura dos projetos de teste (NUnit/MTP), infra de integração, comandos.
- `docs/api.md` — contratos e endpoints.
- `docs/dominio.md` — regras e conceitos do domínio.
- `docs/schema.md` — modelo de dados e schema.
- `docs/decisoes.md` — decisões de arquitetura (por quê).

## 🛠️ Execução local

1. Configure o PostgreSQL para testes/desenvolvimento.
2. Ajuste a `ConnectionStrings` no `appsettings.Development.json` ou variáveis de ambiente.
3. Execute a API com `dotnet run --project src/Api/Api.csproj`.

> Este README é apenas um resumo para começar. Consulte `CLAUDE.md` e `docs/` para detalhes do projeto.
