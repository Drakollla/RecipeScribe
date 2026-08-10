using Core.Models;

namespace Core.Contracts;

public interface ILlmProfileStore
{
    IReadOnlyList<LlmProfile> GetAll();
    LlmProfile? Get(string name);
    void Save(LlmProfile profile);
    void Delete(string name);
}
