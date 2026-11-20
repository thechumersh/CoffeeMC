using Avalonia.Media;

namespace CoffeeMC.App.Models
{
    // Одна строка журнала, уже готовая к отображению: с выбранным цветом и жирностью
    public class LogDisplayLine
    {
        public string Text { get; set; } = string.Empty;
        public IBrush Foreground { get; set; } = Brushes.White;
        public FontWeight Weight { get; set; } = FontWeight.Normal;
        public TextWrapping Wrapping { get; set; } = TextWrapping.Wrap;
    }
}