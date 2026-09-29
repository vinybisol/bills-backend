# CLAUDE.md — Backend (Orçamento Pessoal)

API do sistema de orçamento pessoal. Este arquivo define como os agentes devem trabalhar neste repositório. **Siga estas regras em toda tarefa.**

## Stack

- .NET 10, Minimal APIs, EF Core
- PostgreSQL (Neon) — connection string do **pooler** + `SSL Mode=Require`
- Autenticação: validação de JWT do Firebase; identidade própria via `app_user` (ver schema)
- Hospedagem: Azure Web App (F1)

## Arquitetura

Clean Architecture em 4 projetos (`src/`); detalhes, árvore de pastas e fluxo em `docs/arquitetura.md`. Dependências apontam **para dentro**: `Api → Application → Domain` e `Data → Application/Domain`. `Domain` não depende de ninguém; `Application` não conhece EF Core; só `Api/Program.cs` (composition root) referencia `Data` (registro de DI + `IMigrationService`).

| Camada | O que vai lá |
|---|---|
| `Domain` | Entidades (estado encapsulado, factories `Create`/`Provision`, métodos de comportamento), enums, `Result`/`Error`, `ICurrentOwner`, cálculos puros em `Domain/Calculations`. |
| `Application` | `Abstractions/Services` (`IXService`), `Abstractions/Repositories` (`IXRepository`, `IUnitOfWork`), DTOs de saída em `DTOs/Services`, implementações `internal sealed` em `Services/`, registro em `DependencyInjection/Register.cs`. Regras de negócio e validação retornam `Result`/`Result<T>`. |
| `Data` | `AppDbContext` (filtro global por `owner_id` via `ICurrentOwner`), migrations, repositórios EF `internal sealed` em `Repositories/`, `UnitOfWork`, registro em `DependencyInjection/Register.cs`. |
| `Api` | Endpoints Minimal API finos em `Endpoints/<Feature>Endpoints.cs`, requests em `Contracts/`, `Filters/UserEndpointFilter` (resolve/provisiona o `app_user` e define o owner), `Extensions/ResultExtensions.ToHttpResult`. |

**Padrão para nova feature** (siga `CategoryEndpoints`/`CategoryService`/`CategoryRepository`):
1. Entidade/regra no `Domain` (+ testes em `Domain.UnitTests`).
2. `IXRepository` em `Application/Abstractions/Repositories` + implementação em `Data/Repositories`; registre em `RegisterData`.
3. `IXService` + `XService` (`internal sealed`, injeta `ICurrentOwner` quando precisa do owner) retornando `Result<Dto>`; registre em `RegisterApplications` (+ testes com NSubstitute em `Application.UnitTests`).
4. Endpoint: `group.MapGroup("/x").AddEndpointFilter<UserEndpointFilter>()`, handler só recebe request/serviço/`CancellationToken` e devolve `result.ToHttpResult()`. Nunca leia claims do Firebase nem use `AppDbContext` no endpoint. Só endpoints intencionalmente anônimos (ex.: `SharedBillsEndpoint`) ficam sem o filtro.
5. Testes de integração em `Api.IntegrationTests/Endpoints/XEndpointTests.cs`.

**Contrato HTTP padronizado** (`ResultExtensions.ToHttpResult`):
- Sucesso: `Result` → 204; `Result<T>` → 200 com o corpo; `Result<IEnumerable<T>>` → 200, ou **204 quando a lista é vazia**. Criação usa `Results.Created` no endpoint.
- Falha: `ValidationError` → 400 `ValidationProblem` (erros agrupados por código); `NotFound` → 404, `Conflict` → 409 (ex.: editar/pagar lançamento congelado), `Unauthorized` → 401, `Forbidden` → 403 — todos como `ProblemDetails` (`title` = código, `detail` = mensagem).
- Sem token / token sem uid → 401.

## Agente e skills

- **Sempre** use o agente `csharp-dotnet-expert` para tarefas de código deste repo.
- **Sempre** use as skills de .NET disponíveis.

## Testes (obrigatório)

- Framework: **NUnit 4** + **NSubstitute** (mocks) + `Assert.That`, no **Microsoft.Testing.Platform (MTP)**. Versões centralizadas em `Directory.Packages.props` (CPM); configuração comum em `tests/Directory.Build.props` (e `tests/UnitTests/Directory.Build.props`).
- Layout dos projetos de teste:
  - `tests/UnitTests/Domain.UnitTests` — entidades e `Domain/Calculations`.
  - `tests/UnitTests/Application.UnitTests` — serviços, com repositórios/`ICurrentOwner` substituídos via NSubstitute.
  - `tests/UnitTests/Api.UnitTests` — `UserEndpointFilter`, `ResultExtensions`, claims do Firebase.
  - `tests/UnitTests/Data.UnitTests` — utilitários da camada de dados (ex.: `NeonConnectionString`).
  - `tests/IntegrationTests/Api.IntegrationTests` — endpoints ponta a ponta (JWT, filtro, banco real).
- **Toda** tarefa que adiciona ou altera comportamento deve incluir:
  - **Testes unitários** da lógica (regras de negócio, cálculos de split, projeção, recálculo).
  - **Testes de integração** dos endpoints (incluindo autenticação e acesso ao banco).
- **Nunca** abra um PR com testes falhando ou sem cobertura para o que foi implementado.

### Ciclo de teste rápido (iteração)

Os testes de integração rodam contra um Postgres real — a suíte completa é lenta. **Durante o desenvolvimento, não rode `dotnet test` cheio a cada mudança.** Rode só o subconjunto relevante para encurtar o feedback.

> Com MTP (`global.json` → `"runner": "Microsoft.Testing.Platform"`), sempre aponte o projeto com `--project <caminho>` e ponha o filtro depois (`dotnet test --project <caminho> --filter ...`). `dotnet test --filter ...` **sem** `--project` roda na solução inteira: cada projeto sem teste correspondente reporta "Zero tests ran" (exit code 8) e o comando falha — além de subir o projeto de integração à toa.

- Só os unitários (sem banco, segundos), um projeto por camada em `tests/UnitTests/`:
  `dotnet test --project tests/UnitTests/Domain.UnitTests`, `dotnet test --project tests/UnitTests/Application.UnitTests`, `dotnet test --project tests/UnitTests/Api.UnitTests`, `dotnet test --project tests/UnitTests/Data.UnitTests`
- Só os de integração (um único projeto, precisa do Postgres): `dotnet test --project tests/IntegrationTests/Api.IntegrationTests`
- Só a fixture da feature: `dotnet test --project tests/IntegrationTests/Api.IntegrationTests --filter FullyQualifiedName~ProjectionEndpointTests`
- Várias fixtures: `dotnet test --project tests/IntegrationTests/Api.IntegrationTests --filter "FullyQualifiedName~MeEndpointTests|FullyQualifiedName~HealthEndpointTests"`
- Por nome de teste: `dotnet test --project tests/IntegrationTests/Api.IntegrationTests --filter Name~Idempot`
- Um serviço nos unitários: `dotnet test --project tests/UnitTests/Application.UnitTests --filter FullyQualifiedName~AppUserServiceTests`

**Só antes de abrir o PR** rode a suíte completa (`dotnet test`, na raiz — usa `BillsBackend.slnx`) **uma vez**. PR só é aberto com a suíte inteira verde.

### Isolamento dos testes de integração

Cada teste usa um `firebase_uid` (e portanto um `owner_id`) **distinto**; o filtro global por owner isola os dados sem precisar limpar o banco entre testes. Por isso o reset do banco (Respawn) roda **uma vez por fixture** (no `[OneTimeSetUp]`), não por teste. Ao criar uma nova fixture de integração, herde de `IntegrationTestBase` ou siga esse mesmo padrão — **nunca** adicione um `[SetUp]` que reseta o banco a cada teste.

- Todos os testes de integração vivem em **um único projeto**: `tests/IntegrationTests/Api.IntegrationTests` (NUnit no Microsoft.Testing.Platform). Infra compartilhada em `Infrastructure/` (`CustomWebApplicationFactory`, `TestTokens`, `IntegrationTestBase`); uma fixture por feature em `Endpoints/<Feature>EndpointTests.cs`; dados de `TestCaseSource` em `TestSupport/`.
- Para um uid único por teste, use `CreateAuthenticatedClient()` (gera um uid via `NewFirebaseUid()`) ou passe um uid próprio e distinto em `TestTokens.CreateValidToken(...)`.
- O assembly é `[assembly: NonParallelizable]` (ver `AssemblyInfo.cs`): as fixtures rodam em sequência, porque o Respawn de uma fixture apagaria os dados de outra em execução. Não adicione `[Parallelizable]` nem crie outro projeto de integração apontando para o mesmo banco.

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

## Invariantes de domínio (NUNCA violar)

- `owner_id` em todo o domínio é FK para `app_user.id` (BIGINT interno), **nunca** o uid do Firebase. Filtro global por `owner_id` nas queries; nunca cruzar dados entre usuários.
- `firebase_uid` fica isolado em `app_user`; nunca vaza para o domínio.
- `app_user` é provisionado just-in-time no primeiro login, não por CRUD.
- Lançamentos (`bill_entry`/`income_entry`) carregam **snapshot** de `planned_amount` e `split_ratio_snapshot` — nunca FK para o valor do molde. Isso garante a imutabilidade.
- Lançamento **pago/recebido é congelado**: não editar `planned_amount`/`actual_amount`/`split_ratio_snapshot`. Só a ação dedicada unpay/unreceive descongela. Editar congelado → 409.
- Recálculo de reajuste só afeta lançamentos **não pagos** do mês informado em diante; passado pago é congelado.
- "A receber" vive dentro do `bill_entry` (campos `person_id`, `received`, `received_date`), sem entidade separada.
- `bill_entry.paid` (eu paguei) ≠ `bill_entry.received` (a pessoa me pagou de volta). Independentes.
- `split_ratio` é a fração que é **minha**: 1.0 = só minha, 0.5 = dividida, 0.0 = passa por mim. `split < 1` exige `person_id`; `= 1` proíbe.
- Moldes usam **soft delete** (`active=false`), nunca exclusão física.

## Convenções

- Migrations versionadas pelo EF Core; nunca alterar migration já aplicada.
- Validação de entrada em todos os endpoints.
- Filtro global por `owner_id` nas queries (isolamento por usuário na aplicação).

## Setup local de testes

Os testes de integração precisam de um PostgreSQL (banco `bills_test`). A connection string vem de `ConnectionStrings:NeonTest` (user-secrets local ou a env var `ConnectionStrings__NeonTest`).

**Recomendado — Postgres local via Docker** (rápido, sem depender do Neon):
```bash
docker compose up -d
dotnet user-secrets set "ConnectionStrings:NeonTest" \
  "Host=localhost;Port=5432;Database=bills_test;Username=postgres;Password=postgres" \
  --project tests/IntegrationTests/Api.IntegrationTests
dotnet test
```
> Use o formato **key-value** (acima), não a URI `postgresql://...`: assim `NeonConnectionString.Normalize` devolve a string intacta e não força `SSL Mode=Require`, que o Postgres local não tem.

**Alternativa — Neon:** aponte `ConnectionStrings:NeonTest` para um banco de teste no Neon (formato URI, com SSL).

No **CI**, os testes rodam contra um **service container** de PostgreSQL (ver `.github/workflows/ci.yml`) — sem segredo externo nem custo adicional.

## Documentação (ler sob demanda)

- `docs/arquitetura.md` — árvore de pastas, fluxo da requisição, mapa por feature, passo a passo para nova feature.
- `docs/testes.md` — layout dos projetos de teste, infra de integração, exemplos e comandos MTP.
- `docs/dominio.md` — conceitos, regras e fluxos completos.
- `docs/schema.md` — tabelas, colunas, constraints (SQL completo em `docs/schema.sql`).
- `docs/api.md` — endpoints e contratos.
- `docs/decisoes.md` — por que cada decisão de arquitetura.
