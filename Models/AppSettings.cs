using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CoffeeMC.App.Models
{
    public class AccountEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = System.Guid.NewGuid().ToString();

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        // "Offline" (обычный нелицензионный ник) или "Microsoft" (настоящий лицензионный аккаунт)
        [JsonPropertyName("type")]
        public string Type { get; set; } = "Offline";

        // --- Только для Type == "Microsoft" ---
        [JsonPropertyName("uuid")]
        public string Uuid { get; set; } = string.Empty;

        [JsonPropertyName("msRefreshToken")]
        [JsonConverter(typeof(CoffeeMC.App.Services.ProtectedStringConverter))]
        public string MsRefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("mcAccessToken")]
        [JsonConverter(typeof(CoffeeMC.App.Services.ProtectedStringConverter))]
        public string McAccessToken { get; set; } = string.Empty;

        [JsonPropertyName("mcAccessTokenExpiresUtc")]
        public System.DateTime McAccessTokenExpiresUtc { get; set; }

        // Локальная защита профиля PIN-кодом (хранится только хеш) — чтобы на общем ПК
        // друзья не могли просто выбрать чужой сохранённый ник (офлайн или Microsoft) и зайти под ним
        [JsonPropertyName("pinHash")]
        public string? PinHash { get; set; }

        // Для значка "Microsoft" в списках аккаунтов (в файл настроек не пишется)
        [JsonIgnore]
        public bool IsMicrosoft => Type == "Microsoft";

        public override string ToString() => Username;
    }

    public class AppSettings
    {
        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "CoffeeDark";

        [JsonPropertyName("language")]
        public string Language { get; set; } = "ru";

        [JsonPropertyName("accentColor")]
        public string AccentColor { get; set; } = "#E8A33D";

        [JsonPropertyName("minecraftPath")]
        public string MinecraftPath { get; set; } = string.Empty;

        [JsonPropertyName("accounts")]
        public List<AccountEntry> Accounts { get; set; } = new();

        [JsonPropertyName("activeAccountId")]
        public string ActiveAccountId { get; set; } = string.Empty;

        [JsonPropertyName("selectedProfileName")]
        public string SelectedProfileName { get; set; } = string.Empty;

        [JsonPropertyName("minMemoryMb")]
        public int MinMemoryMb { get; set; } = 512;

        [JsonPropertyName("maxMemoryMb")]
        public int MaxMemoryMb { get; set; } = 2048;

        [JsonPropertyName("javaPath")]
        public string JavaPath { get; set; } = string.Empty;

        [JsonPropertyName("launchBehavior")]
        public string LaunchBehavior { get; set; } = "Keep";

        // Радиус скругления углов у кнопок и карточек, в пикселях
        [JsonPropertyName("uiCornerRadius")]
        public int UiCornerRadius { get; set; } = 6;

        // Java, добавленные вручную через менеджер Java (чтобы они всегда оставались в списке)
        [JsonPropertyName("customJavaPaths")]
        public List<string> CustomJavaPaths { get; set; } = new();

        // Если нужной версии Java нет на компьютере — скачать Eclipse Temurin самому
        [JsonPropertyName("autoDownloadJava")]
        public bool AutoDownloadJava { get; set; } = true;

        // Включать праздничные темы (Хэллоуин, Новый год) автоматически в нужные даты
        [JsonPropertyName("autoSeasonalTheme")]
        public bool AutoSeasonalTheme { get; set; } = true;

        // Запоминаем выбор в окне создания профиля (фильтры версий и "установить сразу")
        [JsonPropertyName("profileFilterRelease")]
        public bool ProfileFilterRelease { get; set; } = true;

        [JsonPropertyName("profileFilterSnapshot")]
        public bool ProfileFilterSnapshot { get; set; } = true;

        [JsonPropertyName("profileFilterBeta")]
        public bool ProfileFilterBeta { get; set; }

        [JsonPropertyName("profileFilterAlpha")]
        public bool ProfileFilterAlpha { get; set; }

        [JsonPropertyName("profileFilterLocal")]
        public bool ProfileFilterLocal { get; set; }

        [JsonPropertyName("profileInstallImmediately")]
        public bool ProfileInstallImmediately { get; set; } = true;
    }
}