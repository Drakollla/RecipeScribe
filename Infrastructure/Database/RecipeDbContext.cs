using Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database
{
    public class RecipeDbContext : DbContext
    {
        public DbSet<Recipe> Recipes => Set<Recipe>();
        public DbSet<Ingredient> Ingredients => Set<Ingredient>();
        public DbSet<RecipeStep> Steps => Set<RecipeStep>();

        public RecipeDbContext(DbContextOptions<RecipeDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Recipe>()
                .HasMany(r => r.Ingredients)
                .WithOne(i => i.Recipe)     
                .HasForeignKey(i => i.RecipeId) 
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Recipe>()
                .HasMany(r => r.Steps)      
                .WithOne(s => s.Recipe)     
                .HasForeignKey(s => s.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}