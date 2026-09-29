using Api.Extensions;
using Domain.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Api.UnitTests.Extensions;

[TestFixture]
public sealed class ResultExtensionsTests
{
    private sealed record Item(int Id);

    private static IEnumerable<TestCaseData> ErrorStatusCases()
    {
        yield return new TestCaseData(Error.NotFound("Pessoa"), StatusCodes.Status404NotFound).SetArgDisplayNames("NotFound");
        yield return new TestCaseData(Error.Validation("Nome inválido."), StatusCodes.Status400BadRequest).SetArgDisplayNames("Validation");
        yield return new TestCaseData(Error.Conflict("Pessoa"), StatusCodes.Status409Conflict).SetArgDisplayNames("Conflict");
        yield return new TestCaseData(Error.Unauthorized(), StatusCodes.Status401Unauthorized).SetArgDisplayNames("Unauthorized");
        yield return new TestCaseData(Error.Forbidden(), StatusCodes.Status403Forbidden).SetArgDisplayNames("Forbidden");
        yield return new TestCaseData(Error.InvalidOperation, StatusCodes.Status500InternalServerError).SetArgDisplayNames("Failure");
    }

    private static void AssertProblem(IResult httpResult, Error error, int expectedStatus)
    {
        Assert.That(httpResult, Is.InstanceOf<ProblemHttpResult>());
        var problem = (ProblemHttpResult)httpResult;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(problem.StatusCode, Is.EqualTo(expectedStatus));
            Assert.That(problem.ProblemDetails.Status, Is.EqualTo(expectedStatus));
            Assert.That(problem.ProblemDetails.Title, Is.EqualTo(error.Code));
            Assert.That(problem.ProblemDetails.Detail, Is.EqualTo(error.Message));
        }
    }

    // --- Result (no value) ---

    [Test]
    public void ToHttpResult_Success_ReturnsNoContent() =>
        Assert.That(Result.Success().ToHttpResult(), Is.InstanceOf<NoContent>());

    [TestCaseSource(nameof(ErrorStatusCases))]
    public void ToHttpResult_Failure_MapsErrorTypeToProblemStatus(Error error, int expectedStatus) =>
        AssertProblem(Result.Failure(error).ToHttpResult(), error, expectedStatus);

    // --- Result<T> ---

    [Test]
    public void ToHttpResultOfT_Success_ReturnsOkWithValue()
    {
        // Arrange
        var item = new Item(1);

        // Act
        var httpResult = Result.Success(item).ToHttpResult();

        // Assert
        Assert.That(httpResult, Is.InstanceOf<Ok<Item>>());
        Assert.That(((Ok<Item>)httpResult).Value, Is.SameAs(item));
    }

    [TestCaseSource(nameof(ErrorStatusCases))]
    public void ToHttpResultOfT_Failure_MapsErrorTypeToProblemStatus(Error error, int expectedStatus) =>
        AssertProblem(Result.Failure<Item>(error).ToHttpResult(), error, expectedStatus);

    // --- ValidationError ---

    [Test]
    public void ToHttpResult_ValidationError_ReturnsValidationProblemGroupedByCode()
    {
        // Arrange
        var validation = new ValidationError(
        [
            new Error("Name", "Nome obrigatório.", ErrorType.Validation),
            new Error("Amount", "Valor deve ser positivo.", ErrorType.Validation),
        ]);

        // Act
        var httpResult = Result.Failure(validation).ToHttpResult();

        // Assert
        Assert.That(httpResult, Is.InstanceOf<IStatusCodeHttpResult>());
        Assert.That(((IStatusCodeHttpResult)httpResult).StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        var details = httpResult switch
        {
            ProblemHttpResult p => p.ProblemDetails as HttpValidationProblemDetails,
            ValidationProblem v => v.ProblemDetails,
            _ => null,
        };
        Assert.That(details, Is.Not.Null, "Expected a validation problem payload");
        Assert.That(details!.Errors, Is.EquivalentTo(new Dictionary<string, string[]>
        {
            ["Name"] = ["Nome obrigatório."],
            ["Amount"] = ["Valor deve ser positivo."],
        }));
    }

    [Test]
    public void ToHttpResult_ValidationErrorWithRepeatedCodes_AggregatesMessagesUnderSameKey()
    {
        // Arrange
        var validation = new ValidationError(
        [
            Error.Validation("Nome obrigatório."),
            Error.Validation("Valor deve ser positivo."),
            new Error("Kind", "Tipo inválido.", ErrorType.Validation),
        ]);

        // Act
        var httpResult = Result.Failure(validation).ToHttpResult();

        // Assert
        var details = (httpResult as ProblemHttpResult)?.ProblemDetails as HttpValidationProblemDetails;
        Assert.That(details, Is.Not.Null, "Expected a validation problem payload");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((IStatusCodeHttpResult)httpResult).StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(details!.Errors, Is.EquivalentTo(new Dictionary<string, string[]>
            {
                ["Error.Validation"] = ["Nome obrigatório.", "Valor deve ser positivo."],
                ["Kind"] = ["Tipo inválido."],
            }));
        }
    }

    // --- Result<IEnumerable<T>> ---

    [Test]
    public void ToHttpResultOfEnumerable_EmptyCollection_ReturnsNoContent()
    {
        // Arrange
        Result<IEnumerable<Item>> result = Result.Success<IEnumerable<Item>>(new List<Item>());

        // Act / Assert
        Assert.That(result.ToHttpResult(), Is.InstanceOf<NoContent>());
    }

    [Test]
    public void ToHttpResultOfEnumerable_NonEmptyCollection_ReturnsOkWithSameCollection()
    {
        // Arrange
        var items = new List<Item> { new(1), new(2) };
        Result<IEnumerable<Item>> result = Result.Success<IEnumerable<Item>>(items);

        // Act
        var httpResult = result.ToHttpResult();

        // Assert
        Assert.That(httpResult, Is.InstanceOf<IValueHttpResult>());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(((IStatusCodeHttpResult)httpResult).StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(((IValueHttpResult)httpResult).Value, Is.SameAs(items));
        }
    }

    [Test]
    public void ToHttpResultOfEnumerable_EmptyLazySequence_ReturnsNoContent()
    {
        // Arrange
        Result<IEnumerable<Item>> result = Result.Success(Enumerable.Range(0, 0).Select(i => new Item(i)));

        // Act / Assert
        Assert.That(result.ToHttpResult(), Is.InstanceOf<NoContent>());
    }

    [Test]
    public void ToHttpResultOfEnumerable_NonEmptyLazySequence_ReturnsOkWithMaterializedList()
    {
        // Arrange
        Result<IEnumerable<Item>> result = Result.Success(Enumerable.Range(1, 3).Select(i => new Item(i)));

        // Act
        var httpResult = result.ToHttpResult();

        // Assert
        Assert.That(httpResult, Is.InstanceOf<IValueHttpResult>());
        var value = ((IValueHttpResult)httpResult).Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.InstanceOf<List<Item>>());
            Assert.That(value, Is.EqualTo(new[] { new Item(1), new Item(2), new Item(3) }));
        }
    }

    [Test]
    public void ToHttpResultOfEnumerable_Failure_ReturnsProblem()
    {
        // Arrange
        var error = Error.NotFound("Categoria");
        Result<IEnumerable<Item>> result = Result.Failure<IEnumerable<Item>>(error);

        // Act / Assert
        AssertProblem(result.ToHttpResult(), error, StatusCodes.Status404NotFound);
    }
}
