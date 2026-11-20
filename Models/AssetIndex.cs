using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CoffeeMC.App.Models
{
    public class AssetObject
    {
        [JsonPropertyName("hash")]
        public string Hash { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    public class AssetIndex
    {
        [JsonPropertyName("objects")]
        public Dictionary<string, AssetObject> Objects { get; set; } = new();

        // true у версий до 1.6 — нужно дополнительно разложить файлы в "виртуальную" папку
        // с обычными именами (не хешами), потому что старая игра ищет их именно так
        [JsonPropertyName("virtual")]
        public bool Virtual { get; set; }

        // true у самых старых версий — звуки дополнительно нужны прямо в .minecraft/resources,
        // потому что такая старая игра вообще не знает о папке assets
        [JsonPropertyName("map_to_resources")]
        public bool MapToResources { get; set; }
    }
}