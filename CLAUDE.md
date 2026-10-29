# CLAUDE.md — Backend (Orçamento Pessoal)

API do sistema de orçamento pessoal. Este arquivo define como os agentes devem trabalhar neste repositório. **Siga estas regras em toda tarefa.**

## Stack

- .NET 10, Minimal APIs, EF Core
- PostgreSQL (Neon) — connection string do **pooler** + `SSL Mode=Require`
- Autenticação: validação de JWT do Firebase; identidade própria via `app_user` (ver schema)
- Hospedagem: Azure Web App (F1)

## ADRs (fonte das regras)

As decisões de arquitetura vivem no repositório de ADRs (servidor MCP `adrs`; 19 ADRs aceitas). **As ADRs são a regra**: código novo segue a ADR; onde o código atual ainda diverge, a divergência está registrada como issue e listada em `docs/decisoes.md` › "Pendências". Não "corrija" o código para o comportamento antigo nem replique a divergência em código novo.

- Antes de uma decisão de arquitetura nova, busque nas ADRs (`adrs-search_adrs`). Para criar/alterar ADR siga a ADR 11 (MCP primeiro, linkar relacionadas, atualizar o índice, conferir o status no corpo depois de `update_status`, rodar `run_doctor` ao final).
- Índice das ADRs e como cada uma se aplica aqui: `docs/decisoes.md`.

## Arquitetura

Clean Architecture em 4 projetos (`src/`) — **ADR 1**; detalhes, árvore de pastas e fluxo em `docs/arquitetura.md`. Dependências apontam **para dentro**: `Api → Application → Domain` e `Data → Application/Domain`. `Domain` não depende de ninguém; `Application` não conhece EF Core; só `Api/Program.cs` (composition root) referencia `Data` (só para registro de DI). Uma feature cria só os elementos de que precisa — não é obrigatório tocar todas as camadas nem criar abstração sem fronteira real. Testes de arquitetura automatizados no CI são exigidos pela ADR 1 (pendência #88).

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

**Regras transversais (ADRs):**
- **Result (ADR 2)**: falha esperada é `Result`/`Result<T>` com `Error { code, message }` estável; exceção só para o inesperado. Só traduza exceção de infraestrutura em falha quando o significado for conhecido (ex.: UNIQUE `23505` → 409); nunca capture e converta indiscriminadamente.
- **Repositório + Unit of Work (ADR 5)**: repositórios não fazem commit; o serviço chama `IUnitOfWork.SaveChangesAsync` uma vez por caso de uso (leitura não comita). Contratos não expõem tipos do EF (`IQueryable`, `DbSet`...).
- **Tempo (ADR 7)**: nunca leia o relógio do sistema (`DateTime.Now/UtcNow`, `DateTimeOffset.Now`); use `TimeProvider` injetado na `Application` e passe instantes/datas ao `Domain`. Capture **um** instante de referência por operação. Instantes em UTC; datas de negócio como `DateOnly`; mês/data corrente derivados no **fuso de negócio explícito**, nunca no fuso do servidor; defina se igualdade com a expiração = expirado. Testes com `FixedTimeProvider`. (Fuso de negócio e borda de expiração ainda pendentes: #91.)
- **Configuração (ADR 4)**: toda configuração nova é um `IOptions<T>` com `ValidateDataAnnotations()` (ou validação própria) + `ValidateOnStart()`; nada de ler `IConfiguration`/env var solta no código; sem default silencioso para valor inválido; segredos nunca no repositório nem em log.
- **Readiness (ADR 10)**: `/health/live` = liveness (não depende de dependência externa); `/health/ready` = readiness (PostgreSQL é dependência **crítica** → 503). Check leve, com timeout, sem efeito colateral (nunca cria dado nem aplica migration). Toda dependência externa nova deve ser classificada como crítica ou opcional, com o comportamento degradado documentado.
- **Versão em runtime (ADR 19)**: a versão vem só do build (`AssemblyInformationalVersionAttribute`), nunca de constante/arquivo editado à mão (pendência #82).

**Contrato HTTP padronizado** (`ResultExtensions.ToHttpResult`):
- Sucesso: `Result` → 204; `Result<T>` → 200 com o corpo; `Result<IEnumerable<T>>` → **200 com o array, inclusive vazio (`[]`)** — **ADR 9**: nunca 204 por lista vazia, sem envelope. Criação usa `Results.Created` no endpoint. ⚠️ O código atual ainda devolve 204 para lista vazia (pendência #86): endpoint novo segue a ADR; a migração dos existentes é feita de uma vez na #86 (breaking change para o frontend).
- Falha: `ValidationError` → 400 `ValidationProblem` (erros agrupados por código); `NotFound` → 404, `Conflict` → 409 (ex.: editar/pagar lançamento congelado), `Unauthorized` → 401, `Forbidden` → 403 — todos como `ProblemDetails` RFC 9457 (**ADR 3**; `application/problem+json`; `title` = título padrão do status, `detail` = mensagem segura, extensão `code` = código estável do erro, extensão `traceId`; `type` estável por tipo de problema; `instance` opcional e por ocorrência, sem dados sensíveis da URL). Clientes decidem por `type`/`code`, nunca por `title`/`detail`. Alinhamento de `type`/`instance` pendente (#90).
- Sem token / token sem uid → 401. Erros do framework (401, rota 404/405, 415, 400 de binding) e exceções não tratadas (500 genérico, sem mensagem de exceção nem stack trace) também saem como `ProblemDetails` (ver `docs/api.md` › "Contrato de erro").

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
- **Dados de teste (ADR 8)**: use **Bogus** para dados incidentais realistas (nomes, valores em faixa explícita), com **seed local** por gerador/teste — nunca estado aleatório global. Todo valor que decide o resultado esperado é **explícito** (bordas testadas deliberadamente, ex.: 17/18/19). Datas de referência explícitas (`FixedTimeProvider`). Entidades sempre via factory (`Create`/`Provision`). Nunca dados reais de produção. NSubstitute só para comportamento de dependências, não para gerar dados. (Bogus ainda não instalado: #89.)
- Comportamento dependente de tempo (ADR 7): teste viradas de dia/mês e a igualdade exata com o instante de expiração.

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

## Git flow (ADRs 15 e 16)

Git Flow simplificado: dois branches permanentes e dois tipos de branch curto. Sem branches `release/*` por padrão.

| Branch | Papel | Sai de | Entra em | Estratégia de merge |
|---|---|---|---|---|
| `main` | Código liberado. Todo merge em `main` corresponde a uma release. | — | — | — |
| `develop` | Integração; tudo aqui vai na próxima release. | `main` (uma vez) | `main` | **merge commit** |
| `feature/<nome>` | Uma mudança (kebab-case, descritivo). | `develop` | `develop` | **squash** |
| `hotfix/<nome>` | Correção urgente do que está em produção. | `main` | `main`, depois back-merge em `develop` | **merge commit** |

- Nunca faça squash nem rebase entre `develop` e `main` (em nenhuma direção): a tag de release precisa continuar alcançável a partir de `develop`.
- Depois de toda release ou hotfix: **back-merge `main` → `develop` com merge commit** (conferir com `git merge-base --is-ancestor vX.Y.Z develop`).
- Sem cherry-pick entre os branches permanentes. O que não está pronto não entra em `develop` (ou entra atrás de feature flag).
- Como o PR de feature vira um commit só, o **título do PR** é um resumo descritivo no imperativo; os commits internos podem ser informais. Não há convenção de mensagem de commit obrigatória.

## Changelog, versão e release (ADRs 12, 13, 14, 17, 18, 19)

- **`CHANGELOG.md`** (raiz) é escrito por pessoas, no formato Keep a Changelog 1.1.0. O PR que traz uma mudança **observável pelo consumidor** (endpoint/campo novo, comportamento alterado, bug perceptível, remoção, correção de vulnerabilidade, mudança perceptível de performance) adiciona a entrada em `## [Unreleased]`, na categoria certa (`Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, `Security`). Descreva o **efeito para o consumidor**, não a implementação (“Exportar contas em CSV”, não “Adiciona ExportService”). Testes, refactor sem mudança de comportamento, pipeline/build e atualização rotineira de dependência **não** entram.
- **Versão**: SemVer 2.0.0, definida **só** pela tag anotada `vMAJOR.MINOR.PATCH` em `main`. **Nunca** coloque `<Version>` em `.csproj`/`Directory.Build.props`: a pipeline injeta a versão. Tag publicada é imutável (erro → nova versão).
- **Escolha do incremento** (pessoa, no momento da release, pelas categorias em `[Unreleased]`): `Removed` ou `Changed` que quebra contrato → MAJOR (MINOR enquanto `0.y.z`); `Added`/`Deprecated` → MINOR; só `Fixed`/`Security` → PATCH. `[Unreleased]` vazio = nada a liberar.
- **Release**: revisar `[Unreleased]` → renomear para `## [X.Y.Z] - AAAA-MM-DD` → novo `[Unreleased]` vazio → atualizar links de comparação → merge `develop` → `main` (merge commit) → `git tag -a vX.Y.Z` no `main` e `git push origin vX.Y.Z` → back-merge `main` → `develop`. A pipeline de release dispara **só pela tag**, compila **uma vez**, publica a GitHub Release com as notas da seção do changelog e promove **o mesmo artefato** entre ambientes, condicionado a readiness (ADR 10).
- **Pré-release**: todo push em `develop` gera `<última tag>-dev.<run>+<sha>` (sem tag: `0.0.0-dev...`), descartável (7 dias), só para ambientes de desenvolvimento.
- A versão injetada é exposta em runtime (health e logs) — ADR 19.
- ⚠️ Pipelines de versão/release ainda não seguem isso: hoje `cd-prod.yml` publica a cada push em `main` e não há versão injetada (pendências #82, #83, #85).

## Ciclo de trabalho por issue

1. Sempre use a branch `develop` como base.
2. Baixe ou atualize ela localmente.
3. Selecione a issue atribuída (ou a próxima da fila de onboarding).
4. Crie a branch `feature/[nome]` a partir de `develop` (ou `hotfix/[nome]` a partir de `main`, só para correção urgente de produção).
5. Implemente, com testes unitários **e** de integração.
6. Se a mudança for observável pelo consumidor, adicione a entrada em `CHANGELOG.md` › `[Unreleased]`.
7. Rode `dotnet test` — só prossiga se tudo passar.
8. Faça commit e push da branch.
9. Abra o PR para `develop` (hotfix: para `main`), com título descritivo no imperativo, referenciando a issue (`Closes #<n>`).
10. **Pare e aguarde a aprovação humana do PR.** Não faça merge sozinho (o merge de feature é squash).
11. Após o PR ser aprovado e mergeado, a issue é fechada.
12. Pegue a próxima issue e repita.

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

- Migrations versionadas pelo EF Core; nunca alterar migration já aplicada (ADR 6).
- Migrations são aplicadas **só pelo CD**, num passo explícito antes de promover a nova versão; **nunca** no startup nem em health check. Localmente, rode manualmente `dotnet ef database update --project src/Data`. Migration aplicada com a versão anterior ainda no ar precisa ser compatível com ela (mudança incompatível = expand/contract em etapas). Migration falhou → para o deploy e investiga; sem rollback automático. (Hoje o `Program.cs` ainda migra em Development: pendência #87; bundle no CD: #83.)
- Validação de entrada em todos os endpoints.
- Filtro global por `owner_id` nas queries (isolamento por usuário na aplicação).

## Setup local de testes

Os testes de integração precisam de um PostgreSQL (banco `bills_test`). A connection string vem de `ConnectionStrings:Neon`: localmente, do arquivo **git-ignored** `tests/IntegrationTests/Api.IntegrationTests/appsettings.Testing.Local.json` (copiado para o output no build; modelo versionado em `appsettings.Testing.Local.example.json`); no CI, da env var `ConnectionStrings__Neon`, que tem precedência sobre o arquivo.

**Recomendado — Postgres local via Docker** (rápido, sem depender do Neon):
```bash
docker compose up -d
cp tests/IntegrationTests/Api.IntegrationTests/appsettings.Testing.Local.example.json \
   tests/IntegrationTests/Api.IntegrationTests/appsettings.Testing.Local.json
dotnet test
```
> Use o formato **key-value** (como no exemplo), não a URI `postgresql://...`: assim `NeonConnectionString.Normalize` devolve a string intacta e não força `SSL Mode=Require`, que o Postgres local não tem.

**Alternativa — Neon:** aponte `ConnectionStrings:Neon` (no arquivo local ou na env var) para um banco de teste no Neon (formato URI, com SSL).

No **CI**, os testes rodam contra um **service container** de PostgreSQL (ver `.github/workflows/ci.yml`) — sem segredo externo nem custo adicional.

## Documentação (ler sob demanda)

- `docs/arquitetura.md` — árvore de pastas, fluxo da requisição, mapa por feature, passo a passo para nova feature.
- `docs/testes.md` — layout dos projetos de teste, infra de integração, exemplos e comandos MTP.
- `docs/dominio.md` — conceitos, regras e fluxos completos.
- `docs/schema.md` — tabelas, colunas, constraints (SQL completo em `docs/schema.sql`).
- `docs/api.md` — endpoints e contratos.
- `docs/decisoes.md` — índice das ADRs (como cada uma se aplica aqui), decisões específicas do projeto e pendências (código × ADR).
- `CHANGELOG.md` — mudanças observáveis pelo consumidor, por versão (ADR 12).
