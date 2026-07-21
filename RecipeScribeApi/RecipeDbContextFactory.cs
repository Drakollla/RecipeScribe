using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RecipeScribeApi;

public class RecipeDbContextFactory : IDesignTimeDbContextFactory<RecipeDbContext>
{
    public RecipeDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets<Program>(optional: true)
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<RecipeDbContext>();
        optionsBuilder.UseSqlite(configuration.GetConnectionString("DefaultConnection"));
        return new RecipeDbContext(optionsBuilder.Options);
    }
}
