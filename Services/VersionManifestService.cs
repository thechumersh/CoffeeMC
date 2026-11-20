using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CoffeeMC.App.Models;

namespace CoffeeMC.App.Services
{
    public class VersionManifestService
    {
        private const string ManifestUrl =
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

        private readonly HttpClient _httpClient = NetClient.Shared;

        // Список версий меняется редко — держим копию 10 минут, чтобы каждое открытие окон не ходило в сеть
        private static VersionManifest? _cached;
        private static System.DateTime _cachedAt;

        public static void Invalidate() => _cached = null;

        public async Task<VersionManifest?> GetManifestAsync()
        {
            if (_cached != null && System.DateTime.UtcNow - _cachedAt < System.TimeSpan.FromMinutes(10))
                return _cached;

            var manifest = await _httpClient.GetFromJsonAsync<VersionManifest>(ManifestUrl);
            if (manifest != null)
            {
                _cached = manifest;
                _cachedAt = System.DateTime.UtcNow;
            }

            return manifest;
        }
    }
}