# Testes

NUnit 4 + NSubstitute + `Assert.That` (modelo de constraints), rodando no **Microsoft.Testing.Platform (MTP)**. As regras obrigatórias (o que testar, quando rodar a suíte completa, isolamento) estão em `CLAUDE.md` › Testes. Aqui fica a estrutura e o "como".

## Configuração

| Arquivo | Papel |
|---|---|
| `global.json` | `"test": { "runner": "Microsoft.Testing.Platform" }`: `dotnet test` usa MTP |
| `Directory.Packages.props` | versões de todos os pacotes (CPM); `.csproj` não tem `Version=` |
| `tests/Directory.Build.props` | comum a todos os testes: `net10.0`, `OutputType=Exe`, `EnableNUnitRunner=true`, NUnit, NUnit.Analyzers, NUnit3TestAdapter (runner MTP), `Microsoft.Testing.Extensions.CodeCoverage`, `using NUnit.Framework` |
| `tests/UnitTests/Directory.Build.props` | importa o anterior e adiciona NSubstitute |

Não há `Microsoft.NET.Test.Sdk`/VSTest, xUnit, Moq nem AutoFixture.

## Estrutura

Cada projeto espelha as pastas do projeto de `src/` que testa. Namespace `<Camada>.UnitTests.<Pasta>`, classe `XTests`, método `Metodo_Cenario_ResultadoEsperado`, padrão AAA.

```
tests/
├─ Directory.Build.props
├─ UnitTests/
│  ├─ Directory.Build.props
│  ├─ Domain.UnitTests/           → src/Domain
│  │  ├─ Abstractions/ResultTests.cs
│  │  ├─ Calculations/            # EntryCalculations, EntryAggregations, BalanceCalculations, ...
│  │  ├─ Entities/                # uma fixture por entidade
│  │  └─ TestSupport/             # builders de entries, InvalidStrings
│  ├─ Application.UnitTests/      → src/Application (InternalsVisibleTo)
│  │  ├─ Services/                # uma fixture por serviço, repositórios via NSubstitute
│  │  └─ TestSupport/             # FixedTimeProvider, EntityId (seta Id privado), InvalidStrings
│  ├─ Api.UnitTests/              → src/Api
│  │  ├─ Extensions/ResultExtensionsTests.cs
│  │  ├─ Filters/UserEndpointFilterTests.cs
│  │  └─ Identity/FirebaseClaimsTests.cs
│  └─ Data.UnitTests/             → src/Data
│     └─ Contexts/NeonConnectionStringTests.cs
└─ IntegrationTests/
   └─ Api.IntegrationTests/
      ├─ AssemblyInfo.cs          # [assembly: NonParallelizable]
      ├─ Infrastructure/
      │  ├─ CustomWebApplicationFactory.cs  # host da Api com banco de teste e chave JWT local
      │  ├─ TestTokens.cs                   # gera JWTs "do Firebase" assinados localmente
      │  └─ IntegrationTestBase.cs          # Respawn 1x por fixture, NewFirebaseUid, CreateAuthenticatedClient
      ├─ Endpoints/               # <Feature>EndpointTests.cs — uma fixture por feature
      └─ TestSupport/
         ├─ InvalidStrings.cs
         └─ ProblemAssertions.cs  # AssertProblemAsync, AssertValidationProblemAsync
```

## O que cada nível cobre

- **Domain**: invariantes das entidades (factories, congelamento, soft delete, split × person) e cálculos puros com casos de borda (zero, `actual` nulo, split 0/0.5/1, limites de mês/ano).
- **Application**: cada ramo do serviço (validação, não encontrado, conflito, sucesso), se `SaveChangesAsync` foi chamado ou não, e datas via `FixedTimeProvider`.
- **Api**: mapeamento `Result` → HTTP, provisionamento e owner no filtro, leitura de claims.
- **Integração**: ponta a ponta com JWT, filtro, EF e Postgres real. Cobre 401, 400 (`ValidationProblem` por campo), 404, 409, isolamento entre owners e o contrato de resposta.

## Exemplo — teste unitário de serviço

```csharp
[TestFixture]
public sealed class PersonServiceTests
{
    private IPersonRepository _repository = null!;
    private IUnitOfWork _unitOfWork = null!;
    private PersonService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IPersonRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        var owner = Substitute.For<ICurrentOwner>();
        owner.Id.Returns(1L);
        _sut = new PersonService(_repository, owner, new FixedTimeProvider(DateTimeOffset.UtcNow), _unitOfWork);
    }

    [Test]
    public async Task CreateAsync_NameAlreadyExists_ReturnsConflict()
    {
        _repository.ExistsByNameAsync("Ana", Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.CreateAsync("Ana", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Conflict));
        }
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
```

## Exemplo — teste de integração

```csharp
[TestFixture]
public sealed class IncomeEndpointTests : IntegrationTestBase
{
    [Test]
    public async Task Post_EmptyName_ReturnsValidationProblem()
    {
        using var client = CreateAuthenticatedClient();   // uid único → owner único

        using var response = await client.PostAsJsonAsync("/api/v1/incomes",
            new { name = "", kind = "recurring", defaultAmount = 1000m });

        await ProblemAssertions.AssertValidationProblemAsync(response, "name");
    }
}
```

Nunca adicione `[SetUp]` que reseta o banco nem `[Parallelizable]` no projeto de integração (ver `CLAUDE.md` › Isolamento).

## Banco para integração

Precisa de um PostgreSQL com o banco `bills_test`. A connection string vem de `ConnectionStrings:NeonTest` (user-secrets do projeto `tests/IntegrationTests/Api.IntegrationTests` ou a variável `ConnectionStrings__NeonTest`). Passo a passo em `CLAUDE.md` › Setup local de testes. No CI, um service container `postgres:16`.

## Comandos

```bash
dotnet test --project tests/UnitTests/Application.UnitTests                  # uma camada
dotnet test --project tests/IntegrationTests/Api.IntegrationTests \
  --filter FullyQualifiedName~IncomeEndpointTests                            # uma fixture
dotnet test                                                                  # tudo (antes do PR)
dotnet test --project <proj> -- --coverage --coverage-output-format cobertura  # cobertura
```

Com MTP, use sempre `--project` ao filtrar. `dotnet test --filter` sem `--project` roda na solução inteira e falha nos projetos em que nenhum teste corresponde ao filtro ("Zero tests ran", exit code 8).
