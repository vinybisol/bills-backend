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

- Usa NUnit para unitários e testes de integração.
- Há suporte para rodar testes locais com PostgreSQL, incluindo integração com banco real.

## 📁 Estrutura principal

- `src/Api` – endpoints e configuração da API.
- `src/Application` – regras de negócio e serviços.
- `src/Data` – EF Core, contexto e migrations.
- `src/Domain` – entidades, enums e abstrações do domínio.

## 📌 Documentação

- `docs/api.md` — contratos e endpoints.
- `docs/dominio.md` — regras e conceitos do domínio.
- `docs/schema.md` — modelo de dados e schema.

## 🛠️ Execução local

1. Configure o PostgreSQL para testes/desenvolvimento.
2. Ajuste a `ConnectionStrings` no `appsettings.Development.json` ou variáveis de ambiente.
3. Execute a API com `dotnet run --project src/Api/Api.csproj`.

> Este README é apenas um resumo para começar. Consulte `CLAUDE.md` e `docs/` para detalhes do projeto.
