using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CoffeeMC.App.Models
{
    // Описывает один пункт из общего списка версий (то, что лежит в version_manifest_v2.json)
    public class VersionEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        // Тип версии: "release", "snapshot", "old_beta", "old_alpha"
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("releaseTime")]
        public string ReleaseTime { get; set; } = string.Empty;

        // Как версия будет выглядеть в списках и выпадающем меню
        public override string ToString() => $"[{Type}] {Id}";
    }

    // Описывает секцию "latest" в манифесте — какая версия сейчас последний релиз/снапшот
    public class LatestVersions
    {
        [JsonPropertyName("release")]
        public string Release { get; set; } = string.Empty;

        [JsonPropertyName("snapshot")]
        public string Snapshot { get; set; } = string.Empty;
    }

    // Корневой объект — то, во что превращается весь скачанный JSON целиком
    public class VersionManifest
    {
        [JsonPropertyName("latest")]
        public LatestVersions Latest { get; set; } = new();

        [JsonPropertyName("versions")]
        public List<VersionEntry> Versions { get; set; } = new();
    }
}