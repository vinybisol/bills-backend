# CLAUDE.md — Backend (Orçamento Pessoal)

API do sistema de orçamento pessoal. Este arquivo define como os agentes devem trabalhar neste repositório. **Siga estas regras em toda tarefa.**

## Stack

- .NET 10, Minimal APIs, EF Core
- PostgreSQL (Neon) — connection string do **pooler** + `SSL Mode=Require`
- Autenticação: validação de JWT do Firebase; identidade própria via `app_user` (ver schema)
- Hospedagem: Azure Web App (F1)

## Agente e skills

- **Sempre** use o agente `csharp-dotnet-expert` para tarefas de código deste repo.
- **Sempre** use as skills de .NET disponíveis.

## Testes (obrigatório)

- Framework: **NUnit**.
- **Toda** tarefa que adiciona ou altera comportamento deve incluir:
  - **Testes unitários** da lógica (regras de negócio, cálculos de split, projeção, recálculo).
  - **Testes de integração** dos endpoints (incluindo autenticação e acesso ao banco).
- **Nunca** abra um PR com testes falhando ou sem cobertura para o que foi implementado.

### Ciclo de teste rápido (iteração)

Os testes de integração rodam contra um Postgres real — a suíte completa é lenta. **Durante o desenvolvimento, não rode `dotnet test` cheio a cada mudança.** Rode só o subconjunto relevante para encurtar o feedback:

- Só os unitários (sem banco, segundos): `dotnet test tests/BillsBackend.UnitTests`
- Só a fixture da feature: `dotnet test --filter FullyQualifiedName~ProjectionEndpointTests`
- Por nome de teste: `dotnet test --filter Name~Idempot`

**Só antes de abrir o PR** rode a suíte completa (`dotnet test`) **uma vez**. PR só é aberto com a suíte inteira verde.

### Isolamento dos testes de integração

Cada teste usa um `firebase_uid` (e portanto um `owner_id`) **distinto**; o filtro global por owner isola os dados sem precisar limpar o banco entre testes. Por isso o reset do banco (Respawn) roda **uma vez por fixture** (no `[OneTimeSetUp]`), não por teste. Ao criar uma nova fixture de integração, herde de `IntegrationTestBase` ou siga esse mesmo padrão — **nunca** adicione um `[SetUp]` que reseta o banco a cada teste.

## Git flow

- Branches permanentes: `main` (produção) e `develop` (integração).
- **Todo trabalho** acontece em branch própria a partir de `develop`:
  - Padrão: `claude/feat/[nome-da-implementacao]` (kebab-case, descritivo).
- PRs são abertos **de** `claude/feat/...` **para** `develop`.
- `main` recebe merge apenas a partir de `develop` em releases (não direto de feature).

## Ciclo de trabalho por issue

1. Sempre use a branch 'develop' como base
2. Baixe ou atualize ela localmente
3. Selecione a issue atribuída (ou a próxima da fila de onboarding).
4. Crie a branch `claude/feat/[nome]` a partir de `develop`.
5. Implemente, com testes unitários **e** de integração.
6. Rode `dotnet test` — só prossiga se tudo passar.
7. Faça commit e push da branch.
8. Abra o PR para `develop`, referenciando a issue (`Closes #<n>`).
9. **Pare e aguarde a aprovação humana do PR.** Não faça merge sozinho.
10. Após o PR ser aprovado e mergeado, a issue é fechada.
11. Pegue a próxima issue e repita.

## Regras de domínio (resumo — ver escopo e schema completos)

- `owner_id` em todo o domínio é FK para `app_user.id` (BIGINT interno), **nunca** o uid do Firebase.
- `firebase_uid` fica isolado em `app_user`; nunca vaza para o domínio.
- `app_user` é provisionado just-in-time no primeiro login, não por CRUD.
- Lançamentos (`bill_entry`/`income_entry`) carregam **snapshot** de `planned_amount` e `split_ratio` — nunca FK para o valor do molde. Isso garante a imutabilidade.
- Recálculo de reajuste só afeta meses **não pagos** a partir do mês informado; passado pago é congelado.
- "A receber" vive dentro do `bill_entry` (campos `person_id`, `received`, `received_date`), sem entidade separada.
- `split_ratio` é a fração que é **minha**: 1.0 = só minha, 0.5 = dividida, 0.0 = passa por mim.

## Convenções

- Migrations versionadas pelo EF Core; nunca alterar migration já aplicada.
- Validação de entrada em todos os endpoints.
- Filtro global por `owner_id` nas queries (isolamento por usuário na aplicação).

## Setup local de testes

Os testes de integração apontam para um banco PostgreSQL real no Neon (banco `bills_test`).

Configure a connection string antes de rodar `dotnet test`:
```bash
dotnet user-secrets set "ConnectionStrings:NeonTest" "<connection-string-do-neon-test>" \
  --project tests/BillsBackend.IntegrationTests
```

No CI, a connection string vem do GitHub Secret `NEON_TEST_CONNECTION_STRING`.
