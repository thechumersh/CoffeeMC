using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoffeeMC.App.Models;

namespace CoffeeMC.App.Services
{
    public class GameLaunchService
    {
        public async Task<Process?> LaunchAsync(GameProfile profile, AccountEntry account, AppSettings settings, IProgress<string> progress)
        {
            var versionFolder = Path.Combine(GameInstallerService.MinecraftDir, "versions", profile.VersionId);
            var jsonPath = Path.Combine(versionFolder, $"{profile.VersionId}.json");
            var jarPath = Path.Combine(versionFolder, $"{profile.VersionId}.jar");
            var nativesDir = Path.Combine(versionFolder, "natives");

            if (!File.Exists(jsonPath))
            {
                progress.Report(Localization.Get("Launch_FilesNotFound"));
                return null;
            }

            progress.Report(Localization.Get("Launch_ReadingManifest"));
            var json = await File.ReadAllTextAsync(jsonPath);
            VersionDetails? details = null;
            try { details = JsonSerializer.Deserialize<VersionDetails>(json); }
            catch (JsonException) { }

            if (details == null)
            {
                progress.Report(Localization.Get("Launch_ManifestParseError"));
                return null;
            }

			// Модифицированные версии (Forge/OptiFine/Fabric) часто наследуют часть данных
			// (а иногда даже сам .jar и распакованные natives) от базовой ванильной версии
			// через "inheritsFrom" — подмешиваем их ДО того, как решаем, что версия не установлена.
			string effectiveJarPath = jarPath;
			string effectiveNativesDir = nativesDir;

			if (!string.IsNullOrEmpty(details.InheritsFrom))
			{
				var baseId = details.InheritsFrom;
				var baseFolder = Path.Combine(GameInstallerService.MinecraftDir, "versions", baseId);
				var baseJsonPath = Path.Combine(baseFolder, $"{baseId}.json");
				var baseJarPath = Path.Combine(baseFolder, $"{baseId}.jar");
				var baseNativesDir = Path.Combine(baseFolder, "natives");

				if (File.Exists(baseJsonPath))
				{
					var baseJson = await File.ReadAllTextAsync(baseJsonPath);
					VersionDetails? baseDetails = null;
						try { baseDetails = JsonSerializer.Deserialize<VersionDetails>(baseJson); }
						catch (JsonException) { }

					if (baseDetails != null)
					{
						details = MergeWithBase(details, baseDetails);
					}
				}

				if (!File.Exists(jarPath) && File.Exists(baseJarPath))
				{
					effectiveJarPath = baseJarPath;
				}

				if (!Directory.Exists(nativesDir) && Directory.Exists(baseNativesDir))
				{
					effectiveNativesDir = baseNativesDir;
				}
			}

            if (!File.Exists(effectiveJarPath))
            {
                progress.Report(Localization.Get("Launch_FilesNotFound"));
                return null;
            }

            progress.Report(Localization.Get("Launch_BuildingClasspath"));
            var classpath = BuildClasspath(details, effectiveJarPath, progress);

            bool isMicrosoft = account.Type == "Microsoft" && !string.IsNullOrEmpty(account.McAccessToken);
            var uuidString = isMicrosoft && Guid.TryParse(account.Uuid, out var realUuid)
                ? realUuid.ToString("N")
                : AccountService.GetOfflineUuid(account.Username).ToString("N");
            var accessToken = isMicrosoft ? account.McAccessToken : "0";
            var userType = isMicrosoft ? "msa" : "legacy";

            var assetsRoot = Path.Combine(GameInstallerService.MinecraftDir, "assets");
            var virtualAssetsPath = Path.Combine(assetsRoot, "virtual", details.AssetIndex.Id);
            // Старые версии (до 1.6) ждут в ${game_assets} путь к "виртуальной" папке с обычными именами файлов,
            // а не общий assets_root — если она была создана при установке, используем именно её
            var gameAssetsPath = Directory.Exists(virtualAssetsPath) ? virtualAssetsPath : assetsRoot;

            // Копируем уже распакованные natives (GameInstallerService их сохраняет один раз в папке
            // версии) в отдельную временную папку на каждый запуск — так два одновременных инстанса
            // одной и той же версии не делят один и тот же .dll/.so и не ловят UnsatisfiedLinkError
            // из-за файловых блокировок друг друга.
            var sessionNativesDir = PrepareSessionNativesDir(effectiveNativesDir);

            var placeholders = new Dictionary<string, string>
            {
                ["auth_player_name"] = account.Username,
                ["version_name"] = profile.VersionId,
                ["game_directory"] = GameInstallerService.MinecraftDir,
                ["assets_root"] = assetsRoot,
                ["game_assets"] = gameAssetsPath,
                ["assets_index_name"] = details.AssetIndex.Id,
                ["auth_uuid"] = uuidString,
                ["auth_access_token"] = accessToken,
                ["user_properties"] = "{}",
                ["user_type"] = userType,
                ["version_type"] = profile.VersionType,
                ["auth_xuid"] = "0",
                ["clientid"] = "0",
                ["natives_directory"] = sessionNativesDir,
                ["launcher_name"] = "CoffeeMC",
                ["launcher_version"] = "0.2.1",
                ["classpath"] = classpath,
                ["resolution_width"] = "854",
                ["resolution_height"] = "480"
            };

            // Приоритет: Java профиля -> Java из настроек -> автоподбор (с возможной загрузкой Temurin)
            string? chosenJava = null;

            if (!string.IsNullOrEmpty(profile.JavaPath))
            {
                if (File.Exists(profile.JavaPath))
                    chosenJava = profile.JavaPath;
                else
                    progress.Report(Localization.Get("Java_ProfileJavaMissingFormat", profile.JavaPath));
            }

            if (chosenJava == null && !string.IsNullOrEmpty(settings.JavaPath) && File.Exists(settings.JavaPath))
            {
                // Пользователь сам указал Java в Настройках → Игра
                chosenJava = settings.JavaPath;
            }

            if (chosenJava == null)
            {
                var requiredMajor = JavaLocatorService.GetRequiredMajorVersion(details, profile.VersionId);
                var customPaths = settings.CustomJavaPaths.ToList();
                // Поиск Java ходит по диску (и иногда запускает java -version) — не блокируем интерфейс
                var autoDetected = await Task.Run(() => JavaLocatorService.FindCompatibleExecutable(requiredMajor, customPaths));

                if (autoDetected == null && settings.AutoDownloadJava)
                {
                    progress.Report(Localization.Get("Java_AutoDownloadingFormat", requiredMajor));

                    try
                    {
                        var downloaded = await JavaDownloadService.DownloadAsync(requiredMajor, progress, null, CancellationToken.None);
                        autoDetected = downloaded.Executable;
                    }
                    catch (Exception ex)
                    {
                        progress.Report(Localization.Get("Java_AutoDownloadFailedFormat", requiredMajor, ex.Message));
                    }
                }

                // Не удалось найти или скачать идеальную — берём ближайшую из имеющихся
                autoDetected ??= await Task.Run(() => JavaLocatorService.FindJavaExecutable(requiredMajor, customPaths));

                if (autoDetected != null)
                {
                    chosenJava = autoDetected;
                    progress.Report(Localization.Get("Launch_AutoJavaFormat", requiredMajor, autoDetected));
                }
                else
                {
                    progress.Report(Localization.Get("Launch_AutoJavaNotFoundFormat", requiredMajor));
                }
            }

            var javaExecutable = chosenJava ?? "javaw";

            var arguments = new List<string>
            {
                $"-Xms{settings.MinMemoryMb}M",
                $"-Xmx{settings.MaxMemoryMb}M",
                $"-Djava.library.path={sessionNativesDir}"
            };

            arguments.AddRange(ExtractPlainStringArgs(details.Arguments?.Jvm, placeholders));

            // Свои JVM-аргументы профиля идут после стандартных, поэтому могут их переопределять (например -Xmx)
            arguments.AddRange(SplitArguments(profile.JvmArguments).Select(a => ReplacePlaceholders(a, placeholders)));

            arguments.Add("-cp");
            arguments.Add(classpath);
            arguments.Add(details.MainClass);

            List<string> gameArgs;
            if (details.Arguments?.Game != null && details.Arguments.Game.Count > 0)
            {
                gameArgs = ExtractPlainStringArgs(details.Arguments.Game, placeholders);
            }
            else if (!string.IsNullOrEmpty(details.MinecraftArguments))
            {
                gameArgs = BuildLegacyGameArgs(details.MinecraftArguments, placeholders);
            }
            else
            {
                progress.Report(Localization.Get("Launch_NoGameArgs"));
                gameArgs = new List<string>();
            }

            arguments.AddRange(gameArgs);
            arguments.AddRange(SplitArguments(profile.GameArguments).Select(a => ReplacePlaceholders(a, placeholders)));

            var startInfo = new ProcessStartInfo
            {
                FileName = javaExecutable,
                WorkingDirectory = GameInstallerService.MinecraftDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var arg in arguments)
                startInfo.ArgumentList.Add(arg);

            progress.Report(Localization.Get("Launch_StartingFormat", javaExecutable, profile.Name, account.Username));

            Process process;
            try
            {
                process = Process.Start(startInfo)!;
                process.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                progress.Report(Localization.Get("Launch_JavaStartErrorFormat", ex.Message));
                return null;
            }

            // Токен доступа не должен попадать в журнал (его потом копируют и отправляют при просьбе о помощи)
            string Redact(string line) =>
                isMicrosoft && accessToken.Length > 8 ? line.Replace(accessToken, "***") : line;

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) progress.Report(Redact(e.Data));
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) progress.Report(Redact(e.Data));
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Временная копия natives нужна только на время игры — удаляем после выхода (раньше папки копились навсегда)
            if (!string.Equals(sessionNativesDir, effectiveNativesDir, StringComparison.OrdinalIgnoreCase))
            {
                process.Exited += (_, _) =>
                {
                    try { Directory.Delete(sessionNativesDir, true); }
                    catch { /* файл ещё занят — подчистим при следующем запуске */ }
                };
            }

            progress.Report(Localization.Get("Launch_StartedFormat", process.Id));

            return process;
        }

        // Делит строку аргументов по пробелам и переводам строк; "в кавычках" остаётся одним аргументом
        private static List<string> SplitArguments(string? text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return result;

            var current = new System.Text.StringBuilder();
            bool inQuotes = false;
            bool hasToken = false;

            foreach (var ch in text)
            {
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    hasToken = true;
                    continue;
                }

                if (char.IsWhiteSpace(ch) && !inQuotes)
                {
                    if (hasToken)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                        hasToken = false;
                    }
                    continue;
                }

                current.Append(ch);
                hasToken = true;
            }

            if (hasToken)
                result.Add(current.ToString());

            return result;
        }

        private static string PrepareSessionNativesDir(string sourceNativesDir)
        {
            var nativesRoot = Path.Combine(Path.GetTempPath(), "CoffeeMC_natives");
            var sessionDir = Path.Combine(nativesRoot, Guid.NewGuid().ToString("N"));

            CleanupStaleNatives(nativesRoot);

            try
            {
                Directory.CreateDirectory(sessionDir);

                if (Directory.Exists(sourceNativesDir))
                {
                    foreach (var file in Directory.GetFiles(sourceNativesDir))
                    {
                        try
                        {
                            File.Copy(file, Path.Combine(sessionDir, Path.GetFileName(file)), overwrite: true);
                        }
                        catch
                        {
                            // Один файл не скопировался — не критично, пробуем остальные
                        }
                    }
                }
            }
            catch
            {
                // Не удалось создать временную папку (права доступа и т.п.) — играем прямо
                // из общей папки версии, как раньше, это по-прежнему рабочий вариант для одного инстанса
                return sourceNativesDir;
            }

            return sessionDir;
        }

        // Удаляет копии natives от прошлых запусков (игры, которые крашнулись или были убиты, не успели их стереть)
        private static void CleanupStaleNatives(string nativesRoot)
        {
            try
            {
                if (!Directory.Exists(nativesRoot))
                    return;

                foreach (var dir in Directory.GetDirectories(nativesRoot))
                {
                    if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir) < TimeSpan.FromHours(1))
                        continue; // свежая — возможно, ею пользуется запущенная сейчас игра

                    try { Directory.Delete(dir, true); }
                    catch { /* занята — пропускаем */ }
                }
            }
            catch
            {
                // уборка не критична
            }
        }

        private static string BuildClasspath(VersionDetails details, string jarPath, IProgress<string> progress)
        {
            var librariesRoot = Path.Combine(GameInstallerService.MinecraftDir, "libraries");
            var paths = new List<string>();

            foreach (var library in details.Libraries)
            {
                if (!GameInstallerService.IsLibraryAllowedOnWindows(library))
                    continue;

                if (library.Natives != null)
                    continue; // natives распаковываются отдельно и не входят в classpath как jar

                var artifact = library.Downloads?.Artifact;
                string? relativePath = artifact?.Path;
                bool viaMaven = false;

                if (string.IsNullOrEmpty(relativePath))
                {
                    // У OptiFine/Forge/LaunchWrapper в json часто нет блока "downloads" — только
                    // maven-имя "группа:артефакт:версия". Путь тогда строится по стандарту maven;
                    // без этого net.minecraft.launchwrapper.Launch и подобные не попадают в classpath.
                    relativePath = MavenNameToPath(library.Name);
                    viaMaven = true;
                }

                if (string.IsNullOrEmpty(relativePath))
                    continue;

                var fullPath = Path.Combine(librariesRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(fullPath) && viaMaven)
                {
                    // Путь по стандарту maven не совпал с тем, как файл реально разложил установщик
                    // мода — как запасной вариант ищем файл по имени артефакта где угодно в libraries
                    var found = FindLibraryByArtifactId(librariesRoot, library.Name);
                    if (found != null)
                        fullPath = found;
                }

                if (!File.Exists(fullPath))
                {
                    progress.Report(Localization.Get("Launch_LibraryMissingFormat", library.Name, fullPath));
                    continue;
                }

                if (!paths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                    paths.Add(fullPath);
            }

            paths.Add(jarPath);

            return string.Join(";", paths);
        }

        private static string? FindLibraryByArtifactId(string librariesRoot, string mavenName)
        {
            var parts = mavenName.Split(':');
            if (parts.Length < 2 || !Directory.Exists(librariesRoot))
                return null;

            var artifactId = parts[1];

            try
            {
                // Имя файла должно быть "artifactId-<версия>...": иначе "asm" находил бы "asm-commons" и т.п.
                var prefix = artifactId + "-";
                return Directory.EnumerateFiles(librariesRoot, prefix + "*.jar", SearchOption.AllDirectories)
                    .FirstOrDefault(f =>
                    {
                        var name = Path.GetFileName(f);
                        return name.Length > prefix.Length && char.IsDigit(name[prefix.Length]);
                    });
            }
            catch
            {
                return null;
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
		
        private static VersionDetails MergeWithBase(VersionDetails child, VersionDetails baseVersion)
        {
            var merged = new VersionDetails
            {
                Id = child.Id,
                MainClass = !string.IsNullOrEmpty(child.MainClass) ? child.MainClass : baseVersion.MainClass,
                Downloads = baseVersion.Downloads,
                AssetIndex = !string.IsNullOrEmpty(child.AssetIndex.Id) ? child.AssetIndex : baseVersion.AssetIndex,
                // Раньше терялось: без javaVersion лаунчер выбирал Java только по номеру версии
                JavaVersion = child.JavaVersion ?? baseVersion.JavaVersion,
                MinecraftArguments = !string.IsNullOrEmpty(child.MinecraftArguments) ? child.MinecraftArguments : baseVersion.MinecraftArguments,
                Arguments = MergeArguments(baseVersion.Arguments, child.Arguments),
                Libraries = new List<LibraryInfo>()
            };

            // Библиотека мода заменяет такую же из базовой версии (Forge/Fabric часто ставят свою версию ASM и т.п.),
            // иначе в classpath попадали обе версии и игра падала с конфликтом классов
            var childKeys = new HashSet<string>(child.Libraries.Select(LibraryKey), StringComparer.OrdinalIgnoreCase);
            merged.Libraries.AddRange(baseVersion.Libraries.Where(l => !childKeys.Contains(LibraryKey(l))));
            merged.Libraries.AddRange(child.Libraries);

            return merged;
        }

        // group:artifact[:classifier] без версии
        private static string LibraryKey(LibraryInfo library)
        {
            var parts = library.Name.Split(':');
            if (parts.Length < 2)
                return library.Name;

            var classifier = parts.Length > 3 ? parts[3] : string.Empty;
            return $"{parts[0]}:{parts[1]}:{classifier}";
        }

        // Аргументы мода ДОБАВЛЯЮТСЯ к аргументам базовой версии, а не заменяют их:
        // иначе у Fabric/Forge 1.13+ пропадали --username, --version, --assetsDir и т.д.
        private static ArgumentsSection? MergeArguments(ArgumentsSection? baseArgs, ArgumentsSection? childArgs)
        {
            if (childArgs == null)
                return baseArgs;
            if (baseArgs == null)
                return childArgs;

            return new ArgumentsSection
            {
                Jvm = Concat(baseArgs.Jvm, childArgs.Jvm),
                Game = Concat(baseArgs.Game, childArgs.Game)
            };
        }

        private static List<JsonElement>? Concat(List<JsonElement>? first, List<JsonElement>? second)
        {
            if (first == null) return second;
            if (second == null) return first;

            var result = new List<JsonElement>(first);
            result.AddRange(second);
            return result;
        }

        private static List<string> ExtractPlainStringArgs(List<JsonElement>? elements, Dictionary<string, string> placeholders)
        {
            var result = new List<string>();
            if (elements == null)
                return result;

            foreach (var element in elements)
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    result.Add(ReplacePlaceholders(element.GetString()!, placeholders));
                }
            }

            return result;
        }

        private static List<string> BuildLegacyGameArgs(string template, Dictionary<string, string> placeholders)
        {
            var tokens = template.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var result = new List<string>();

            foreach (var token in tokens)
            {
                result.Add(ReplacePlaceholders(token, placeholders));
            }

            return result;
        }

        // Один проход: значение подстановки (например ник) не подставляется повторно, так что ник
        // вида "${auth_access_token}" не сможет вытащить токен в чужой аргумент
        private static readonly System.Text.RegularExpressions.Regex PlaceholderRegex =
            new(@"\$\{([A-Za-z0-9_]+)\}", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string ReplacePlaceholders(string input, Dictionary<string, string> placeholders)
        {
            return PlaceholderRegex.Replace(input, m =>
                placeholders.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);
        }
    }
}