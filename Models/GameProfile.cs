using System.Text.Json.Serialization;

namespace CoffeeMC.App.Models
{
    // Одна "конфигурация запуска" — имя + версия игры.
    // Сами файлы игры (client.jar и т.д.) сюда не входят — они общие для всех профилей.
    public class GameProfile
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("versionId")]
        public string VersionId { get; set; } = string.Empty;

        [JsonPropertyName("versionType")]
        public string VersionType { get; set; } = string.Empty;

        // Дополнительные JVM-аргументы (например -XX:+UseG1GC), добавляются перед -cp
        [JsonPropertyName("jvmArguments")]
        public string JvmArguments { get; set; } = string.Empty;

        // Дополнительные аргументы Minecraft (например --width 1280), добавляются в конец команды запуска
        [JsonPropertyName("gameArguments")]
        public string GameArguments { get; set; } = string.Empty;

        // Java именно для этого профиля; пусто — берётся из настроек или подбирается автоматически
        [JsonPropertyName("javaPath")]
        public string JavaPath { get; set; } = string.Empty;

        // Показываем в списке как "Название (версия)"
        public override string ToString() => $"{Name} ({VersionId})";
    }
}