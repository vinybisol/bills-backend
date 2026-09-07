using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Api.Filters;
using Application.Abstractions.Services;
using AutoFixture.Xunit3;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Moq;
using TestCommon;

namespace Api.Tests.Filters;

[ExcludeFromCodeCoverage]
public sealed class UserEndpointFilterTests
{
    [Theory]
    [AutoMoqData]
    internal async Task InvokeAsync_UserNotHaveFirebaseUid_ShouldReturnUnauthorized(
       UserEndpointFilter sut)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var context = EndpointFilterInvocationContext.Create(httpContext);
        var nextDelegate = new EndpointFilterDelegate(_ => ValueTask.FromResult<object?>(null));

        // Act
        var result = await sut.InvokeAsync(context, nextDelegate);

        // Assert
        Assert.Equal(Results.Unauthorized(), result);
    }

    [Theory]
    [AutoMoqData]
    internal async Task InvokeAsync_UserHasFirebaseUid_ShouldReturnContext(
        [Frozen] Mock<IUserProvisioningService> provisioningMock,
        AppUser appUser,
        UserEndpointFilter sut)
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var context = EndpointFilterInvocationContext.Create(httpContext);
        var claimsIdentity = new ClaimsIdentity();
        claimsIdentity.AddClaim(new Claim("user_id", "12345"));
        context.HttpContext.User.AddIdentity(claimsIdentity);

        var delegateVar = false;
        var nextDelegate = new EndpointFilterDelegate((context) =>
        {
            delegateVar = true;
            return ValueTask.FromResult<object?>(context);
        });

        provisioningMock.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), CancellationToken.None))
            .ReturnsAsync(appUser);

        // Act
        var result = await sut.InvokeAsync(context, nextDelegate);

        // Assert
        Assert.True(delegateVar);
    }
}