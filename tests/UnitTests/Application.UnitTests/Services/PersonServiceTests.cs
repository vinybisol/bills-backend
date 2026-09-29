using Application.Abstractions.Repositories;
using Application.Abstractions.Repositories.Strategies;
using Application.DTOs.Services;
using Application.Services;
using Application.UnitTests.TestSupport;
using Domain.Abstractions;
using Domain.Abstractions.Filters;
using Domain.Entities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.UnitTests.Services;

[TestFixture]
public sealed class PersonServiceTests
{
    private const long OwnerId = 7L;
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 29, 18, 0, 0, TimeSpan.Zero);

    private IPersonRepository _repository = null!;
    private ICurrentOwner _currentOwner = null!;
    private IUnitOfWork _unitOfWork = null!;
    private PersonService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IPersonRepository>();
        _currentOwner = Substitute.For<ICurrentOwner>();
        _currentOwner.Id.Returns(OwnerId);
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _sut = new PersonService(_repository, _currentOwner, new FixedTimeProvider(FixedNow), _unitOfWork);
    }

    private static Person ExistingPerson(long id = 10L, string name = "Ana") =>
        EntityId.With(Person.Create(OwnerId, name, FixedNow), id);

    // --- CreateAsync ---

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public async Task CreateAsync_BlankName_ReturnsValidationErrorWithoutPersisting(string? name)
    {
        // Act
        var result = await _sut.CreateAsync(name!, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
        }
        await _repository.DidNotReceiveWithAnyArgs().ExistsByNameAsync(default!, default);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Test]
    public async Task CreateAsync_NameAlreadyExists_ReturnsConflictWithoutPersisting()
    {
        // Arrange
        _repository.ExistsByNameAsync("Ana", Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await _sut.CreateAsync("Ana", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Conflict));
        }
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task CreateAsync_ValidName_AddsPersonForCurrentOwnerAndSaves()
    {
        // Arrange
        Person? added = null;
        _repository.Add(Arg.Do<Person>(p => added = p));

        // Act
        var result = await _sut.CreateAsync("Ana", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Name, Is.EqualTo("Ana"));
            Assert.That(added, Is.Not.Null);
            Assert.That(added!.OwnerId, Is.EqualTo(OwnerId));
            Assert.That(added.CreatedAt, Is.EqualTo(FixedNow));
            Assert.That(added.Active, Is.True);
        }
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateAsync_NameWithSurroundingWhitespace_TrimsBeforeCheckingAndSaving()
    {
        // Act
        var result = await _sut.CreateAsync("  Ana  ", CancellationToken.None);

        // Assert
        Assert.That(result.Value.Name, Is.EqualTo("Ana"));
        await _repository.Received(1).ExistsByNameAsync("Ana", Arg.Any<CancellationToken>());
        _repository.Received(1).Add(Arg.Is<Person>(p => p.Name == "Ana"));
    }

    [Test]
    public async Task CreateAsync_CancellationToken_IsForwardedToRepositoryAndUnitOfWork()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        // Act
        await _sut.CreateAsync("Ana", cts.Token);

        // Assert
        await _repository.Received(1).ExistsByNameAsync("Ana", cts.Token);
        await _unitOfWork.Received(1).SaveChangesAsync(cts.Token);
    }

    [Test]
    public async Task CreateAsync_RepositoryCancelled_PropagatesOperationCanceledException()
    {
        // Arrange
        _repository.ExistsByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        // Act / Assert
        await Assert.ThatAsync(
            () => _sut.CreateAsync("Ana", CancellationToken.None),
            Throws.InstanceOf<OperationCanceledException>());
    }

    // --- GetAllByNameAsync ---

    [Test]
    public async Task GetAllByNameAsync_RepositoryReturnsItems_ReturnsThem()
    {
        // Arrange
        IEnumerable<PersonDto> people = [new PersonDto(1L, "Ana"), new PersonDto(2L, "Bruno")];
        _repository.GetAllByNameAsync(Arg.Any<IPagedQuery<Person>>(), Arg.Any<CancellationToken>()).Returns(people);

        // Act
        var result = await _sut.GetAllByNameAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(people));
        }
    }

    [Test]
    public async Task GetAllByNameAsync_Always_QueriesFirstPageOfOneThousandOrderedByName()
    {
        // Arrange
        IPagedQuery<Person>? query = null;
        _repository.GetAllByNameAsync(Arg.Do<IPagedQuery<Person>>(q => query = q), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Empty<PersonDto>());

        // Act
        await _sut.GetAllByNameAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query, Is.Not.Null);
            Assert.That(query!.Take, Is.EqualTo(1000));
            Assert.That(query.Skip, Is.Zero);
            Assert.That(query.OrderBy.Compile()(Person.Create(OwnerId, "Zé", FixedNow)), Is.EqualTo("Zé"));
        }
    }

    [Test]
    public async Task GetAllByNameAsync_RepositoryReturnsEmpty_ReturnsEmptySuccess()
    {
        // Arrange
        _repository.GetAllByNameAsync(Arg.Any<IPagedQuery<Person>>(), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Empty<PersonDto>());

        // Act
        var result = await _sut.GetAllByNameAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.Empty);
        }
    }

    [Test]
    public async Task GetAllByNameAsync_RepositoryReturnsNull_ReturnsEmptySuccess()
    {
        // Arrange
        _repository.GetAllByNameAsync(Arg.Any<IPagedQuery<Person>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<PersonDto>>(null!));

        // Act
        var result = await _sut.GetAllByNameAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.Empty);
        }
    }

    // --- UpdateAsync ---

    [TestCaseSource(typeof(InvalidStrings), nameof(InvalidStrings.Cases))]
    public async Task UpdateAsync_BlankName_ReturnsValidationErrorWithoutLookup(string? name)
    {
        // Act
        var result = await _sut.UpdateAsync(10L, name!, CancellationToken.None);

        // Assert
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
        await _repository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Test]
    public async Task UpdateAsync_PersonNotFound_ReturnsNotFound()
    {
        // Arrange
        _repository.GetByIdAsync(10L, Arg.Any<CancellationToken>()).Returns((Person?)null);

        // Act
        var result = await _sut.UpdateAsync(10L, "Maria", CancellationToken.None);

        // Assert
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task UpdateAsync_NameAlreadyExists_ReturnsConflictAndKeepsName()
    {
        // Arrange
        var person = ExistingPerson(name: "Ana");
        _repository.GetByIdAsync(10L, Arg.Any<CancellationToken>()).Returns(person);
        _repository.ExistsByNameAsync("Maria", Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await _sut.UpdateAsync(10L, "Maria", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Conflict));
            Assert.That(person.Name, Is.EqualTo("Ana"));
        }
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task UpdateAsync_ValidData_RenamesTrimmedAndSaves()
    {
        // Arrange
        var person = ExistingPerson(id: 10L, name: "Ana");
        _repository.GetByIdAsync(10L, Arg.Any<CancellationToken>()).Returns(person);

        // Act
        var result = await _sut.UpdateAsync(10L, "  Maria  ", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(new PersonDto(10L, "Maria")));
            Assert.That(person.Name, Is.EqualTo("Maria"));
        }
        await _repository.Received(1).ExistsByNameAsync("Maria", Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- DeleteByIdAsync ---

    [Test]
    public async Task DeleteByIdAsync_PersonNotFound_ReturnsNotFound()
    {
        // Arrange
        _repository.GetByIdAsync(10L, Arg.Any<CancellationToken>()).Returns((Person?)null);

        // Act
        var result = await _sut.DeleteByIdAsync(10L, CancellationToken.None);

        // Assert
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Test]
    public async Task DeleteByIdAsync_ExistingPerson_SoftDeletesAndSaves()
    {
        // Arrange
        var person = ExistingPerson();
        _repository.GetByIdAsync(10L, Arg.Any<CancellationToken>()).Returns(person);

        // Act
        var result = await _sut.DeleteByIdAsync(10L, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(person.Active, Is.False);
        }
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
