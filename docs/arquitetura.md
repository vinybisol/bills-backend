# Arquitetura

Clean Architecture em 4 projetos. Dependências apontam **para dentro**:

```
            ┌──────────────┐
            │     Api      │  Minimal API, filtros, contratos de request, composition root
            └──────┬───────┘
                   │ referencia
          ┌────────▼────────┐        ┌──────────────┐
          │   Application   │◄───────│     Data     │  EF Core, repositórios, migrations
          └────────┬────────┘        └──────┬───────┘
                   │                        │
            ┌──────▼────────────────────────▼──┐
            │              Domain              │  entidades, Result, cálculos puros
            └──────────────────────────────────┘
```

- `Domain` não depende de nenhum projeto.
- `Application` depende só de `Domain`. Não conhece EF Core nem HTTP.
- `Data` implementa as abstrações de `Application` (repositórios, `IUnitOfWork`).
- `Api` depende de `Application`. Só o `Program.cs` (composition root) usa `Data`, para registrar DI e rodar `IMigrationService` em Development.

Por quê: ver `docs/decisoes.md` (Clean Architecture, contrato HTTP padronizado).

## Estrutura de pastas

```
BillsBackend.slnx
Directory.Packages.props          # versões de pacotes (Central Package Management)
global.json                       # dotnet test usa Microsoft.Testing.Platform
nuget.config                      # restore só do nuget.org
src/
├─ Domain/
│  ├─ Abstractions/
│  │  ├─ Result.cs                # Result, Result<T>, Error, ValidationError, ErrorType
│  │  └─ Filters/ICurrentOwner.cs # owner da requisição (app_user.id)
│  ├─ Calculations/               # funções puras (sem banco/HTTP)
│  │  ├─ EntryCalculations.cs     # effective, myShare, receivable, período, variação
│  │  ├─ EntryAggregations.cs     # totais, recebíveis, agregação por categoria/mês
│  │  ├─ BalanceCalculations.cs   # saldos otimista / pior caso / realizado
│  │  ├─ BillHistoryCalculations.cs
│  │  ├─ BillRecalculation.cs     # reajuste só em não pagos
│  │  └─ ProjectionCalculations.cs
│  ├─ Entities/                   # AppUser, Category, Person, PersonAccessLink,
│  │                              # Bill, Income, BillEntry, IncomeEntry
│  ├─ Enums/                      # BillKindEnum, IncomeKindEnum
│  └─ Infrastructures/AppOptions.cs
├─ Application/
│  ├─ Abstractions/
│  │  ├─ Services/                # IXService (contratos públicos dos casos de uso)
│  │  ├─ Repositories/            # IXRepository, IUnitOfWork, Strategies/IPagedQuery
│  │  └─ Exceptions/              # UniqueConstraintViolationException
│  ├─ DTOs/
│  │  ├─ Services/                # DTOs de saída (XDto)
│  │  └─ PagedQueryDto.cs
│  ├─ Services/                   # XService (internal sealed) + EntryValidation
│  └─ DependencyInjection/Register.cs   # RegisterApplications
├─ Data/
│  ├─ Contexts/                   # AppDbContext (filtros globais), factory de design-time,
│  │                              # MigrationService, NeonConnectionString
│  ├─ Repositories/               # XRepository (internal sealed), UnitOfWork
│  ├─ Migrations/                 # EF Core (nunca alterar migration aplicada)
│  └─ DependencyInjection/Register.cs   # RegisterData
└─ Api/
   ├─ Program.cs                  # composition root, auth, CORS, grupo /api/v1
   ├─ Endpoints/                  # XEndpoints.cs — um arquivo por feature
   ├─ Contracts/                  # records de request (e respostas exclusivas da Api)
   ├─ Filters/                    # UserEndpointFilter, CurrentOwner
   ├─ Identity/                   # FirebaseAuthOptions, FirebaseClaims
   └─ Extensions/ResultExtensions.cs    # Result → IResult (ProblemDetails)
tests/
├─ Directory.Build.props          # config comum de testes (NUnit + MTP + coverage)
├─ UnitTests/                     # um projeto por camada — ver docs/testes.md
└─ IntegrationTests/Api.IntegrationTests/
```

## Fluxo de uma requisição

```
HTTP ──► ExceptionHandler* ──► StatusCodePages ──► JwtBearer (Firebase) ──► UserEndpointFilter ──► Endpoint ──► XService ──► IXRepository ──► AppDbContext
                                      │                                  │
                                      │ provisiona app_user (JIT)        │ Result<T>
                                      │ ICurrentOwner.SetCurrentOwnerId  ▼
                                      └────────────────────────── ResultExtensions.ToHttpResult ──► resposta
```

0. **Erros → ProblemDetails** (`Program.cs`): `AddProblemDetails` preenche `instance` (caminho) e `traceId` em todo `ProblemDetails`. `UseExceptionHandler()` (*só fora de Development*; em Development fica a página de exceção do desenvolvedor) transforma exceção não tratada em 500 `ProblemDetails` sem stack trace. `UseStatusCodePages()` (antes da autenticação) dá corpo `ProblemDetails` a 4xx/5xx sem corpo: 401 do `JwtBearer`/`UserEndpointFilter`, 404 de rota, 405, 415 e 400 de binding (JSON malformado, enum desconhecido).
1. **Autenticação**: `JwtBearer` valida o token do Firebase (issuer/audience do projeto). Todo o grupo `/api/v1` exige autorização, exceto rotas marcadas `AllowAnonymous`.
2. **`UserEndpointFilter`**: lê o `firebase_uid` das claims, chama `IUserProvisioningService.GetOrCreateAsync` (cria o `app_user` no primeiro acesso) e grava o id interno em `ICurrentOwner`. Sem uid → 401. É o **único** lugar da Api que lê claims.
3. **Endpoint**: recebe request, serviço e `CancellationToken`; chama o serviço e devolve `result.ToHttpResult()` (ou `Results.Created` na criação).
4. **Serviço** (`Application`): valida a entrada, aplica as regras de negócio e usa as entidades e `Domain/Calculations`. Retorna `Result`/`Result<T>`, nunca lança exceção por erro de negócio. Grava via `IUnitOfWork.SaveChangesAsync`.
5. **Repositório** (`Data`): consultas EF. O `AppDbContext` aplica filtro global `OwnerId == ICurrentOwner.Id` (e `Active` nos moldes/cadastros). Consultas de leitura podem projetar direto para DTO.
6. **`UnitOfWork`** traduz violação de UNIQUE do Postgres (`23505`) em `UniqueConstraintViolationException`, que o serviço converte em 409.

> ⚠️ `IgnoreQueryFilters()` desliga **todos** os filtros globais da consulta, inclusive o de owner. Quando for necessário (ex.: resolver nome de molde/pessoa desativado), filtre `OwnerId` explicitamente.

## Contrato HTTP (`ResultExtensions.ToHttpResult`)

| Resultado do serviço | HTTP |
|---|---|
| `Result` sucesso | 204 |
| `Result<T>` sucesso | 200 + corpo |
| `Result<IEnumerable<T>>` sucesso | 200, ou **204 se vazio** |
| Criação (no endpoint) | 201 + `Location` |
| `ValidationError` | 400 `ValidationProblem` (`errors` por campo) |
| `Error.Validation` (erro simples, sem campo) | 400 `ProblemDetails` (`code` = `Error.Validation`, sem `errors`) |
| `Error.NotFound` | 404 `ProblemDetails` |
| `Error.Conflict` (duplicado, lançamento congelado) | 409 `ProblemDetails` |
| `Error.Unauthorized` / `Error.Forbidden` | 401 / 403 `ProblemDetails` |
| Outros | 500 `ProblemDetails` |
| Exceção não tratada (exception handler, fora de Development) | 500 `ProblemDetails` genérico |
| 4xx/5xx sem corpo do framework (status code pages) | `ProblemDetails` com o status |

Todas as respostas de erro são RFC 9457 (`application/problem+json`): `title` = título padrão do status, `detail` = mensagem do erro, extensão `code` = código do erro (`Error.Code`), `instance` = caminho, `traceId` = id de correlação; `ValidationProblem` também tem `errors` por campo (e `code` = `Error.Validation`). Contrato completo em `docs/api.md` › "Contrato de erro".

## Mapa das features

| Feature | Endpoint (`src/Api/Endpoints`) | Serviço (`src/Application/Services`) | Repositórios (`src/Data/Repositories`) |
|---|---|---|---|
| Health / Me | `UserEndpoints` | `AppUserService`, `UserProvisioningService` | `AppUserRepository` |
| Categorias | `CategoryEndpoints` | `CategoryService` | `CategoryRepository` |
| Pessoas | `PersonEndpoints` | `PersonService` | `PersonRepository` |
| Links de acesso | `PersonAccessLinksEndpoint`, `SharedBillsEndpoint` (anônimo) | `PersonAccessLinksService` | `PersonAccessLinksRepository`, `PersonRepository` |
| Receitas (molde) | `IncomeEndpoints` | `IncomeService` | `IncomeRepository` |
| Contas (molde), recálculo, histórico | `BillEndpoints` | `BillService` | `BillRepository`, `BillEntryRepository` |
| Projeção anual | `ProjectionEndpoints` | `ProjectionService` | `BillRepository`, `IncomeRepository`, `BillEntryRepository`, `IncomeEntryRepository` |
| Lançamentos | `EntryEndpoints` | `EntryService` (listagem), `BillEntryService`, `IncomeEntryService` | `BillEntryRepository`, `IncomeEntryRepository` |
| A receber | `ReceivablesEndpoints` | `ReceivablesService` | `BillEntryRepository`, `PersonRepository` |
| Dashboards | `DashboardEndpoints` | `DashboardService` | `BillEntryRepository`, `IncomeEntryRepository` |

## Como adicionar uma feature

Siga `CategoryEndpoints` → `CategoryService` → `CategoryRepository` como referência.

1. **Domain**: entidade com estado encapsulado (construtor privado, factory `Create`, métodos de comportamento, soft delete via `Deactivate`). Lógica pura em `Calculations/`. Testes em `Domain.UnitTests`.
2. **Repositório**: `IXRepository` em `Application/Abstractions/Repositories`; `XRepository` `internal sealed` em `Data/Repositories`; registrar em `RegisterData`. Se a entidade for nova, mapear no `AppDbContext` (snake_case + filtro por owner) e gerar migration.
3. **Serviço**: `IXService` em `Application/Abstractions/Services`; `XService` `internal sealed` com construtor primário (`IXRepository`, `ICurrentOwner`, `TimeProvider`, `IUnitOfWork`); retorna `Result<XDto>`; DTO em `Application/DTOs/Services`; registrar em `RegisterApplications`. Testes com NSubstitute em `Application.UnitTests`.
4. **Endpoint**: `XEndpoints.cs` com `group.MapGroup("/x").AddEndpointFilter<UserEndpointFilter>()`; request em `Api/Contracts`; encadear o `MapXEndpoints()` em `Program.cs`.
5. **Integração**: `Api.IntegrationTests/Endpoints/XEndpointTests.cs` herdando `IntegrationTestBase`.
6. **Docs**: `docs/api.md` (contrato) e, se houver regra nova, `docs/dominio.md`.
