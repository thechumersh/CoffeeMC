using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CoffeeMC.App.Models;

namespace CoffeeMC.App.Services
{
    public class LauncherProfilesCompatService
    {
        private static string FilePath => Path.Combine(GameInstallerService.MinecraftDir, "launcher_profiles.json");

        // Перезаписывает launcher_profiles.json на основе наших профилей —
        // вызывается при каждом изменении списка, чтобы сторонние установщики видели актуальные данные
        public void Sync(List<GameProfile> profiles)
        {
            try
            {
                var file = new LauncherProfilesFile();
                var nowIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                foreach (var profile in profiles)
                {
                    file.Profiles[profile.Name] = new LauncherProfileEntry
                    {
                        Name = profile.Name,
                        Created = nowIso,
                        LastUsed = nowIso,
                        LastVersionId = profile.VersionId
                    };
                }

                Directory.CreateDirectory(GameInstallerService.MinecraftDir);

                var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // Это вспомогательный файл только для совместимости — сбой записи
                // не должен ронять сам лаунчер, просто молча пропускаем
            }
        }
    }
}