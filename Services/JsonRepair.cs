using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoffeeMC.App.Services
{
    // Самолечение конфигов лаунчера: повреждённый или неверно заполненный json не должен ронять запуск.
    // Порядок: обычное чтение -> по-свойственное спасение годных частей -> резервная копия .bak -> значения по умолчанию.
    public static class JsonRepair
    {
        public static readonly JsonSerializerOptions Tolerant = new()
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public static bool WasRepaired { get; private set; }

        public static T? LoadObject<T>(string path) where T : class
        {
            WasRepaired = false;

            if (!File.Exists(path))
                return null;

            string json;
            try { json = File.ReadAllText(path); }
            catch { return null; }

            var value = TryParse<T>(json);
            if (value != null)
                return value;

            // Файл не читается как есть — сохраняем копию для разбора и пробуем спасти что можно
            BackupBroken(path);
            WasRepaired = true;

            value = SalvageObject<T>(json);
            if (value != null)
                return value;

            return LoadBackup<T>(path);
        }

        public static List<T>? LoadList<T>(string path) where T : class
        {
            WasRepaired = false;

            if (!File.Exists(path))
                return null;

            string json;
            try { json = File.ReadAllText(path); }
            catch { return null; }

            var value = TryParse<List<T>>(json);
            if (value != null)
                return value;

            BackupBroken(path);
            WasRepaired = true;

            value = SalvageList<T>(json);
            if (value != null && value.Count > 0)
                return value;

            return LoadBackup<List<T>>(path) ?? value;
        }

        private static T? TryParse<T>(string json) where T : class
        {
            try { return JsonSerializer.Deserialize<T>(json, Tolerant); }
            catch { return null; }
        }

        // Берём только те верхние свойства, которые читаются без ошибок (например, "maxMemoryMb": "много" пропускается)
        private static T? SalvageObject<T>(string json) where T : class
        {
            try
            {
                if (JsonNode.Parse(json, null, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) is not JsonObject root)
                    return null;

                var good = new JsonObject();

                foreach (var (key, node) in root)
                {
                    var probe = new JsonObject { [key] = node?.DeepClone() };

                    if (node is JsonArray array)
                    {
                        // Массив (например, аккаунты): оставляем только годные элементы
                        var goodItems = new JsonArray();
                        foreach (var item in array)
                        {
                            var itemProbe = new JsonObject { [key] = new JsonArray(item?.DeepClone()) };
                            if (TryParse<T>(itemProbe.ToJsonString()) != null)
                                goodItems.Add(item?.DeepClone());
                        }

                        good[key] = goodItems;
                        continue;
                    }

                    if (TryParse<T>(probe.ToJsonString()) != null)
                        good[key] = node?.DeepClone();
                }

                return TryParse<T>(good.ToJsonString());
            }
            catch
            {
                return null;
            }
        }

        private static List<T>? SalvageList<T>(string json) where T : class
        {
            try
            {
                if (JsonNode.Parse(json, null, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) is not JsonArray array)
                    return null;

                var result = new List<T>();
                foreach (var item in array)
                {
                    var parsed = item == null ? null : TryParse<T>(item.ToJsonString());
                    if (parsed != null)
                        result.Add(parsed);
                }

                return result;
            }
            catch
            {
                return null;
            }
        }

        private static T? LoadBackup<T>(string path) where T : class
        {
            var bak = path + ".bak";
            if (!File.Exists(bak))
                return null;

            try { return TryParse<T>(File.ReadAllText(bak)); }
            catch { return null; }
        }

        private static void BackupBroken(string path)
        {
            try { File.Copy(path, $"{path}.broken-{DateTime.Now:yyyyMMdd-HHmmss}", true); }
            catch { }
        }

        // Перед записью сохраняем прошлую рабочую версию как .bak
        public static void Write(string path, string json)
        {
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > 2)
                    File.Copy(path, path + ".bak", true);
            }
            catch { }

            FileHelper.WriteAllTextAtomic(path, json);
        }
    }
}
