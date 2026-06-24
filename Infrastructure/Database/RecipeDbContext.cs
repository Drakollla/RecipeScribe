using Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database
{
    public class RecipeDbContext : DbContext
    {
        public DbSet<Recipe> Recipes => Set<Recipe>();
        public DbSet<Ingredient> Ingredients => Set<Ingredient>();
        public DbSet<RecipeStep> Steps => Set<RecipeStep>();
        public DbSet<User> Users => Set<User>();
        public DbSet<MealPlan> MealPlans => Set<MealPlan>();
        public DbSet<MealPlanItem> MealPlanItems => Set<MealPlanItem>();

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

            modelBuilder.Entity<User>()
                .HasIndex(u => u.TelegramChatId)
                .IsUnique();

            modelBuilder.Entity<MealPlan>()
                .HasOne(mp => mp.User)
                .WithMany(u => u.MealPlans)
                .HasForeignKey(mp => mp.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<MealPlan>()
                .HasIndex(mp => new { mp.UserId, mp.Date })
                .IsUnique();

            modelBuilder.Entity<MealPlanItem>()
                .HasOne(mpi => mpi.MealPlan)
                .WithMany(mp => mp.Items)
                .HasForeignKey(mpi => mpi.MealPlanId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<MealPlanItem>()
                .HasOne(mpi => mpi.Recipe)
                .WithMany()
                .HasForeignKey(mpi => mpi.RecipeId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}