using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoffeeMC.App.Services
{
    // Токены Microsoft/Minecraft в settings.json шифруются средствами Windows (DPAPI, привязка к пользователю Windows):
    // скопированный файл настроек на другом ПК или у другого пользователя бесполезен.
    // Старые незашифрованные значения читаются как есть и при следующем сохранении шифруются.
    public sealed class ProtectedStringConverter : JsonConverter<string>
    {
        private const string Prefix = "dpapi:";

        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString() ?? string.Empty;
            if (!value.StartsWith(Prefix, StringComparison.Ordinal) || !OperatingSystem.IsWindows())
                return value;

            try
            {
                var data = Convert.FromBase64String(value[Prefix.Length..]);
                var plain = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                // Расшифровать не вышло (файл с другого ПК/пользователя) — считаем, что токена нет, нужен новый вход
                return string.Empty;
            }
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (string.IsNullOrEmpty(value) || !OperatingSystem.IsWindows())
            {
                writer.WriteStringValue(value);
                return;
            }

            try
            {
                var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
                writer.WriteStringValue(Prefix + Convert.ToBase64String(data));
            }
            catch
            {
                writer.WriteStringValue(string.Empty); // лучше потерять токен, чем записать его открытым текстом
            }
        }
    }
}
