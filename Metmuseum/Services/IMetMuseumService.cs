using Newtonsoft.Json.Linq;

namespace Metmuseum.Services
{
    public interface IMetMuseumService
    {
        Task<IReadOnlyList<int>> GetAllIdsAsync(CancellationToken ct = default);
        Task<JObject?> GetObjectAsync(int id, CancellationToken ct = default);
    }
}
