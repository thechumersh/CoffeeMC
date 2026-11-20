using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CoffeeMC.App.Models;

namespace CoffeeMC.App.Services
{
    // Определяет, какая мажорная версия Java нужна конкретной версии Minecraft,
    // и ищет подходящую установленную JDK/JRE на диске — без запуска java -version
    // для каждого кандидата (быстро: версия почти всегда видна прямо в имени папки).
    public static class JavaLocatorService
    {
        // Официальный javaVersion.majorVersion в паспорте версии есть не всегда
        // (особенно у старых/кастомных версий) — тогда используем этот запасной порог,
        // тот же, что применяют большинство лаунчеров (MultiMC/Prism):
        // Java 8 — Minecraft ≤ 1.16.5, Java 17 — 1.17–1.20.4, Java 21 — 1.20.5+
        public static int GetRequiredMajorVersion(VersionDetails details, string versionId)
        {
            if (details.JavaVersion != null && details.JavaVersion.MajorVersion > 0)
                return details.JavaVersion.MajorVersion;

            var match = Regex.Match(versionId, @"^1\.(\d+)(?:\.(\d+))?");
            if (!match.Success)
                return 17; // неизвестный формат id (снапшот/кастомная сборка) — безопасный современный дефолт

            int minor = int.Parse(match.Groups[1].Value);
            int patch = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;

            if (minor < 17) return 8;
            if (minor == 20 && patch >= 5) return 21;
            if (minor > 20) return 21;
            return 17;
        }

        // Подходящая Java (точная версия, либо более новая для 17+). null — подходящей на компьютере нет.
        public static string? FindCompatibleExecutable(int requiredMajor, IEnumerable<string>? customPaths = null)
        {
            var all = JavaDiscoveryService.FindAll(customPaths);
            return JavaDiscoveryService.FindCompatible(requiredMajor, all)?.Executable;
        }

        // Возвращает путь к javaw.exe нужной версии, либо null, если не нашлось вообще никакой Java.
        // Если точного совпадения нет — берёт ближайшую из имеющихся (лучше так, чем ничего).
        public static string? FindJavaExecutable(int requiredMajor, IEnumerable<string>? customPaths = null)
        {
            var all = JavaDiscoveryService.FindAll(customPaths);

            var compatible = JavaDiscoveryService.FindCompatible(requiredMajor, all);
            if (compatible != null)
                return compatible.Executable;

            var below = all.Where(i => i.Major <= requiredMajor).OrderByDescending(i => i.Major).FirstOrDefault();
            if (below != null)
                return below.Executable;

            return all.OrderByDescending(i => i.Major).FirstOrDefault()?.Executable;
        }
    }
}
