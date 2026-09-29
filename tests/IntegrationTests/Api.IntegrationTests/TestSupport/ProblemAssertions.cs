using System.Net;
using System.Net.Http.Json;

namespace Api.IntegrationTests.TestSupport;

/// <summary>Assertions over the standardized error contract (ProblemDetails / ValidationProblem).</summary>
internal static class ProblemAssertions
{
    public static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(problem!.Status, Is.EqualTo((int)expectedStatus));
        }
    }

    public static async Task AssertValidationProblemAsync(HttpResponseMessage response, params string[] expectedFields)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemBody>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.Location, Is.Null);
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(problem!.Status, Is.EqualTo(400));
            Assert.That(problem.Errors.Keys, Is.EquivalentTo(expectedFields));
            Assert.That(problem.Errors.Values, Has.All.Not.Empty);
        }
    }

    private sealed record ProblemBody(int Status, string? Title, string? Detail);

    private sealed record ValidationProblemBody(int Status, Dictionary<string, string[]> Errors);
}
