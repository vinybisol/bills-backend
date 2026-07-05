# Memory Index

- [Stack & testing layout](project_stack_and_testing.md) — .NET 10, Domain/Application/Data/Api layered projects, dependency-direction rule, NUnit unit vs integration tests
- [Docker sandbox limitation](project_docker_sandbox_limitation.md) — Postgres reachability varies by session; always verify with `dotnet test`, don't assume from memory
- [NUnit/NSubstitute gotchas](project_nunit_nsubstitute_gotchas.md) — fixture instance reuse across tests, and EF-generated ids missing on mocked entities
