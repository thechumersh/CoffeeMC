using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CoffeeMC.App.Models;

namespace CoffeeMC.App.Services
{
    public class ProfileService
    {
        // Папка вида C:\Users\Имя\AppData\Roaming\CoffeeMC
        private static readonly string AppFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CoffeeMC");

        private static readonly string ProfilesFile = Path.Combine(AppFolder, "profiles.json");

        public List<GameProfile> LoadProfiles()
        {
            var loaded = JsonRepair.LoadList<GameProfile>(ProfilesFile);
            bool repaired = JsonRepair.WasRepaired;
            var result = new List<GameProfile>();

            if (loaded != null)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var p in loaded)
                {
                    // Профиль без имени или версии не запустить — выбрасываем; одинаковые имена делаем уникальными
                    if (p == null || string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.VersionId))
                    {
                        repaired = true;
                        continue;
                    }

                    p.JvmArguments ??= string.Empty;
                    p.GameArguments ??= string.Empty;
                    p.JavaPath ??= string.Empty;
                    p.VersionType ??= string.Empty;

                    var name = p.Name;
                    for (int i = 2; !names.Add(name); i++)
                    {
                        name = $"{p.Name} ({i})";
                        repaired = true;
                    }

                    p.Name = name;
                    result.Add(p);
                }
            }

            if (repaired)
            {
                try { SaveProfiles(result); } catch { }
            }

            return result;
        }

        public void SaveProfiles(List<GameProfile> profiles)
        {
            Directory.CreateDirectory(AppFolder);

            var json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
            JsonRepair.Write(ProfilesFile, json);
        }
    }
}