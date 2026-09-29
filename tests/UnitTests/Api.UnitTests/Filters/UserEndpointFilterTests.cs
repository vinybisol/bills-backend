using System.Security.Claims;
using Api.Filters;
using Application.Abstractions.Services;
using Domain.Abstractions.Filters;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;

namespace Api.UnitTests.Filters;

[TestFixture]
public sealed class UserEndpointFilterTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

    private IUserProvisioningService _provisioning = null!;
    private ICurrentOwner _currentOwner = null!;
    private UserEndpointFilter _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _provisioning = Substitute.For<IUserProvisioningService>();
        _currentOwner = Substitute.For<ICurrentOwner>();
        _sut = new UserEndpointFilter(_provisioning, _currentOwner);
    }

    private static EndpointFilterInvocationContext ContextWith(params Claim[] claims)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test")),
        };
        return EndpointFilterInvocationContext.Create(httpContext);
    }

    private static AppUser UserWithId(long id)
    {
        var user = AppUser.Provision("uid-1", "a@example.com", "Alice", FixedNow);
        typeof(AppUser).GetProperty(nameof(AppUser.Id))!.SetValue(user, id);
        return user;
    }

    [Test]
    public async Task InvokeAsync_NoFirebaseUidClaim_ReturnsUnauthorizedWithoutCallingNext()
    {
        // Arrange
        var context = ContextWith(new Claim("email", "a@example.com"));
        var nextCalled = false;
        EndpointFilterDelegate next = _ => { nextCalled = true; return ValueTask.FromResult<object?>(null); };

        // Act
        var result = await _sut.InvokeAsync(context, next);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.InstanceOf<UnauthorizedHttpResult>());
            Assert.That(nextCalled, Is.False);
        }
        await _provisioning.DidNotReceiveWithAnyArgs().GetOrCreateAsync(default!, default, default, default);
        _currentOwner.DidNotReceiveWithAnyArgs().SetCurrentOwnerId(default);
    }

    [Test]
    public async Task InvokeAsync_AnonymousUser_ReturnsUnauthorized()
    {
        // Arrange
        var context = EndpointFilterInvocationContext.Create(new DefaultHttpContext());

        // Act
        var result = await _sut.InvokeAsync(context, _ => ValueTask.FromResult<object?>(null));

        // Assert
        Assert.That(result, Is.InstanceOf<UnauthorizedHttpResult>());
    }

    [Test]
    public async Task InvokeAsync_AuthenticatedUser_ProvisionsWithTokenClaims()
    {
        // Arrange
        var context = ContextWith(
            new Claim("user_id", "uid-1"), new Claim("email", "a@example.com"), new Claim("name", "Alice"));
        _provisioning.GetOrCreateAsync("uid-1", "a@example.com", "Alice", Arg.Any<CancellationToken>())
            .Returns(UserWithId(42L));

        // Act
        await _sut.InvokeAsync(context, _ => ValueTask.FromResult<object?>(null));

        // Assert
        await _provisioning.Received(1).GetOrCreateAsync("uid-1", "a@example.com", "Alice", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InvokeAsync_AuthenticatedUser_PropagatesRequestAbortedToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var context = ContextWith(new Claim("user_id", "uid-1"));
        context.HttpContext.RequestAborted = cts.Token;
        _provisioning.GetOrCreateAsync("uid-1", null, null, Arg.Any<CancellationToken>()).Returns(UserWithId(42L));

        // Act
        await _sut.InvokeAsync(context, _ => ValueTask.FromResult<object?>(null));

        // Assert
        await _provisioning.Received(1).GetOrCreateAsync("uid-1", null, null, cts.Token);
    }

    [Test]
    public async Task InvokeAsync_AuthenticatedUser_SetsCurrentOwnerBeforeCallingNext()
    {
        // Arrange
        var context = ContextWith(new Claim("user_id", "uid-1"));
        _provisioning.GetOrCreateAsync("uid-1", null, null, Arg.Any<CancellationToken>()).Returns(UserWithId(42L));
        var ownerSetWhenNextRan = false;
        EndpointFilterDelegate next = _ =>
        {
            ownerSetWhenNextRan = _currentOwner.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(ICurrentOwner.SetCurrentOwnerId));
            return ValueTask.FromResult<object?>(null);
        };

        // Act
        await _sut.InvokeAsync(context, next);

        // Assert
        _currentOwner.Received(1).SetCurrentOwnerId(42L);
        Assert.That(ownerSetWhenNextRan, Is.True);
    }

    [Test]
    public async Task InvokeAsync_AuthenticatedUser_ReturnsResultOfNext()
    {
        // Arrange
        var context = ContextWith(new Claim("user_id", "uid-1"));
        _provisioning.GetOrCreateAsync("uid-1", null, null, Arg.Any<CancellationToken>()).Returns(UserWithId(42L));
        var endpointResult = new object();

        // Act
        var result = await _sut.InvokeAsync(context, _ => ValueTask.FromResult<object?>(endpointResult));

        // Assert
        Assert.That(result, Is.SameAs(endpointResult));
    }
}
