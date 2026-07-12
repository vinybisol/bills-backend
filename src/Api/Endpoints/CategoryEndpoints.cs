using Api.Contracts;
using Api.Extensions;
using Api.Filters;
using Application.Abstractions.Services;

namespace Api.Endpoints;

internal static class CategoryEndpoints
{
    public static RouteGroupBuilder MapCategoryEndpoints(this RouteGroupBuilder group)
    {
        var categoryGroup = group
          .MapGroup("/categories")
          .AddEndpointFilter<UserEndpointFilter>();

        categoryGroup.MapPost("", CreateCategory);
        categoryGroup.MapGet("", ListCategories);
        categoryGroup.MapPut("/{id:long}", UpdateCategory);
        categoryGroup.MapDelete("/{id:long}", DeleteCategory);

        return group;
    }

    private static async Task<IResult> CreateCategory(
        CreateCategoryRequest req,
        ICategoryService categoryService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.BadRequest("Name is required.");

        var result = await categoryService.CreateCategoryAsync(req.Name, ct);

        if (result.IsFailure)
            return result.ToHttpResult();

        var category = result.Value;
        return Results.Created($"/api/v1/categories/{category.Id}", category);
    }

    private static async Task<IResult> ListCategories(
        ICategoryService categoryService,
        CancellationToken ct)
    {
        var result = await categoryService.GetAllByNameAsync(ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> UpdateCategory(
        long id,
        UpdateCategoryRequest req,
        ICategoryService categoryService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.BadRequest("Name is required.");

        var result = await categoryService.UpdateAsync(id, req.Name, ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteCategory(
        long id,
        ICategoryService service,
        CancellationToken ct)
    {
        var result = await service.DeleteByIdAsync(id, ct);
        return result.ToHttpResult();
    }
}
