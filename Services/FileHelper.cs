using System.IO;
using System.Text;

namespace CoffeeMC.App.Services
{
    public static class FileHelper
    {
        private static readonly object Gate = new();

        // Пишет через временный файл: сбой питания или падение посреди записи больше не оставляют обрезанный JSON
        public static void WriteAllTextAtomic(string path, string content)
        {
            lock (Gate)
            {
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, content, new UTF8Encoding(false));
                File.Move(tmp, path, overwrite: true);
            }
        }

        // Путь внутри корневой папки? Защита от "../" в именах из чужих json (версии модов, индексы ресурсов)
        public static string? SafeCombine(string root, string relative)
        {
            try
            {
                var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var full = Path.GetFullPath(Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar)));
                return full.StartsWith(rootFull, System.StringComparison.OrdinalIgnoreCase) ? full : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
