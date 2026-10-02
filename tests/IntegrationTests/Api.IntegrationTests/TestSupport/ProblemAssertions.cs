using System.Net;
using System.Net.Http.Json;

namespace Api.IntegrationTests.TestSupport;

/// <summary>
/// Assertions over the standardized RFC 9457 error contract (ProblemDetails / ValidationProblem,
/// <c>application/problem+json</c>). The stable error code travels in the <c>code</c> extension,
/// never in <c>title</c>.
/// </summary>
internal static class ProblemAssertions
{
    public const string ProblemJson = "application/problem+json";

    public static async Task AssertProblemAsync(
        HttpResponseMessage response, HttpStatusCode expectedStatus, string? expectedCode = null)
    {
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo(ProblemJson));
            Assert.That(problem!.Status, Is.EqualTo((int)expectedStatus));
            Assert.That(problem.Title, Is.Not.Null.And.Not.Empty);
            if (expectedCode is not null)
            {
                Assert.That(problem.Code, Is.EqualTo(expectedCode));
                Assert.That(problem.Title, Is.Not.EqualTo(expectedCode), "The error code must not be the title");
            }
        }
    }

    public static async Task AssertValidationProblemAsync(HttpResponseMessage response, params string[] expectedFields)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemBody>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo(ProblemJson));
            Assert.That(problem!.Status, Is.EqualTo(400));
            Assert.That(problem.Code, Is.EqualTo("Error.Validation"));
            Assert.That(problem.Errors.Keys, Is.EquivalentTo(expectedFields));
            Assert.That(problem.Errors.Values, Has.All.Not.Empty);
        }
    }

    private sealed record ProblemBody(int Status, string? Title, string? Detail, string? Code);

    private sealed record ValidationProblemBody(
        int Status, Dictionary<string, string[]> Errors, string? Code);
}
