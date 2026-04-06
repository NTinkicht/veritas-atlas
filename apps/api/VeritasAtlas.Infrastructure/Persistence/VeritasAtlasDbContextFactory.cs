using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using VeritasAtlas.Infrastructure.Persistence;

namespace VeritasAtlas.Infrastructure.Persistence;

public sealed class VeritasAtlasDbContextFactory : IDesignTimeDbContextFactory<VeritasAtlasDbContext>
{
    public VeritasAtlasDbContext CreateDbContext(string[] args)
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var apiProjectPath = Path.GetFullPath(Path.Combine(currentDirectory, "..", "VeritasAtlas.Api"));

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiProjectPath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

        var optionsBuilder = new DbContextOptionsBuilder<VeritasAtlasDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new VeritasAtlasDbContext(optionsBuilder.Options);
    }
}
