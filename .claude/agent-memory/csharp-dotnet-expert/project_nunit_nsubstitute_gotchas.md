---
name: project-nunit-nsubstitute-gotchas
description: two gotchas hit writing NSubstitute-based tests in bills-backend's NUnit suite - fixture instance reuse, and EF-generated ids not existing on mocked entities
metadata:
  type: project
---

Two non-obvious issues surfaced 2026-07-05 while converting `UserProvisioningServiceTests` from a
real (InMemory) `AppDbContext` to NSubstitute mocks of `IAppUserService`/`ICategoryRepository`
(see [[project-stack-and-testing]] for the layout this applies to):

1. **NUnit reuses a single `[TestFixture]` instance across all `[Test]` methods by default**
   (unlike xUnit, which news up a fresh instance per test). A `private readonly IFoo _foo =
   Substitute.For<IFoo>();` field initializer therefore creates ONE substitute shared by every
   test in the class — call-history assertions (`Received(1)`) then see leaked calls from
   unrelated tests and fail with confusing "N non-matching calls" errors. Fix: create substitutes
   in a `[SetUp]` method (non-readonly fields assigned there), not via field initializers.

2. **Entities with EF `ValueGeneratedOnAdd` id properties (private setter, no public way to set
   them) stay at their default value (`0`) when a repository is mocked**, because nothing ever
   calls the real `SaveChangesAsync` that EF Core uses to backfill the id via reflection. If
   downstream domain logic requires a positive id (e.g. `Category.Create` throws
   `ArgumentOutOfRangeException` for `ownerId <= 0`), the test must simulate that assignment
   itself: `typeof(AppUser).GetProperty(nameof(AppUser.Id))!.SetValue(user, someId);` inside the
   mocked repository's `Returns(callInfo => ...)` callback. `PropertyInfo.SetValue` can invoke a
   private setter directly — no extra `BindingFlags` needed since `GetProperty` still finds the
   property (its getter is public even though the setter is private).

**How to apply**: when writing new NSubstitute-based unit tests in this repo, default to `[SetUp]`
for creating substitutes, and check whether any entity the mock hands back has an EF-generated id
that downstream code depends on being positive/non-default before wiring up the mock's return
value.
