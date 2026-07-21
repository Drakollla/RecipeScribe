using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Infrastructure.Database
{
    public abstract class RepositoryBase<T> where T : class
    {
        protected RecipeDbContext Context { get; set; }

        protected RepositoryBase(RecipeDbContext context)
        {
            Context = context;
        }

        public IQueryable<T> FindAll(bool trackChanges) =>
            !trackChanges ?
                Context.Set<T>().AsNoTracking() :
                Context.Set<T>();

        public IQueryable<T> FindByCondition(Expression<Func<T, bool>> expression, bool trackChanges) =>
            !trackChanges ?
                Context.Set<T>().Where(expression).AsNoTracking() :
                Context.Set<T>().Where(expression);

        public async Task CreateAsync(T entity) => await Context.Set<T>().AddAsync(entity);
        public void Update(T entity) => Context.Set<T>().Update(entity);
        public void Delete(T entity) => Context.Set<T>().Remove(entity);

        public async Task SaveAsync() => await Context.SaveChangesAsync();
    }
}