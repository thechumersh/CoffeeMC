namespace CoffeeMC.App.Models
{
    // Обёртка вокруг AccountEntry для отображения в списке —
    // добавляет флаг "это активный аккаунт", которого нет в самих данных
    public class AccountDisplayItem
    {
        public AccountEntry Account { get; set; } = new();
        public bool IsActive { get; set; }

        public bool IsMicrosoft => Account.IsMicrosoft;

        public string PinIcon => string.IsNullOrEmpty(Account.PinHash) ? "🔓" : "🔒";

        public string DisplayName
        {
            get
            {
                var label = Account.Username; // тип аккаунта показывает значок MS рядом
                return IsActive
                    ? $"{label} {CoffeeMC.App.Services.Localization.Get("General_ActiveAccountSuffix")}"
                    : label;
            }
        }
    }
}