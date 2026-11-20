using System;
using System.Collections.Generic;

namespace CoffeeMC.App.Services
{
    public sealed class ThemeDefinition
    {
        public required string Key { get; init; }
        public required string Name { get; init; }
        public required string Background { get; init; }
        public required string Panel { get; init; }
        public required string Border { get; init; }
        public required string Text { get; init; }
        public required bool IsDark { get; init; }
    }

    public static class ThemeCatalog
    {
        public static readonly IReadOnlyList<ThemeDefinition> All = new List<ThemeDefinition>
        {
            new()
            {
                Key = "CoffeeDark", Name = "Кофейная тёмная",
                Background = "#3B211D", Panel = "#2E1815", Border = "#D9A066", Text = "#F0DCC4",
                IsDark = true
            },
            new()
            {
                Key = "CoffeeLight", Name = "Кофейная светлая",
                Background = "#F5E9DA", Panel = "#EAD9C4", Border = "#8C5A3B", Text = "#3B211D",
                IsDark = false
            },
            new()
            {
                Key = "OledBlack", Name = "OLED чёрная",
                Background = "#000000", Panel = "#0C0C0C", Border = "#2A2A2A", Text = "#E8E8E8",
                IsDark = true
            },
            new()
            {
                Key = "PlainBlack", Name = "Просто чёрная",
                Background = "#1A1A1A", Panel = "#242424", Border = "#3A3A3A", Text = "#F2F2F2",
                IsDark = true
            },
            new()
            {
                Key = "Halloween", Name = "Хэллоуин",
                Background = "#1A0F24", Panel = "#120A1A", Border = "#FF7518", Text = "#F3E5D0",
                IsDark = true
            },
            new()
            {
                Key = "NewYear", Name = "Новый год",
                Background = "#0F2233", Panel = "#0A1826", Border = "#9FD8F5", Text = "#EAF6FF",
                IsDark = true
            },
            new()
            {
                Key = "PlainWhite", Name = "Просто белая",
                Background = "#FFFFFF", Panel = "#F0F0F0", Border = "#D0D0D0", Text = "#1A1A1A",
                IsDark = false
            },
        };

        // Совместимость со старыми настройками, где тема хранилась как "Dark" / "Light"
        public static ThemeDefinition Resolve(string? key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                foreach (var theme in All)
                {
                    if (string.Equals(theme.Key, key, StringComparison.OrdinalIgnoreCase))
                        return theme;
                }

                if (string.Equals(key, "Dark", StringComparison.OrdinalIgnoreCase))
                    return All[0];

                if (string.Equals(key, "Light", StringComparison.OrdinalIgnoreCase))
                    return All[1];
            }

            return All[0];
        }
    }
}
