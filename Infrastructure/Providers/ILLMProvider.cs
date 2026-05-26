using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Providers
{
    public interface ILLMProvider
    {
        string Name { get; }
        void Register(IServiceCollection services, IConfiguration config);
    }
}