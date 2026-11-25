---
name: pre-commit-check
description: Revisa as mudanças pendentes antes de um commit/PR/release e verifica se seguem as ADRs de changelog, SemVer, tags, Git Flow, merge, release, pré-release, versão em runtime e pinning (ADRs 12–20), além das regras de código, testes e segredos do repo. Use antes de commitar, abrir PR, fazer release ou hotfix, ou quando pedirem "confere se está tudo certo para o commit".
---

# Checagem pré-commit

Objetivo: antes de commitar (ou abrir PR / fazer release), analisar **tudo** que está mudando e dizer, item a item, se está conforme as ADRs e o `CLAUDE.md`. **Não commite, não faça push nem crie tag** sem o usuário pedir — esta skill analisa, corrige o que for seguro e reporta.

Fonte das regras: ADRs (MCP `adrs`, `get_adr <n>`) e `CLAUDE.md`. Divergências já conhecidas entre código e ADR estão em `docs/decisoes.md` › "Pendências": **não as aponte como erro do commit**, mas **reprove** se o commit criar uma divergência nova ou replicar uma existente em código novo.

## 1. Levantar o contexto

```
git --no-pager status -sb
git --no-pager branch --show-current
git --no-pager fetch origin --tags --quiet
git --no-pager diff --stat            # não staged
git --no-pager diff --cached --stat   # staged
git --no-pager log --oneline origin/develop..HEAD
git --no-pager describe --tags --abbrev=0 origin/main   # última release (pode não existir ainda)
```

Leia o diff completo (`git --no-pager diff` e `git --no-pager diff --cached`). Arquivos não rastreados (`??`) também entram na análise. Classifique o trabalho:

| Branch atual | Tipo | Base | Destino do PR |
|---|---|---|---|
| `feature/<nome>` | feature | `develop` | `develop` (squash) |
| `hotfix/<nome>` | hotfix | `main` | `main` (merge commit) + back-merge em `develop` |
| `develop` / `main` | release ou back-merge | — | ver seção 9 |
| qualquer outro | ⚠️ fora do padrão | — | — |

## 2. Branch e histórico (ADRs 15 e 16)

- [ ] Commits de feature/hotfix **não** vão direto em `develop` ou `main` (exceto o commit de release, seção 9).
- [ ] Nome em kebab-case: `feature/<nome-descritivo>` ou `hotfix/<nome>`.
- [ ] A branch parte da base correta: `git merge-base --is-ancestor origin/develop HEAD` (feature) ou `origin/main` (hotfix). Base desatualizada → sugerir merge da base na branch (sem rebase de branch publicada sem pedido).
- [ ] Nenhum cherry-pick entre `develop` e `main`; nenhum squash/rebase entre `develop` e `main`.
- [ ] O commit contém **uma** mudança coerente; o que não está pronto não entra em `develop` (ou entra atrás de feature flag).
- [ ] Sugira o **título do PR** no imperativo, descritivo (vira a mensagem do squash), com `Closes #<n>` quando houver issue. Commits internos podem ser informais.

## 3. Changelog (ADRs 12 e 13)

Decida se alguma mudança é **observável pelo consumidor** da API:

| Mudança | Entra? | Categoria |
|---|---|---|
| Endpoint/capacidade nova, campo novo na resposta | Sim | `Added` |
| Comportamento existente diferente, mudança perceptível de performance, novo runtime mínimo | Sim | `Changed` |
| Algo será removido no futuro | Sim | `Deprecated` |
| Endpoint/campo removido | Sim | `Removed` |
| Bug perceptível (inclusive corrigido via refactor) | Sim | `Fixed` |
| Dependência atualizada para corrigir vulnerabilidade | Sim | `Security` |
| Testes, refactor sem mudança de comportamento, pipeline/build, atualização rotineira de dependência, docs internas, o próprio changelog | Não | — |

Se houver mudança observável:

- [ ] `CHANGELOG.md` alterado no **mesmo** commit/PR, **só** dentro de `## [Unreleased]`.
- [ ] Categoria correta (`### Added|Changed|Deprecated|Removed|Fixed|Security`), só categorias com conteúdo.
- [ ] Texto descreve o **efeito para o consumidor**, em vocabulário de produto — nunca nome de classe/serviço/DTO, nem número de versão. Ex.: "Exportar contas em CSV.", não "Adiciona ExportService".
- [ ] `Changed` que **quebra contrato** (campo renomeado, status HTTP diferente, formato mudou) deixa isso explícito na entrada — define MAJOR (MINOR enquanto `0.y.z`). Citar quando exige mudança coordenada no `bills-frontend`.
- [ ] Seções já liberadas (`## [X.Y.Z] - ...`) **não** foram editadas.
- [ ] `## [Unreleased]` continua sendo a primeira seção, mesmo vazia.

Sem mudança observável: `CHANGELOG.md` **não** muda. Diga explicitamente "sem impacto para o consumidor — changelog não muda".

## 4. Versão (ADRs 14, 18 e 19)

- [ ] Nenhum `<Version>`, `<VersionPrefix>`, `<VersionSuffix>`, `<AssemblyVersion>`, `<FileVersion>` ou `<InformationalVersion>` adicionado em `*.csproj`, `Directory.Build.props` ou similares (`<PackageVersion Include=...>` de dependência no `Directory.Packages.props` é permitido).
- [ ] Nenhuma versão da aplicação hardcoded em código, `appsettings*.json`, arquivo de texto ou variável de ambiente de deploy. Em runtime a versão vem **só** de `AssemblyInformationalVersionAttribute`; sem versão injetada → placeholder (`unknown`/`0.0.0-local`), nunca falha.
- [ ] Mudança de pipeline que mexe em versão: release = nome da tag sem `v`; pré-release em `develop` = `<última tag alcançável>-dev.<run>+<sha curto>` (sem tag: `0.0.0-dev...`); checkout com histórico completo e tags (`fetch-depth: 0`); artefato de pré-release com retenção de 7 dias, nunca promovido a produção.

## 5. Pinning de dependências (ADR 20)

Vale para **toda linha adicionada ou alterada** (divergência antiga não é erro do commit, mas não pode ser copiada para código novo):

- [ ] NuGet: versão exata no `Directory.Packages.props` (CPM) — sem `*`, ranges `[`/`(`. Sem `Version=` em `PackageReference` de `.csproj`.
- [ ] Se existirem `packages.lock.json`, foram atualizados junto e o restore do CI usa `--locked-mode`.
- [ ] .NET SDK: versão exata em `global.json` com `rollForward: disable`; CI usa `global-json-file`, não `10.0.x`.
- [ ] GitHub Actions: `uses: owner/action@<SHA completo de 40 chars> # vX.Y.Z`; runner versionado (`ubuntu-24.04`), nunca `*-latest`.
- [ ] Imagens Docker (`Dockerfile`, `docker-compose.yml`, `services:`/`container:` de workflow): `imagem:tag@sha256:<digest>`, nunca `latest`.
- [ ] Atualização de dependência que afeta o consumidor (ex.: correção de segurança) tem entrada no changelog.

Divergência antiga de ADR 20 que ainda não está em `docs/decisoes.md` › "Pendências": **avise** (sugerir issue), sem corrigir fora do escopo do commit.

## 6. Código e arquitetura (ADRs 1–10, `CLAUDE.md`)

Confira só o que o diff toca:

- [ ] Camadas: `Domain` não depende de ninguém; `Application` sem EF Core; só `Api/Program.cs` referencia `Data`. Endpoint fino: sem claims do Firebase, sem `AppDbContext`, com `UserEndpointFilter` (exceto endpoint intencionalmente anônimo).
- [ ] Falhas esperadas via `Result`/`Error` com código estável (ADR 2); erros como `ProblemDetails` (ADR 3); coleção → `200` com array, inclusive vazio, em endpoint **novo** (ADR 9).
- [ ] Repositórios não comitam; um `SaveChangesAsync` por caso de uso (ADR 5).
- [ ] Sem `DateTime.Now/UtcNow`/`DateTimeOffset.Now`; `TimeProvider` injetado, um instante por operação (ADR 7).
- [ ] Configuração nova = `IOptions<T>` + validação + `ValidateOnStart()` (ADR 4).
- [ ] Dependência externa nova classificada como crítica/opcional no readiness (ADR 10).
- [ ] Nenhuma migration já aplicada alterada; migration nova compatível com a versão anterior no ar (expand/contract) (ADR 6).
- [ ] Invariantes de domínio do `CLAUDE.md` preservadas (`owner_id` interno, snapshots, lançamento pago congelado → 409, soft delete de moldes, `split_ratio`).

## 7. Testes (ADR 8)

- [ ] Comportamento novo/alterado tem teste unitário **e** de integração do endpoint (inclusive autenticação).
- [ ] Integração: uid distinto por teste, sem `[SetUp]` que reseta o banco, sem `[Parallelizable]`.
- [ ] Dados decisivos explícitos; tempo via `FixedTimeProvider`; entidades via factory.
- [ ] Rode o subconjunto relevante (sempre com `--project`), ex.:
  - `dotnet test --project tests/UnitTests/Application.UnitTests --filter FullyQualifiedName~<Feature>`
  - `dotnet test --project tests/IntegrationTests/Api.IntegrationTests --filter FullyQualifiedName~<Feature>EndpointTests`
- [ ] Antes de abrir o PR: `dotnet test` na raiz, **verde**. Sem Postgres local disponível, diga isso no relatório em vez de aprovar.

## 8. Segredos, arquivos e documentação

- [ ] Nada de segredo, connection string real, token ou `appsettings.Testing.Local.json` no diff/stage. Sem `bin/`, `obj/`, `TestResults/` ou relatórios de cobertura.
- [ ] Documentação afetada atualizada: `docs/api.md` (contrato), `docs/schema.md`/`schema.sql` (schema), `docs/dominio.md` (regra), `docs/arquitetura.md`, `docs/testes.md`.
- [ ] Se o commit resolve uma pendência de `docs/decisoes.md`, a linha da pendência e o status na tabela de ADRs foram atualizados.

## 9. Fluxos especiais

### Release (`develop` → `main`) — ADRs 12, 13, 14, 16, 17

1. `[Unreleased]` vazio = nada a liberar; pare.
2. Revisar textos, juntar duplicadas, confirmar categorias e se algum `Changed` quebra contrato.
3. Escolher a versão: `Removed`/`Changed` breaking → MAJOR (MINOR enquanto `0.y.z`); `Added`/`Deprecated` → MINOR; só `Fixed`/`Security` → PATCH. O maior incremento vence.
4. Commit de release (em `develop`): renomear para `## [X.Y.Z] - AAAA-MM-DD` (data da release), novo `## [Unreleased]` vazio acima, atualizar links no fim do arquivo:
   ```
   [Unreleased]: https://github.com/vinybisol/bills-backend/compare/vX.Y.Z...HEAD
   [X.Y.Z]: https://github.com/vinybisol/bills-backend/compare/vANTERIOR...vX.Y.Z
   ```
   Primeira versão: `[X.Y.Z]: https://github.com/vinybisol/bills-backend/releases/tag/vX.Y.Z`.
5. Confirmar que a tag ainda **não existe** (`git tag -l vX.Y.Z` e `git ls-remote --tags origin vX.Y.Z`): tag publicada é imutável.
6. Mudanças na pipeline de release precisam estar em `main` antes da tag.
7. PR `develop` → `main` com **merge commit**. Depois do merge, no `main` atualizado: `git tag -a vX.Y.Z -m "vX.Y.Z"` e `git push origin vX.Y.Z` (push de branch não envia tag).
8. Back-merge `main` → `develop` com merge commit; conferir `git merge-base --is-ancestor vX.Y.Z origin/develop`.

### Hotfix (`hotfix/*` → `main`)

- Só a correção; versão PATCH; seção própria `## [X.Y.Z] - AAAA-MM-DD` logo abaixo de `[Unreleased]` (ADR 12 › correções urgentes).
- Merge commit em `main`, tag anotada, back-merge em `develop` mantendo as entradas pendentes de `[Unreleased]` ao resolver conflito no changelog.

### Back-merge (`main` → `develop`)

- Merge commit (nunca squash/rebase). Conferir que a última tag ficou alcançável a partir de `develop`.

## 10. Relatório

Termine com uma tabela curta e um veredito:

| Área | Status | Observação |
|---|---|---|
| Branch / Git Flow | ✅ / ❌ / ➖ | ... |
| Changelog | ... | categoria + texto sugerido, ou "sem impacto" |
| Versão | ... | ... |
| Pinning (ADR 20) | ... | ... |
| Código / arquitetura | ... | ... |
| Testes | ... | comandos rodados e resultado |
| Segredos / arquivos | ... | ... |
| Docs | ... | ... |

**Veredito:** `PRONTO PARA COMMIT` ou `BLOQUEADO` (listar o que corrigir). Inclua a sugestão de título do PR e, se aplicável, a entrada de changelog proposta. Correções seguras e dentro do escopo (ex.: adicionar a entrada de changelog) podem ser aplicadas após mostrar ao usuário; nunca commite sem pedido explícito.
