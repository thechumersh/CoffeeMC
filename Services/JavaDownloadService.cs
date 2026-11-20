using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoffeeMC.App.Models;

namespace CoffeeMC.App.Services
{
    // Скачивает Java с Eclipse Temurin (Adoptium, adoptium.net) — тот же источник, что используют Prism и другие лаунчеры.
    // Файл проверяется по контрольной сумме SHA-256 из API Adoptium, затем распаковывается в %APPDATA%\CoffeeMC\java.
    public static class JavaDownloadService
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            return NetClient.Shared;
        }

        // Какие версии Java можно скачать (LTS из API Adoptium; если сети нет — стандартный набор)
        public static async Task<List<int>> GetAvailableMajorsAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var json = await Http.GetStringAsync("https://api.adoptium.net/v3/info/available_releases", cts.Token);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("available_lts_releases", out var lts))
                {
                    var list = lts.EnumerateArray().Select(e => e.GetInt32()).Where(v => v >= 8).OrderBy(v => v).ToList();
                    if (list.Count > 0)
                        return list;
                }
            }
            catch
            {
                // нет сети или API изменился — используем запасной список
            }

            return new List<int> { 8, 11, 17, 21, 25 };
        }

        private static string GetArchitecture(int major)
        {
            var os = RuntimeInformation.OSArchitecture;

            // Windows ARM64 есть у Temurin только с 21-й версии; для старых работает x64 через эмуляцию
            if (os == Architecture.Arm64 && major >= 21)
                return "aarch64";
            if (os == Architecture.X86)
                return "x86-32";

            return "x64";
        }

        public static async Task<JavaInstall> DownloadAsync(int major, IProgress<string>? log, IProgress<double>? percent, CancellationToken ct)
        {
            var arch = GetArchitecture(major);
            string? link = null, checksum = null;
            long size = 0;

            log?.Report(Localization.Get("Java_QueryingFormat", major));

            try
            {
                var url = $"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture={arch}&image_type=jre&os=windows&vendor=eclipse";
                var json = await Http.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(json);

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (!item.TryGetProperty("binary", out var bin) || !bin.TryGetProperty("package", out var pkg))
                        continue;

                    link = pkg.TryGetProperty("link", out var l) ? l.GetString() : null;
                    checksum = pkg.TryGetProperty("checksum", out var c) ? c.GetString() : null;
                    size = pkg.TryGetProperty("size", out var s) && s.TryGetInt64(out var sv) ? sv : 0;

                    if (!string.IsNullOrEmpty(link))
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // API с описанием недоступен — ниже попробуем прямую ссылку на последнюю версию (без проверки суммы)
            }

            // Запускать будем скачанное — поэтому только проверенный источник и обязательная контрольная сумма.
            // Раньше при недоступном описании качался архив без проверки: теперь это ошибка.
            if (string.IsNullOrEmpty(link) || string.IsNullOrEmpty(checksum))
                throw new IOException(Localization.Get("Java_ChecksumUnavailable"));

            if (!NetClient.IsHostOrSubdomain(link, "adoptium.net", "github.com", "githubusercontent.com"))
                throw new IOException(Localization.Get("Java_UntrustedSource"));

            Directory.CreateDirectory(JavaDiscoveryService.ManagedRoot);
            var tempId = Guid.NewGuid().ToString("N")[..8];
            var tempZip = Path.Combine(JavaDiscoveryService.ManagedRoot, $".dl-{tempId}.zip");
            var tempDir = Path.Combine(JavaDiscoveryService.ManagedRoot, $".x-{tempId}");

            try
            {
                using (var response = await Http.GetAsync(link, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength ?? size;

                    await using var source = await response.Content.ReadAsStreamAsync(ct);
                    await using var target = File.Create(tempZip);

                    var buffer = new byte[81920];
                    long done = 0;
                    int lastPercent = -1;
                    int read;

                    while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), ct);
                        done += read;

                        if (total > 0)
                        {
                            int pct = (int)(done * 100 / total);
                            if (pct != lastPercent)
                            {
                                lastPercent = pct;
                                percent?.Report(pct);

                                if (pct % 25 == 0)
                                    log?.Report(Localization.Get("Java_DownloadingFormat", major, pct));
                            }
                        }
                    }
                }

                {
                    var actual = await ComputeSha256Async(tempZip, ct);
                    if (!string.Equals(actual, checksum, StringComparison.OrdinalIgnoreCase))
                        throw new IOException(Localization.Get("Java_ChecksumMismatch"));

                    log?.Report(Localization.Get("Java_ChecksumOk"));
                }

                log?.Report(Localization.Get("Java_Extracting"));
                await Task.Run(() => ZipFile.ExtractToDirectory(tempZip, tempDir), ct);

                var extractedHome = FindHome(tempDir)
                    ?? throw new IOException(Localization.Get("Java_ArchiveInvalid"));

                var finalDir = Path.Combine(JavaDiscoveryService.ManagedRoot, $"temurin-{major}-jre");
                if (Directory.Exists(finalDir))
                    Directory.Delete(finalDir, true); // обновление уже скачанной версии

                Directory.Move(extractedHome, finalDir);

                var install = JavaDiscoveryService.Inspect(Path.Combine(finalDir, "bin", "javaw.exe"), JavaSource.Managed)
                    ?? throw new IOException(Localization.Get("Java_ArchiveInvalid"));

                log?.Report(Localization.Get("Java_DownloadDoneFormat", major, install.Executable));
                return install;
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static string? FindHome(string root)
        {
            if (File.Exists(Path.Combine(root, "bin", "java.exe")))
                return root;

            foreach (var dir in Directory.GetDirectories(root))
            {
                var found = FindHome(dir);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
        {
            using var sha = SHA256.Create();
            await using var stream = File.OpenRead(path);
            var hash = await sha.ComputeHashAsync(stream, ct);
            return Convert.ToHexString(hash);
        }
    }
}
