namespace CoffeeMC.App.Models
{
    // То, что возвращает окно создания профиля при закрытии:
    // либо готовый профиль + флаг "нужно ли сразу качать", либо null (нажали Отмена)
    public class ProfileCreationResult
    {
        public GameProfile Profile { get; set; } = new();
        public bool InstallImmediately { get; set; }
    }
}