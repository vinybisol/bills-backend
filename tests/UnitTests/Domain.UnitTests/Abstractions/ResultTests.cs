using Domain.Abstractions;

namespace Domain.UnitTests.Abstractions;

[TestFixture]
public sealed class ResultTests
{
    [Test]
    public void Success_NoValue_IsSuccessWithNoneError()
    {
        // Act
        var result = Result.Success();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.IsFailure, Is.False);
            Assert.That(result.Error, Is.EqualTo(Error.None));
        }
    }

    [Test]
    public void Failure_WithError_IsFailureCarryingError()
    {
        // Arrange
        var error = Error.NotFound("Pessoa");

        // Act
        var result = Result.Failure(error);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(error));
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
        }
    }

    [Test]
    public void Failure_WithNoneError_ThrowsInvalidOperationException() =>
        Assert.That(() => Result.Failure(Error.None), Throws.InvalidOperationException);

    [Test]
    public void Create_NullValue_ReturnsNullValueFailure()
    {
        // Act
        var result = Result.Create<string>(null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(Error.NullValue));
        }
    }

    [Test]
    public void Value_OnFailure_ThrowsInvalidOperationException()
    {
        // Arrange
        Result<string> result = Error.Conflict("Pessoa");

        // Act / Assert
        Assert.That(() => result.Value, Throws.InvalidOperationException);
    }

    [Test]
    public void ImplicitConversion_FromValue_ReturnsSuccessWithValue()
    {
        // Act
        Result<string> result = "ok";

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo("ok"));
        }
    }

    [TestCase(ErrorType.NotFound)]
    [TestCase(ErrorType.Conflict)]
    [TestCase(ErrorType.Validation)]
    [TestCase(ErrorType.Unauthorized)]
    [TestCase(ErrorType.Forbidden)]
    public void ErrorFactories_Always_MapToExpectedErrorType(ErrorType expected)
    {
        // Act
        var error = expected switch
        {
            ErrorType.NotFound => Error.NotFound("X"),
            ErrorType.Conflict => Error.Conflict("X"),
            ErrorType.Validation => Error.Validation("X"),
            ErrorType.Unauthorized => Error.Unauthorized(),
            _ => Error.Forbidden(),
        };

        // Assert
        Assert.That(error.Type, Is.EqualTo(expected));
    }

    [Test]
    public void ValidationErrorFromResults_MixedResults_KeepsOnlyFailures()
    {
        // Arrange
        var nameError = new Error("Name", "Nome obrigatório.", ErrorType.Validation);
        var amountError = new Error("Amount", "Valor inválido.", ErrorType.Validation);
        Result[] results = [Result.Success(), Result.Failure(nameError), Result.Failure(amountError)];

        // Act
        var validationError = ValidationError.FromResults(results);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(validationError.Type, Is.EqualTo(ErrorType.Validation));
            Assert.That(validationError.Errors, Is.EqualTo(new[] { nameError, amountError }));
        }
    }
}
