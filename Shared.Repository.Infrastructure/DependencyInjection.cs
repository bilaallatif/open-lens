using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Shared.Repository.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRepositoryServices(this IServiceCollection services)
    {
        services.AddOptions<RepositoryOptions>().BindConfiguration("Repository");

        services.AddSingleton<IMongoClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RepositoryOptions>>().Value;
            return new MongoClient(options.ConnectionString);
        });

        services.AddSingleton<IMongoDatabase>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RepositoryOptions>>().Value;
            return sp.GetRequiredService<IMongoClient>().GetDatabase(options.DatabaseName);
        });

        return services;
    }
}
