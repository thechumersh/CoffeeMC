using Avalonia.Media;
using Avalonia.Media.Immutable;
using CoffeeMC.App.Models;

namespace CoffeeMC.App.Services
{
    public static class LogColorHelper
    {
        // Ловит и наш формат "[ERROR] текст", и формат самого Minecraft "[12:34:56] [Client thread/INFO]: текст"
        private static readonly System.Text.RegularExpressions.Regex LevelRegex = new(
            @"[\[/](ERROR|WARN|INFO|DEBUG|FATAL)\]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        // Кисти создаются один раз: раньше на каждую строку журнала создавалась новая кисть
        private static readonly IBrush ErrorBrush = new ImmutableSolidColorBrush(Color.Parse("#E05050"));
        private static readonly IBrush WarnBrush = new ImmutableSolidColorBrush(Color.Parse("#E8B84C"));
        private static readonly IBrush DebugBrush = new ImmutableSolidColorBrush(Color.Parse("#8A8A8A"));
        private static readonly IBrush FatalBrush = new ImmutableSolidColorBrush(Color.Parse("#B03030"));

        public static LogDisplayLine BuildDisplayLine(string line, IBrush defaultBrush, bool colorize, bool wrap)
        {
            var wrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;

            if (!colorize)
                return new LogDisplayLine { Text = line, Foreground = defaultBrush, Wrapping = wrapping };

            var match = LevelRegex.Match(line);

            if (match.Success)
            {
                return match.Groups[1].Value.ToUpperInvariant() switch
                {
                    "ERROR" => Colored(line, ErrorBrush, wrapping),
                    "WARN" => Colored(line, WarnBrush, wrapping),
                    "DEBUG" => Colored(line, DebugBrush, wrapping),
                    "FATAL" => Colored(line, FatalBrush, wrapping, bold: true),
                    _ => new LogDisplayLine { Text = line, Foreground = defaultBrush, Wrapping = wrapping }
                };
            }

            // Стектрейсы и OutOfMemory часто вообще не помечены уровнем — подсвечиваем отдельно,
            // чтобы такие строки точно не терялись среди обычного текста
            if (line.Contains("Exception", System.StringComparison.OrdinalIgnoreCase) ||
                line.Contains("OutOfMemoryError", System.StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Crash Report", System.StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("\tat ", System.StringComparison.Ordinal))
            {
                return Colored(line, ErrorBrush, wrapping);
            }

            return new LogDisplayLine { Text = line, Foreground = defaultBrush, Wrapping = wrapping };
        }

        private static LogDisplayLine Colored(string line, IBrush brush, TextWrapping wrapping, bool bold = false)
        {
            return new LogDisplayLine
            {
                Text = line,
                Foreground = brush,
                Weight = bold ? FontWeight.Bold : FontWeight.Normal,
                Wrapping = wrapping
            };
        }
    }
}