using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CoffeeMC.App.Models;

namespace CoffeeMC.App.Services
{
    public class SettingsService
    {
        private static readonly string AppFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CoffeeMC");

        private static readonly string SettingsFile = Path.Combine(AppFolder, "settings.json");

        // Читаем снисходительно: лишние запятые и комментарии в settings.json (если его правили руками) не ломают запуск
        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public AppSettings Load()
        {
            var loaded = JsonRepair.LoadObject<AppSettings>(SettingsFile);
            var repaired = JsonRepair.WasRepaired;

            var settings = loaded ?? new AppSettings();
            if (Sanitize(settings))
                repaired = true;

            // Если что-то пришлось чинить — сразу записываем исправленный файл
            if (repaired)
            {
                try { Save(settings); } catch { }
            }

            return settings;
        }

        // Приводит значения к допустимым: неверные настройки заменяются разумными, а не роняют лаунчер
        // Возвращает true, если что-то пришлось исправить
        private static bool Sanitize(AppSettings s)
        {
            bool changed = false;

            s.Accounts ??= new(); 
            s.CustomJavaPaths ??= new();

            var before = s.Accounts.Count;
            s.Accounts.RemoveAll(a => a == null || string.IsNullOrWhiteSpace(a.Username));
            if (s.Accounts.Count != before) changed = true;

            var ids = new HashSet<string>();
            foreach (var a in s.Accounts)
            {
                if (string.IsNullOrWhiteSpace(a.Id) || !ids.Add(a.Id))
                {
                    a.Id = Guid.NewGuid().ToString();
                    ids.Add(a.Id);
                    changed = true;
                }

                if (a.Type != "Offline" && a.Type != "Microsoft")
                {
                    a.Type = "Offline";
                    changed = true;
                }
            }

            if (!string.IsNullOrEmpty(s.ActiveAccountId) && !s.Accounts.Exists(a => a.Id == s.ActiveAccountId))
            {
                s.ActiveAccountId = s.Accounts.Count > 0 ? s.Accounts[0].Id : string.Empty;
                changed = true;
            }

            var min = s.MinMemoryMb; var max = s.MaxMemoryMb;
            if (min < 128 || min > 65536) min = 512;
            if (max < 256 || max > 65536) max = 2048;
            if (min > max) min = max;
            if (min != s.MinMemoryMb) { s.MinMemoryMb = min; changed = true; }
            if (max != s.MaxMemoryMb) { s.MaxMemoryMb = max; changed = true; }

            var radius = Math.Clamp(s.UiCornerRadius, 0, 30);
            if (radius != s.UiCornerRadius) { s.UiCornerRadius = radius; changed = true; }

            if (s.Language != "ru" && s.Language != "en") { s.Language = "ru"; changed = true; }
            if (s.LaunchBehavior != "Keep" && s.LaunchBehavior != "Minimize" && s.LaunchBehavior != "Close")
            {
                s.LaunchBehavior = "Keep";
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(s.Theme)) { s.Theme = "CoffeeDark"; changed = true; }
            if (string.IsNullOrWhiteSpace(s.AccentColor) || !Avalonia.Media.Color.TryParse(s.AccentColor, out _))
            {
                s.AccentColor = "#E8A33D";
                changed = true;
            }

            s.JavaPath ??= string.Empty;
            s.SelectedProfileName ??= string.Empty;
            s.MinecraftPath ??= string.Empty;

            return changed;
        }

        public void Save(AppSettings settings)
        {
            Directory.CreateDirectory(AppFolder);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            JsonRepair.Write(SettingsFile, json);
        }
    }
}