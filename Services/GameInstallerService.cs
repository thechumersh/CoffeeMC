using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CoffeeMC.App.Models;

namespace CoffeeMC.App.Services
{
    public class GameInstallerService
    {
        public static string MinecraftDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft");

        private const string ResourcesBaseUrl = "https://resources.download.minecraft.net";

        private readonly HttpClient _httpClient = NetClient.Shared;

        public async Task<VersionDetails?> GetVersionDetailsAsync(string url)
        {
            return await _httpClient.GetFromJsonAsync<VersionDetails>(url);
        }

        private static string ComputeSha1(string filePath)
        {
            using var sha1 = SHA1.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = sha1.ComputeHash(stream);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private async Task EnsureFileAsync(string url, string destinationPath, string? expectedSha1, IProgress<string> progress, bool logEachFile = true, bool forceDownload = false)
        {
            bool needsDownload = true;

            if (!forceDownload && File.Exists(destinationPath))
            {
                if (string.IsNullOrEmpty(expectedSha1))
                {
                    // Хеш не известен (библиотека без блока "downloads" в паспорте версии) —
                    // раз файл уже на месте, доверяем ему и не качаем заново каждый запуск
                    needsDownload = false;
                }
                else
                {
                    var actualSha1 = ComputeSha1(destinationPath);
                    needsDownload = !string.Equals(actualSha1, expectedSha1, StringComparison.OrdinalIgnoreCase);
                }
            }

            if (!needsDownload)
            {
                if (logEachFile) progress.Report(Localization.Get("Installer_HashOkFormat", Path.GetFileName(destinationPath)));
                return;
            }

            if (logEachFile) progress.Report(Localization.Get("Installer_DownloadingFormat", Path.GetFileName(destinationPath)));

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            // Качаем во временный файл и только потом подменяем: оборванная загрузка больше не оставляет
            // битый файл на месте настоящего (раньше он считался "уже скачанным", если хеш неизвестен)
            if (!NetClient.IsSecureUrl(url))
                throw new IOException($"Небезопасная ссылка (разрешён только https): {url}");

            // Уникальное имя .part — параллельные загрузки одного и того же файла не портят друг друга
            var tempPath = destinationPath + "." + Guid.NewGuid().ToString("N")[..6] + ".part";

            // До 3 попыток: короткий сбой сети не должен ронять установку на 3000-м файле ресурсов
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        await using var source = await response.Content.ReadAsStreamAsync();
                        await using var target = File.Create(tempPath);
                        await source.CopyToAsync(target);
                    }

                    if (!string.IsNullOrEmpty(expectedSha1) &&
                        !string.Equals(ComputeSha1(tempPath), expectedSha1, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException($"Контрольная сумма не совпала: {Path.GetFileName(destinationPath)}");
                    }

                    break;
                }
                catch (Exception) when (attempt < 3)
                {
                    try { File.Delete(tempPath); } catch { }
                    await Task.Delay(500 * attempt);
                }
                catch
                {
                    try { File.Delete(tempPath); } catch { }
                    throw;
                }
            }

            File.Move(tempPath, destinationPath, overwrite: true);

            if (logEachFile) progress.Report(Localization.Get("Installer_DoneFormat", Path.GetFileName(destinationPath)));
        }

        internal static bool IsLibraryAllowedOnWindows(LibraryInfo library)
        {
            if (library.Rules == null || library.Rules.Count == 0)
                return true;

            bool allowed = false;

            foreach (var rule in library.Rules)
            {
                bool matchesOs = rule.Os == null ||
                                  string.Equals(rule.Os.Name, "windows", StringComparison.OrdinalIgnoreCase);

                if (matchesOs)
                {
                    allowed = rule.Action == "allow";
                }
            }

            return allowed;
        }

        public async Task InstallLibrariesAsync(VersionDetails details, IProgress<string> progress)
        {
            var librariesRoot = Path.Combine(MinecraftDir, "libraries");
            int total = details.Libraries.Count;
            int current = 0;

            foreach (var library in details.Libraries)
            {
                current++;

                if (!IsLibraryAllowedOnWindows(library))
                    continue;

                if (library.Natives != null)
                    continue; // natives ставятся отдельным шагом (InstallNativesAsync)

                var artifact = library.Downloads?.Artifact;
                string? relativePath = artifact?.Path;
                string? url = artifact?.Url;
                string? sha1 = artifact?.Sha1;

                if (string.IsNullOrEmpty(relativePath))
                {
                    // У OptiFine/Forge/LaunchWrapper в паспорте версии часто нет блока "downloads" —
                    // только maven-имя "группа:артефакт:версия". Официальный лаунчер в этом случае
                    // качает библиотеку с центрального maven-репозитория Mojang по стандартному пути;
                    // без этого файл вроде launchwrapper.jar никогда не появляется на диске.
                    relativePath = MavenNameToPath(library.Name);
                    if (relativePath == null)
                        continue;

                    url = "https://libraries.minecraft.net/" + relativePath;
                    sha1 = null;
                }

                if (string.IsNullOrEmpty(url))
                    continue;

                // Путь из чужого json не должен выходить за папку libraries
                var destinationPath = FileHelper.SafeCombine(librariesRoot, relativePath);
                if (destinationPath == null)
                {
                    progress.Report($"[WARN] Пропущена библиотека с небезопасным путём: {library.Name}");
                    continue;
                }

                progress.Report($"[{current}/{total}] {library.Name}");
                await EnsureFileAsync(url, destinationPath, sha1, progress);
            }
        }

        // "net.minecraft:launchwrapper:1.12" -> "net/minecraft/launchwrapper/1.12/launchwrapper-1.12.jar"
        private static string? MavenNameToPath(string name)
        {
            var parts = name.Split(':');
            if (parts.Length < 3)
                return null;

            var group = parts[0].Replace('.', '/');
            var artifactId = parts[1];
            var version = parts[2];
            var classifier = parts.Length > 3 ? "-" + parts[3] : string.Empty;

            return $"{group}/{artifactId}/{version}/{artifactId}-{version}{classifier}.jar";
        }

        public async Task InstallNativesAsync(VersionEntry version, VersionDetails details, IProgress<string> progress)
        {
            var nativesFolder = Path.Combine(MinecraftDir, "versions", version.Id, "natives");
            Directory.CreateDirectory(nativesFolder);

            var tempDownloadFolder = Path.Combine(Path.GetTempPath(), "CoffeeMC_natives");
            Directory.CreateDirectory(tempDownloadFolder);

            var nativeLibraries = new List<LibraryInfo>();
            foreach (var library in details.Libraries)
            {
                if (library.Natives != null && library.Natives.ContainsKey("windows"))
                    nativeLibraries.Add(library);
            }

            if (nativeLibraries.Count == 0)
            {
                progress.Report(Localization.Get("Installer_NoNatives"));
                return;
            }

            int current = 0;
            foreach (var library in nativeLibraries)
            {
                current++;

                var classifierKey = library.Natives!["windows"];
                var classifierInfo = library.Downloads?.Classifiers?.GetValueOrDefault(classifierKey);

                if (classifierInfo == null)
                {
                    progress.Report(Localization.Get("Installer_NativeMissingFormat", current, nativeLibraries.Count, library.Name, classifierKey));
                    continue;
                }

                var archivePath = Path.Combine(tempDownloadFolder, $"{library.Name.Replace(':', '_')}.jar");

                progress.Report(Localization.Get("Installer_NativeDownloadingFormat", current, nativeLibraries.Count, library.Name));
                await EnsureFileAsync(classifierInfo.Url, archivePath, classifierInfo.Sha1, progress, logEachFile: false);

                ExtractNativeArchive(archivePath, nativesFolder, library.Extract?.Exclude);
                progress.Report(Localization.Get("Installer_NativeExtractedFormat", current, nativeLibraries.Count, library.Name));
            }

            progress.Report(Localization.Get("Installer_NativesInstalled"));
        }

        private static void ExtractNativeArchive(string archivePath, string destinationFolder, List<string>? exclude)
        {
            using var archive = ZipFile.OpenRead(archivePath);

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                bool isExcluded = false;
                if (exclude != null)
                {
                    foreach (var excludedPrefix in exclude)
                    {
                        if (entry.FullName.StartsWith(excludedPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            isExcluded = true;
                            break;
                        }
                    }
                }

                if (isExcluded)
                    continue;

                var destinationPath = Path.Combine(destinationFolder, entry.Name);
                entry.ExtractToFile(destinationPath, overwrite: true);
            }
        }

        public async Task InstallAssetsAsync(VersionDetails details, IProgress<string> progress)
        {
            var assetIndexPath = Path.Combine(
                MinecraftDir, "assets", "indexes", $"{details.AssetIndex.Id}.json");

            await EnsureFileAsync(
                details.AssetIndex.Url,
                assetIndexPath,
                details.AssetIndex.Sha1,
                progress,
                logEachFile: false);

            var indexJson = await File.ReadAllTextAsync(assetIndexPath);
            var assetIndex = System.Text.Json.JsonSerializer.Deserialize<AssetIndex>(indexJson);

            if (assetIndex == null)
            {
                progress.Report(Localization.Get("Installer_AssetIndexError"));
                return;
            }

            var objectsRoot = Path.Combine(MinecraftDir, "assets", "objects");
            int total = assetIndex.Objects.Count;
            int current = 0;
            int downloaded = 0;

            // Раньше файлы качались по одному, а каждый уже имеющийся ещё и хешировался SHA-1 при каждой установке.
            // Теперь: 8 загрузок параллельно, а существующий файл считается целым, если совпал размер.
            var options = new ParallelOptions { MaxDegreeOfParallelism = 8 };

            await Parallel.ForEachAsync(assetIndex.Objects, options, async (entry, _) =>
            {
                var hash = entry.Value.Hash;
                if (!IsSha1Hex(hash))
                    return; // повреждённая запись индекса — не даём ей стать путём к файлу

                var hashPrefix = hash.Substring(0, 2);

                var destinationPath = Path.Combine(objectsRoot, hashPrefix, hash);
                var url = $"{ResourcesBaseUrl}/{hashPrefix}/{hash}";

                bool intact = File.Exists(destinationPath) && new FileInfo(destinationPath).Length == entry.Value.Size;

                if (!intact)
                {
                    await EnsureFileAsync(url, destinationPath, hash, progress, logEachFile: false, forceDownload: true);
                    Interlocked.Increment(ref downloaded);
                }

                int done = Interlocked.Increment(ref current);

                if (done % 200 == 0 || done == total)
                {
                    progress.Report(Localization.Get("Installer_AssetsProgressFormat", done, total, Volatile.Read(ref downloaded)));
                }
            });

            progress.Report(Localization.Get("Installer_AssetsInstalled"));

            if (assetIndex.Virtual || assetIndex.MapToResources)
            {
                progress.Report(Localization.Get("Installer_LegacyResourcesPreparing"));

                var virtualRoot = Path.Combine(MinecraftDir, "assets", "virtual", details.AssetIndex.Id);
                var resourcesRoot = Path.Combine(MinecraftDir, "resources");
                int copied = 0;

                foreach (var entry in assetIndex.Objects)
                {
                    var relativePath = entry.Key.Replace('/', Path.DirectorySeparatorChar);
                    var hash = entry.Value.Hash;
                    if (!IsSha1Hex(hash))
                        continue;

                    var hashPrefix = hash.Substring(0, 2);
                    var sourcePath = Path.Combine(objectsRoot, hashPrefix, hash);

                    if (!File.Exists(sourcePath))
                        continue;

                    if (assetIndex.Virtual)
                    {
                        var virtualPath = FileHelper.SafeCombine(virtualRoot, relativePath);
                        if (virtualPath != null && CopyIfMissing(sourcePath, virtualPath)) copied++;
                    }

                    if (assetIndex.MapToResources)
                    {
                        var resourcePath = FileHelper.SafeCombine(resourcesRoot, relativePath);
                        if (resourcePath != null && CopyIfMissing(sourcePath, resourcePath)) copied++;
                    }
                }

                progress.Report(Localization.Get("Installer_LegacyResourcesDoneFormat", copied));
            }
        }

        private static bool IsSha1Hex(string? hash)
        {
            if (hash == null || hash.Length != 40)
                return false;

            foreach (var c in hash)
            {
                if (!Uri.IsHexDigit(c))
                    return false;
            }

            return true;
        }

        private static bool CopyIfMissing(string sourcePath, string destinationPath)
        {
            if (File.Exists(destinationPath))
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: false);
            return true;
        }

        public async Task InstallVersionAsync(VersionEntry version, IProgress<string> progress)
        {
            progress.Report(Localization.Get("Installer_LoadingVersionFormat", version.Id));
            var rawJson = await _httpClient.GetStringAsync(version.Url);
            var details = System.Text.Json.JsonSerializer.Deserialize<VersionDetails>(rawJson);

            if (details == null)
            {
                progress.Report(Localization.Get("Installer_VersionDataError"));
                return;
            }

            var versionFolder = Path.Combine(MinecraftDir, "versions", version.Id);
            var jarPath = Path.Combine(versionFolder, $"{version.Id}.jar");
            var jsonPath = Path.Combine(versionFolder, $"{version.Id}.json");

            Directory.CreateDirectory(versionFolder);
            await File.WriteAllTextAsync(jsonPath, rawJson);

            await EnsureFileAsync(
                details.Downloads.Client.Url,
                jarPath,
                details.Downloads.Client.Sha1,
                progress);

            progress.Report(Localization.Get("Installer_ClientJarInstalled"));

            progress.Report(Localization.Get("Installer_InstallingLibraries"));
            await InstallLibrariesAsync(details, progress);
            progress.Report(Localization.Get("Installer_LibrariesInstalled"));

            progress.Report(Localization.Get("Installer_InstallingNatives"));
            await InstallNativesAsync(version, details, progress);

            progress.Report(Localization.Get("Installer_InstallingAssets"));
            await InstallAssetsAsync(details, progress);
        }

        public async Task<VersionDetails?> LoadLocalVersionDetailsAsync(string versionId)
        {
            var jsonPath = Path.Combine(MinecraftDir, "versions", versionId, $"{versionId}.json");
            if (!File.Exists(jsonPath))
                return null;

            var json = await File.ReadAllTextAsync(jsonPath);
            return System.Text.Json.JsonSerializer.Deserialize<VersionDetails>(json);
        }

        // Если версия наследуется от базовой (OptiFine/Forge) — убеждаемся, что базовая
        // (client.jar, библиотеки, ассеты, natives) тоже установлена
        public async Task EnsureCustomVersionBaseInstalledAsync(string customVersionId, IProgress<string> progress)
        {
            var details = await LoadLocalVersionDetailsAsync(customVersionId);

            if (details == null)
                return;

            // Библиотеки самой кастомной версии (например launchwrapper у OptiFine) — раньше
            // этот метод их вообще не трогал, ставя только базовую ванильную версию, и такие
            // библиотеки просто никогда не оказывались на диске.
            if (details.Libraries.Count > 0)
            {
                progress.Report(Localization.Get("Installer_InstallingLibraries"));
                await InstallLibrariesAsync(details, progress);
                progress.Report(Localization.Get("Installer_LibrariesInstalled"));
            }

            if (details.InheritsFrom == null)
                return;

            var baseId = details.InheritsFrom;
            var baseJarPath = Path.Combine(MinecraftDir, "versions", baseId, $"{baseId}.jar");

            if (File.Exists(baseJarPath))
            {
                progress.Report(Localization.Get("Installer_BaseAlreadyInstalledFormat", baseId));
                return;
            }

            progress.Report(Localization.Get("Installer_BaseDependencyFormat", customVersionId, baseId));

            var manifestService = new VersionManifestService();
            var manifest = await manifestService.GetManifestAsync();
            var baseEntry = manifest?.Versions.Find(v => v.Id == baseId);

            if (baseEntry == null)
            {
                progress.Report(Localization.Get("Installer_BaseNotFoundFormat", baseId));
                return;
            }

            await InstallVersionAsync(baseEntry, progress);
        }
    }
}