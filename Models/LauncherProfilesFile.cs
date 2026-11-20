using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CoffeeMC.App.Models
{
    // Формат launcher_profiles.json оригинального лаунчера Mojang — нужен только
    // для совместимости со сторонними установщиками (OptiFine и подобные),
    // сам Coffee MC этот файл не читает, только поддерживает его актуальным
    public class LauncherProfileEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = "custom";

        [JsonPropertyName("created")]
        public string Created { get; set; } = string.Empty;

        [JsonPropertyName("lastUsed")]
        public string LastUsed { get; set; } = string.Empty;

        [JsonPropertyName("lastVersionId")]
        public string LastVersionId { get; set; } = string.Empty;

        [JsonPropertyName("icon")]
        public string Icon { get; set; } = "Furnace";
    }

    public class LauncherProfilesSettings
    {
        [JsonPropertyName("enableSnapshots")]
        public bool EnableSnapshots { get; set; } = true;

        [JsonPropertyName("enableAdvanced")]
        public bool EnableAdvanced { get; set; } = true;

        [JsonPropertyName("keepLauncherOpen")]
        public bool KeepLauncherOpen { get; set; } = true;
    }

    public class LauncherProfilesFile
    {
        [JsonPropertyName("profiles")]
        public Dictionary<string, LauncherProfileEntry> Profiles { get; set; } = new();

        [JsonPropertyName("settings")]
        public LauncherProfilesSettings Settings { get; set; } = new();

        [JsonPropertyName("version")]
        public int Version { get; set; } = 3;
    }
}