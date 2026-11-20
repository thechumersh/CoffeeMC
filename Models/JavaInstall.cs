namespace CoffeeMC.App.Models
{
    public enum JavaSource
    {
        System,
        Managed,          // скачана самим лаунчером в %APPDATA%\CoffeeMC\java
        MinecraftRuntime, // встроенная Java официального лаунчера Minecraft
        Custom            // указана пользователем вручную
    }

    // Одна найденная установка Java: что за сборка, какая версия, откуда взялась
    public class JavaInstall
    {
        public string Executable { get; set; } = string.Empty; // javaw.exe (или java.exe, если javaw нет)
        public string HomeDir { get; set; } = string.Empty;
        public int Major { get; set; }
        public string Version { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty;
        public string Architecture { get; set; } = string.Empty;
        public string ImageType { get; set; } = string.Empty;
        public JavaSource Source { get; set; }

        public bool IsManaged => Source == JavaSource.Managed;

        private static string Unknown => CoffeeMC.App.Services.Localization.Get("Java_Unknown");

        public string SourceLabel => Source switch
        {
            JavaSource.Managed => CoffeeMC.App.Services.Localization.Get("Java_SourceManaged"),
            JavaSource.MinecraftRuntime => CoffeeMC.App.Services.Localization.Get("Java_SourceMinecraft"),
            JavaSource.Custom => CoffeeMC.App.Services.Localization.Get("Java_SourceCustom"),
            _ => CoffeeMC.App.Services.Localization.Get("Java_SourceSystem")
        };

        public string Title => $"Java {Major}  ·  {(string.IsNullOrEmpty(Vendor) ? Unknown : Vendor)}";

        public string Details =>
            $"{(string.IsNullOrEmpty(Version) ? Unknown : Version)}  ·  {(string.IsNullOrEmpty(Architecture) ? Unknown : Architecture)}  ·  {ImageType}  ·  {SourceLabel}";

        public override string ToString() => $"{Title} ({Executable})";
    }
}
