# Decisões de arquitetura (por quê)

As decisões gerais vivem no repositório de **ADRs** (servidor MCP `adrs`) e são a **regra**: onde o código atual ainda não as segue, a divergência é uma pendência rastreada por issue (ver "Pendências" abaixo), não uma exceção aceita. Este arquivo tem três partes: o índice das ADRs com a aplicação neste repo, as decisões específicas do projeto (domínio, hospedagem) e as pendências.

## Índice das ADRs

| ADR | Decisão | Como se aplica aqui | Status no código |
|---|---|---|---|
| 1 | Clean Architecture em quatro camadas | `Domain`, `Application`, `Data`, `Api`; dependências para dentro; só `Program.cs` (composition root) referencia `Data`. Testes de arquitetura no CI. | ✅ camadas · ⚠️ testes de arquitetura (#88) |
| 2 | Result em vez de exceções para falhas esperadas | `Result`/`Result<T>` + `Error { Code, Message, Type }` em `Domain/Abstractions/Result.cs`; exceção só para o inesperado. | ✅ |
| 3 | Erros em RFC 9457 ProblemDetails | `application/problem+json` com `title`, `status`, `detail`, `code`, `traceId`; 500 genérico. `type` estável por tipo de problema; `instance` por ocorrência. | ✅ parcial · ⚠️ `type`/`instance` (#90) |
| 4 | Validar a configuração antes do startup terminar | `IOptions<T>` + `ValidateDataAnnotations()` + `ValidateOnStart()` (`FirebaseAuthOptions`, `SharedPagesOptions`, `ConnectionStringsOptions`). | ✅ |
| 5 | Repositórios + Unit of Work | `IXRepository`/`IUnitOfWork` em `Application`, implementação EF em `Data`; repositório não comita. | ✅ |
| 6 | Migrations no CD, não no startup | `dotnet ef database update` no job de deploy; local = manual. | ⚠️ startup em Development (#87) · bundle/artefato (#83) |
| 7 | Relógio injetável e fuso de negócio explícito | `TimeProvider` injetado, `FixedTimeProvider` nos testes. Fuso de negócio explícito e borda de expiração definida. | ✅ relógio · ⚠️ fuso/borda (#91) |
| 8 | NUnit + NSubstitute + MTP (escopo .NET) | Ver `docs/testes.md`; Bogus para dados sintéticos com seed local e valores decisivos explícitos. | ✅ · ⚠️ Bogus (#89) |
| 9 | Coleção bem-sucedida → 200 (inclusive vazia, `[]`) | `Result<IEnumerable<T>>` → 200 com array; sem envelope. | ⚠️ hoje 204 para vazia (#86) |
| 10 | Gate de deploy por readiness e degradação explícita | `/health/live` (liveness) × `/health/ready` (PostgreSQL crítico → 503). Readiness do candidato antes de receber tráfego. | ✅ endpoints · ⚠️ gate pré-tráfego (#85) |
| 11 | Processo de criação/manutenção de ADRs | Buscar antes de criar; uma decisão por ADR; linkar relacionadas; MCP `adrs` primeiro; `run_doctor` ao final. | Processo (repo de ADRs) |
| 12 | Changelog humano (Keep a Changelog) | `CHANGELOG.md` na raiz; entrada no PR que traz mudança observável. | ✅ arquivo · ⚠️ check de PR (#82) |
| 13 | SemVer derivado das categorias do changelog | Incremento escolhido no release pelas categorias de `[Unreleased]`. | ⚠️ sem versões ainda (#82) |
| 14 | Tag anotada `vX.Y.Z` como única fonte da versão | Nada de `<Version>` em projeto; pipeline injeta; tag imutável. | ⚠️ sem tags/injeção (#82) |
| 15 | Git Flow simplificado (`develop`/`main`) | `feature/*` de `develop`, `hotfix/*` de `main`; sem cherry-pick; sem `release/*` por padrão. | Processo (`CLAUDE.md` › Git flow) |
| 16 | Squash em feature, merge commit entre permanentes | `feature/*`→`develop` squash; `develop`→`main`, `hotfix/*`→`main` e back-merge `main`→`develop` com merge commit. | Processo (aplicado em review) |
| 17 | Release por tag e artefato construído uma vez | Pipeline só no push de `v*`; build único promovido entre ambientes; notas = seção do changelog. | ⚠️ hoje deploy por push em `main` (#85) |
| 18 | Pré-releases descartáveis a cada integração em `develop` | `<última tag>-dev.<run>+<sha>`; retenção de 7 dias; nunca promovidas. | ⚠️ (#82) |
| 19 | Versão do build exposta em runtime | Versão de `AssemblyInformationalVersionAttribute` no health e nos logs. | ⚠️ (#82) |

## Decisões específicas do projeto

Decisões de domínio e de hospedagem deste repositório, sem ADR própria (o repositório de ADRs registra decisões gerais).

- **Identidade própria (`app_user`), não o Firebase uid como PK.** Evita amarração ao provedor; trocar de auth mexe em uma coluna (`firebase_uid`), não no schema todo. `owner_id` é sempre o id interno.
- **Client-managed auth + JIT provisioning.** Front cria conta/loga no Firebase (SDK); senha nunca passa pela API; backend só valida JWT e provisiona `app_user` no 1º acesso. Sem Admin SDK.
- **Molde × lançamento separados, com snapshot.** `planned_amount`/`split_ratio_snapshot` copiados no `*_entry`, nunca FK. Base da imutabilidade: editar molde não reescreve o passado.
- **Imutabilidade por congelamento de pagos**, não por tabela de auditoria. Pago/recebido trava; unpay/unreceive descongela. Recálculo respeita o congelamento.
- **Sem entidades "a pagar/paga".** Estado é atributo (`paid`/`received`) do mesmo registro; duas tabelas criariam sincronização sem ganho.
- **A-receber dentro do `bill_entry`.** `person_id`+`received`+`received_date`; sem entidade de dívida. `paid` ≠ `received`.
- **Recebimento em lote, sem troco.** Valores exatos na prática; marcação em lote na UI, modelo continua conta-a-conta. Entidade de settlement descartada.
- **Orçamento anual, projeção idempotente.** UNIQUE(molde,ano,mês)+ON CONFLICT DO NOTHING; rerodar não duplica nem toca pagos. Virada de ano = projeção de ano+1 (default_amount já reflete reajustes).
- **Split como fração numérica** (não booleano). Cobre 1.0/0.5/0.0 e futuros sem refatorar.
- **Soft delete nos moldes.** Preserva integridade dos lançamentos históricos que os referenciam.
- **Isolamento na aplicação** (filtro por owner_id), não RLS — mais simples com EF Core. RLS fica para eventual multi-tenant sério.
- **Azure Web App F1 + Neon (pooler).** Free; ciente do cold start (migrar depois se incomodar). Neon pooler + SSL no EF Core.
- **Migrations aplicadas pela pipeline, não no startup** (ADR 6). `dotnet ef database update` roda no job `deploy` do CI/CD (usa a `AppDbContextFactory` já existente para tooling). Evita reaplicar a checagem de migration a cada cold start do Azure Web App F1 e separa responsabilidade de deploy da de runtime. Pela ADR 6, nem Development migra no startup (local = comando manual) — pendência #87.
- **Todos os endpoints sob `/api/v1`.** Antes havia mistura: CRUDs na raiz (`/bills`, `/categories`...) e o resto sob `/api/...`, sem versionamento. Padronizado com um único `var v1 = app.MapGroup("/api/v1")`, sem extrair para extension methods por recurso (ganho baixo frente ao tamanho do `Program.cs` atual; fica como possível follow-up). **Cutover direto, sem aliases das rotas antigas**: como é uma aplicação de uso pessoal com deploy único (Azure Web App + Firebase Hosting controlados pelo mesmo owner), o custo de manter um grupo de rotas legado em paralelo (roteamento duplicado, risco de divergência) superou o benefício; a coordenação foi feita casando o merge deste PR com o da migração do frontend (`bills-frontend#43`) para o mesmo prefixo.
- **Clean Architecture em camadas (`Api` → `Application` → `Domain`, `Data` → `Application`/`Domain`).** Antes os endpoints acessavam `AppDbContext` e liam claims diretamente, misturando HTTP, regra e persistência. Agora: `Domain` (entidades encapsuladas, `Result`, cálculos puros) não depende de nada; `Application` define serviços (`IXService`/`XService` `internal sealed`) e as abstrações de repositório/`IUnitOfWork`, sem conhecer EF Core; `Data` implementa repositórios EF e o `AppDbContext` (filtro global por `owner_id`); `Api` só tem endpoints finos, contratos de request e o `UserEndpointFilter`, que provisiona o `app_user` just-in-time e define o owner da requisição. `Program.cs` é o único ponto da `Api` que conhece `Data` (composition root: DI; ainda roda `IMigrationService` em Development — pendência #87). Ganho: regra testável sem banco e sem HTTP, e um padrão único para novas features (ver `CLAUDE.md` › Arquitetura).
- **Contrato HTTP padronizado via `Result` → `ProblemDetails`** (ADRs 2, 3, 9). Serviços retornam `Result`/`Result<T>` em vez de lançar exceção ou devolver `IResult`; o endpoint só chama `ToHttpResult()`. Mapeamento único: validação → 400 `ValidationProblem`; não encontrado → 404, conflito → 409, 401/403 → `ProblemDetails`; `Result` sem valor → 204. Na época, listas vazias passaram de `200 []` para `204`; **a ADR 9 reverte isso** (coleção vazia → `200 []`) — pendência #86, que será outra breaking change coordenada com o `bills-frontend`. **Breaking change para o frontend** (já tratada): 400s que eram texto puro viraram `ValidationProblem` (JSON com `errors`) e pagar/receber um lançamento já congelado passou a responder 409.
- **Erros em ProblemDetails (RFC 9457)** (ADR 3). Toda resposta de erro é `application/problem+json`, não só as que passam por `ToHttpResult`: `AddProblemDetails` (com `instance` = caminho e `traceId` para correlação com os logs), `UseStatusCodePages` para os 4xx sem corpo gerados pelo framework (401 do JwtBearer, 404/405 de rota, 415, 400 de binding) e `UseExceptionHandler` para exceções. O código estável do erro vai na **extensão `code`**, não em `title`: pela RFC, `title` descreve o *tipo* do problema e não deve variar por ocorrência, e clientes não devem depender dele; `detail` fica com a mensagem. O exception handler roda **só fora de Development**, para manter a página de exceção do desenvolvedor localmente; em produção (e no ambiente `Testing` dos testes de integração) o 500 é genérico, sem mensagem nem stack trace. O 400 de token ausente em `GET /bills/shared` passou a ser `ValidationProblem` (`errors.token`), validado no serviço como nas demais features. **Breaking change para o frontend**: quem lia o código do erro em `title` deve passar a ler `code`; respostas antes sem corpo (401, 404 de rota, 400 de JSON) agora têm corpo JSON.
- **Testes: NUnit 4 + NSubstitute no Microsoft.Testing.Platform, com CPM** (ADR 8). MTP (`global.json`) dá um runner mais leve e coverage via `Microsoft.Testing.Extensions.CodeCoverage` (`--coverage` no CI, sem VSTest/`Microsoft.NET.Test.Sdk`); versões de pacote centralizadas em `Directory.Packages.props` e configuração comum em `tests/Directory.Build.props`. **Um projeto de teste unitário por camada** (`Domain`, `Application`, `Api`, `Data` em `tests/UnitTests/`), espelhando `src/` e permitindo rodar só a camada alterada. **Um único projeto de integração** (`tests/IntegrationTests/Api.IntegrationTests`) contra Postgres real: o Respawn reseta o banco **uma vez por fixture** (isolamento por `firebase_uid` distinto em cada teste) e o assembly é `NonParallelizable`, porque fixtures em paralelo no mesmo banco teriam seus dados apagados pelo Respawn umas das outras.

## Pendências (código × ADR)

Divergências conhecidas entre o código atual e as ADRs. Código novo segue a ADR; o legado é corrigido pela issue indicada. Ao fechar uma issue, remova a linha daqui e as notas ⚠️ correspondentes em `CLAUDE.md` e `docs/`.

| Issue | ADR | Divergência atual |
|---|---|---|
| #86 | 9 | `ResultExtensions.ToHttpResult` devolve 204 sem corpo para coleção vazia (deveria ser `200 []`). Breaking change para o frontend. |
| #87 | 6 | `Program.cs` aplica migrations no startup em Development via `IMigrationService`. |
| #83 | 6 | CD roda `dotnet ef database update` do código-fonte (sem artefato de migration único). |
| #88 | 1 | Não há testes de arquitetura no CI; `SharedBillsEndpoint` define owner e deriva ano/mês no endpoint. |
| #89 | 8 | Bogus não instalado; dados de teste montados à mão. |
| #90 | 3 | `type` é o default do framework (não estável por tipo de problema da aplicação); `instance` é o caminho da rota (não a ocorrência). |
| #91 | 7 | Mês/ano e datas de pagamento derivados em UTC (sem fuso de negócio); borda de expiração inconsistente (`<=` em `PersonAccessLinksService`, `<` em `SharedPagesTokenService`). |
| #82 | 12, 13, 14, 18, 19 | Sem tags de versão, versão injetada, pré-releases, versão exposta em runtime nem check de changelog no PR. |
| #85 | 17, 10 | `cd-prod.yml` publica a cada push em `main` com build próprio; sem gate de readiness antes de receber tráfego (smoke tests só depois do deploy). |
