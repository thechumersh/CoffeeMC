using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoffeeMC.App.Models
{
    public class DownloadInfo
    {
        [JsonPropertyName("sha1")]
        public string Sha1 { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("path")]
        public string? Path { get; set; }
    }

    public class VersionDownloads
    {
        [JsonPropertyName("client")]
        public DownloadInfo Client { get; set; } = new();
    }

    public class AssetIndexInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("sha1")]
        public string Sha1 { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }

    public class OsInfo
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class LibraryRule
    {
        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty;

        [JsonPropertyName("os")]
        public OsInfo? Os { get; set; }
    }

    public class ExtractRules
    {
        [JsonPropertyName("exclude")]
        public List<string>? Exclude { get; set; }
    }

    public class LibraryInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("downloads")]
        public LibraryDownloads? Downloads { get; set; }

        [JsonPropertyName("rules")]
        public List<LibraryRule>? Rules { get; set; }

        [JsonPropertyName("natives")]
        public Dictionary<string, string>? Natives { get; set; }

        [JsonPropertyName("extract")]
        public ExtractRules? Extract { get; set; }
    }

    public class LibraryDownloads
    {
        [JsonPropertyName("artifact")]
        public DownloadInfo? Artifact { get; set; }

        [JsonPropertyName("classifiers")]
        public Dictionary<string, DownloadInfo>? Classifiers { get; set; }
    }

    // Новый формат аргументов (версии 1.13+) — смесь простых строк и "условных" объектов (под конкретную ОС/разрешение)
    public class ArgumentsSection
    {
        [JsonPropertyName("game")]
        public List<JsonElement>? Game { get; set; }

        [JsonPropertyName("jvm")]
        public List<JsonElement>? Jvm { get; set; }
    }

    public class JavaVersionInfo
    {
        [JsonPropertyName("component")]
        public string? Component { get; set; }

        [JsonPropertyName("majorVersion")]
        public int MajorVersion { get; set; }
    }

    public class VersionDetails
    {
		[JsonPropertyName("inheritsFrom")]
        public string? InheritsFrom { get; set; }

        [JsonPropertyName("javaVersion")]
        public JavaVersionInfo? JavaVersion { get; set; }
		
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("downloads")]
        public VersionDownloads Downloads { get; set; } = new();

        [JsonPropertyName("assetIndex")]
        public AssetIndexInfo AssetIndex { get; set; } = new();

        [JsonPropertyName("libraries")]
        public List<LibraryInfo> Libraries { get; set; } = new();

        [JsonPropertyName("mainClass")]
        public string MainClass { get; set; } = string.Empty;

        // Старый формат аргументов (версии до 1.13) — просто строка с плейсхолдерами
        [JsonPropertyName("minecraftArguments")]
        public string? MinecraftArguments { get; set; }

        [JsonPropertyName("arguments")]
        public ArgumentsSection? Arguments { get; set; }
    }
}