using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CoffeeMC.App.Models;
using CoffeeMC.App.Services;

namespace CoffeeMC.App.Services
{
    public class LocalVersionInfo
    {
        public string Id { get; set; } = string.Empty;
        public string? InheritsFrom { get; set; }
        public override string ToString() =>
            InheritsFrom != null
                ? Localization.Get("CreateProfile_LocalItemBaseFormat", Id, InheritsFrom)
                : Localization.Get("CreateProfile_LocalItemFormat", Id);
    }

    public class LocalVersionService
    {
        // Сканирует .minecraft/versions — находит версии, поставленные сторонними
        // установщиками (OptiFine, Forge, Fabric), про которые Coffee MC сам не знает
        public List<LocalVersionInfo> ScanLocalVersions()
        {
            var result = new List<LocalVersionInfo>();
            var versionsRoot = Path.Combine(GameInstallerService.MinecraftDir, "versions");

            if (!Directory.Exists(versionsRoot))
                return result;

            foreach (var folder in Directory.GetDirectories(versionsRoot))
            {
                var id = Path.GetFileName(folder);
                var jsonPath = Path.Combine(folder, $"{id}.json");

                if (!File.Exists(jsonPath))
                    continue;

                try
                {
                    var json = File.ReadAllText(jsonPath);
                    var details = JsonSerializer.Deserialize<VersionDetails>(json);

                    result.Add(new LocalVersionInfo { Id = id, InheritsFrom = details?.InheritsFrom });
                }
                catch
                {
                    // Битый json — просто пропускаем эту папку
                }
            }

            return result;
        }
    }
}