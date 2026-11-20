using System;

namespace CoffeeMC.App.Services
{
    public enum Season
    {
        None,
        Halloween,
        NewYear
    }

    // Определяет, не идёт ли сейчас праздничный период, для которого есть своя тема
    public static class SeasonalService
    {
        public static Season Current(DateTime now)
        {
            // Хэллоуин: 15 октября – 2 ноября
            if ((now.Month == 10 && now.Day >= 15) || (now.Month == 11 && now.Day <= 2))
                return Season.Halloween;

            // Новый год: 15 декабря – 10 января
            if ((now.Month == 12 && now.Day >= 15) || (now.Month == 1 && now.Day <= 10))
                return Season.NewYear;

            return Season.None;
        }

        public static string? ThemeKey(Season season) => season switch
        {
            Season.Halloween => "Halloween",
            Season.NewYear => "NewYear",
            _ => null
        };
    }
}
