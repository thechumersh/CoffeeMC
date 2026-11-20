using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using CoffeeMC.App.Models;
using Microsoft.Win32;

namespace CoffeeMC.App.Services
{
    // Ищет Java по всему компьютеру: переменные окружения, PATH, реестр, стандартные папки
    // производителей, встроенные Java лаунчеров Minecraft и Java, скачанные самим лаунчером.
    // Версия и производитель берутся из файла "release" внутри папки Java (без запуска процесса);
    // только если его нет — запускается "java -version".
    public static class JavaDiscoveryService
    {
        public static string ManagedRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoffeeMC", "java");

        public static List<JavaInstall> FindAll(IEnumerable<string>? customExecutables = null)
        {
            var found = new Dictionary<string, JavaInstall>(StringComparer.OrdinalIgnoreCase);

            void Add(string? exe, JavaSource? forced = null)
            {
                if (string.IsNullOrWhiteSpace(exe))
                    return;

                string full;
                try { full = NormalizeExecutable(Path.GetFullPath(exe)); }
                catch { return; }

                if (!File.Exists(full) || IsStub(full) || found.ContainsKey(full))
                    return;

                var install = Inspect(full, forced ?? ClassifySource(full));
                if (install != null)
                    found[full] = install;
            }

            void ScanRoot(string? root, int depth)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                    return;

                WalkForJava(root, depth, exe => Add(exe));
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            // 1. Скачанные самим лаунчером и встроенные в лаунчеры Minecraft
            ScanRoot(ManagedRoot, 3);
            ScanRoot(Path.Combine(GameInstallerService.MinecraftDir, "runtime"), 4);
            ScanRoot(Path.Combine(localAppData, "Packages", "Microsoft.4297127D64EC6_8wekyb3d8bbwe", "LocalCache", "Local", "runtime"), 4);
            ScanRoot(Path.Combine(appData, "PrismLauncher", "java"), 3);
            ScanRoot(Path.Combine(appData, "PolyMC", "java"), 3);

            // 2. Переменные окружения
            foreach (var variable in new[] { "JAVA_HOME", "JDK_HOME", "JRE_HOME" })
            {
                var home = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrWhiteSpace(home))
                    Add(Path.Combine(home.Trim().Trim('"'), "bin", "javaw.exe"));
            }

            // 3. PATH
            var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var entry in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var dir = entry.Trim().Trim('"');
                    Add(Path.Combine(dir, "javaw.exe"));
                    Add(Path.Combine(dir, "java.exe"));
                }
                catch
                {
                    // кривая запись в PATH — пропускаем
                }
            }

            // 4. Реестр (установщики Oracle, Adoptium, Microsoft, Azul, Corretto, Liberica...)
            if (OperatingSystem.IsWindows())
            {
                foreach (var home in ReadRegistryHomes())
                    Add(Path.Combine(home, "bin", "javaw.exe"));
            }

            // 5. Стандартные папки производителей
            var vendorFolders = new[]
            {
                "Java", "Eclipse Adoptium", "Eclipse Foundation", "AdoptOpenJDK", "Zulu", "Microsoft",
                "BellSoft", "Amazon Corretto", "Semeru", "GraalVM", "OpenJDK", "JetBrains", "Android"
            };
            foreach (var pf in new[] { programFiles, programFilesX86 }.Where(p => !string.IsNullOrEmpty(p)).Distinct())
            {
                foreach (var vendor in vendorFolders)
                    ScanRoot(Path.Combine(pf, vendor), 3);
            }

            ScanRoot(Path.Combine(userProfile, ".jdks"), 2);
            ScanRoot(Path.Combine(localAppData, "Programs"), 3);

            // 6. Вручную добавленные пользователем (последними: если это же Java нашлась выше, останется «найдена в системе»)
            if (customExecutables != null)
            {
                foreach (var custom in customExecutables)
                    Add(custom, JavaSource.Custom);
            }

            return found.Values
                .OrderByDescending(i => i.Major)
                .ThenBy(i => i.Architecture == "x86" ? 1 : 0)
                .ThenByDescending(i => VersionKey(i.Version))
                .ToList();
        }

        // Подбирает Java под нужную мажорную версию игры. null — подходящей нет.
        // Для Java 8 подходит только 8 (на 9+ старые версии Minecraft и Forge обычно не запускаются),
        // для 17+ подойдёт и более новая.
        public static JavaInstall? FindCompatible(int requiredMajor, IEnumerable<JavaInstall> installs)
        {
            return installs
                .Where(i => i.Major == requiredMajor || (requiredMajor >= 17 && i.Major > requiredMajor))
                .OrderBy(i => i.Major == requiredMajor ? 0 : 1)
                .ThenBy(i => i.Major)
                .ThenBy(i => i.Architecture == "x86" ? 1 : 0)
                .ThenByDescending(i => VersionKey(i.Version))
                .FirstOrDefault();
        }

        public static string NormalizeExecutable(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir))
                return path;

            var javaw = Path.Combine(dir, "javaw.exe");
            if (File.Exists(javaw))
                return javaw;

            var java = Path.Combine(dir, "java.exe");
            return File.Exists(java) ? java : path;
        }

        public static JavaInstall? Inspect(string executable, JavaSource source)
        {
            try
            {
                var exe = NormalizeExecutable(executable);
                var binDir = Path.GetDirectoryName(exe);
                var home = binDir == null ? null : Directory.GetParent(binDir)?.FullName;
                if (home == null || !File.Exists(exe))
                    return null;

                string? version = null, vendor = null, arch = null, image = null;

                var releaseFile = Path.Combine(home, "release");
                if (File.Exists(releaseFile))
                {
                    foreach (var line in File.ReadAllLines(releaseFile))
                    {
                        var m = Regex.Match(line, "^(\\w+)=\"?(.*?)\"?\\s*$");
                        if (!m.Success)
                            continue;

                        var value = m.Groups[2].Value;
                        switch (m.Groups[1].Value)
                        {
                            case "JAVA_VERSION": version = value; break;
                            case "IMPLEMENTOR": vendor = value; break;
                            case "OS_ARCH": arch = value; break;
                            case "IMAGE_TYPE": image = value; break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(version))
                    ProbeWithJavaVersion(exe, out version, out vendor, out arch);

                if (string.IsNullOrEmpty(version))
                    return null;

                var major = ParseMajor(version);
                if (major <= 0)
                    return null;

                if (string.IsNullOrEmpty(image))
                    image = File.Exists(Path.Combine(home, "bin", "javac.exe")) ? "JDK" : "JRE";

                return new JavaInstall
                {
                    Executable = exe,
                    HomeDir = home,
                    Major = major,
                    Version = version,
                    Vendor = NormalizeVendor(vendor),
                    Architecture = NormalizeArch(arch),
                    ImageType = image.ToUpperInvariant(),
                    Source = source
                };
            }
            catch
            {
                return null;
            }
        }

        public static int ParseMajor(string version)
        {
            var m = Regex.Match(version, @"^(\d+)(?:\.(\d+))?");
            if (!m.Success)
                return 0;

            int first = int.Parse(m.Groups[1].Value);
            // старая схема: 1.8.0_392 -> 8
            if (first == 1 && m.Groups[2].Success)
                return int.Parse(m.Groups[2].Value);

            return first;
        }

        // Сравнимое число из версии: 17.0.10 > 17.0.9
        public static long VersionKey(string version)
        {
            long key = 0;
            foreach (Match m in Regex.Matches(version ?? string.Empty, @"\d+").Cast<Match>().Take(4))
                key = key * 1000 + Math.Min(long.Parse(m.Value.Length > 3 ? "999" : m.Value), 999);
            return key;
        }

        private static JavaSource ClassifySource(string exe)
        {
            if (exe.StartsWith(ManagedRoot, StringComparison.OrdinalIgnoreCase))
                return JavaSource.Managed;

            var mcRuntime = Path.Combine(GameInstallerService.MinecraftDir, "runtime");
            if (exe.StartsWith(mcRuntime, StringComparison.OrdinalIgnoreCase) ||
                exe.Contains("Microsoft.4297127D64EC6", StringComparison.OrdinalIgnoreCase))
                return JavaSource.MinecraftRuntime;

            return JavaSource.System;
        }

        // Ярлыки-заглушки Windows (Oracle javapath, WindowsApps, System32) — это не сами установки Java
        private static bool IsStub(string exe) =>
            exe.Contains(@"\Common Files\Oracle\Java\javapath", StringComparison.OrdinalIgnoreCase) ||
            exe.Contains(@"\Microsoft\WindowsApps", StringComparison.OrdinalIgnoreCase) ||
            exe.Contains(@"\Windows\System32", StringComparison.OrdinalIgnoreCase);

        private static void WalkForJava(string dir, int depth, Action<string> onFound)
        {
            var bin = Path.Combine(dir, "bin");
            var javaw = Path.Combine(bin, "javaw.exe");
            var java = Path.Combine(bin, "java.exe");

            if (File.Exists(javaw)) { onFound(javaw); return; }
            if (File.Exists(java)) { onFound(java); return; }
            if (depth <= 0)
                return;

            foreach (var sub in SafeDirectories(dir))
                WalkForJava(sub, depth - 1, onFound);
        }

        private static IEnumerable<string> SafeDirectories(string dir)
        {
            try { return Directory.GetDirectories(dir); }
            catch { return Array.Empty<string>(); }
        }

        [SupportedOSPlatform("windows")]
        private static List<string> ReadRegistryHomes()
        {
            var result = new List<string>();
            var roots = new[]
            {
                @"SOFTWARE\JavaSoft", @"SOFTWARE\Eclipse Adoptium", @"SOFTWARE\Eclipse Foundation",
                @"SOFTWARE\AdoptOpenJDK", @"SOFTWARE\Microsoft\JDK", @"SOFTWARE\Azul Systems\Zulu",
                @"SOFTWARE\Amazon\Corretto", @"SOFTWARE\BellSoft", @"SOFTWARE\Semeru"
            };

            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);

                    foreach (var root in roots)
                    {
                        using var key = baseKey.OpenSubKey(root);
                        if (key != null)
                            CollectRegistry(key, 4, result);
                    }
                }
                catch
                {
                    // нет доступа к реестру — просто ищем другими способами
                }
            }

            return result;
        }

        [SupportedOSPlatform("windows")]
        private static void CollectRegistry(RegistryKey key, int depth, List<string> result)
        {
            foreach (var name in new[] { "JavaHome", "Path", "InstallationPath", "InstallDir" })
            {
                if (key.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value))
                    result.Add(value.Trim().Trim('"'));
            }

            if (depth <= 0)
                return;

            foreach (var sub in key.GetSubKeyNames())
            {
                try
                {
                    using var subKey = key.OpenSubKey(sub);
                    if (subKey != null)
                        CollectRegistry(subKey, depth - 1, result);
                }
                catch
                {
                    // раздел недоступен — пропускаем
                }
            }
        }

        // Запасной способ, если нет файла release (например, у старых JRE 8): спрашиваем у самой Java
        private static void ProbeWithJavaVersion(string exe, out string? version, out string? vendor, out string? arch)
        {
            version = null;
            vendor = null;
            arch = null;

            try
            {
                var dir = Path.GetDirectoryName(exe)!;
                var javaExe = Path.Combine(dir, "java.exe");
                if (!File.Exists(javaExe))
                    javaExe = exe;

                var psi = new ProcessStartInfo(javaExe, "-version")
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return;

                var errTask = process.StandardError.ReadToEndAsync();
                var outTask = process.StandardOutput.ReadToEndAsync();

                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(true); } catch { }
                    return;
                }

                var text = errTask.Result + "\n" + outTask.Result;

                var m = Regex.Match(text, "version \"([^\"]+)\"");
                if (!m.Success)
                    return;

                version = m.Groups[1].Value;
                vendor = text;
                arch = text.Contains("64-Bit", StringComparison.OrdinalIgnoreCase) ? "x64" : "x86";
            }
            catch
            {
                // не удалось запустить — вернём пустые значения
            }
        }

        private static string NormalizeVendor(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            bool Has(string s) => raw.Contains(s, StringComparison.OrdinalIgnoreCase);

            if (Has("Temurin") || Has("Eclipse Adoptium")) return "Eclipse Temurin";
            if (Has("AdoptOpenJDK")) return "AdoptOpenJDK";
            if (Has("Azul") || Has("Zulu")) return "Azul Zulu";
            if (Has("Corretto") || Has("Amazon")) return "Amazon Corretto";
            if (Has("Microsoft")) return "Microsoft";
            if (Has("Liberica") || Has("BellSoft")) return "BellSoft Liberica";
            if (Has("GraalVM")) return "GraalVM";
            if (Has("JetBrains")) return "JetBrains Runtime";
            if (Has("Semeru") || Has("IBM")) return "IBM Semeru";
            if (Has("Oracle") || Has("Java(TM)")) return "Oracle";
            if (Has("OpenJDK")) return "OpenJDK";

            var line = raw.Split('\n')[0].Trim();
            return line.Length > 40 ? line[..40] : line;
        }

        private static string NormalizeArch(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var a = raw.Trim().ToLowerInvariant();
            if (a is "x86_64" or "amd64" or "x64") return "x64";
            if (a is "aarch64" or "arm64") return "arm64";
            if (a is "x86" or "i386" or "i586" or "i686") return "x86";
            return raw.Trim();
        }
    }
}
