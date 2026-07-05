using Microsoft.Extensions.DependencyInjection;

namespace Application.DependencyInjection;

public static class RegisterApplications
{
    public static void Register(IServiceCollection services)
    {
        ResolveServices(services);

    }

    private static void ResolveServices(IServiceCollection services)
    {
    }
}
