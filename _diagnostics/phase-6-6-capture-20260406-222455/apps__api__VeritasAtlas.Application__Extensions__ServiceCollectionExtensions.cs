using Microsoft.Extensions.DependencyInjection;

namespace VeritasAtlas.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVeritasAtlasApplication(this IServiceCollection services)
    {
        return services;
    }
}
