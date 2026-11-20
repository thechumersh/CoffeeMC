using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CoffeeMC.App.Services;

namespace CoffeeMC.App.Views
{
    // Праздничные темы и пасхалки — вынесено отдельно, чтобы не раздувать основной файл окна
    public partial class MainWindow
    {
        private static readonly Key[] KonamiCode =
        {
            Key.Up, Key.Up, Key.Down, Key.Down, Key.Left, Key.Right, Key.Left, Key.Right, Key.B, Key.A
        };

        private static readonly string[] RareQuotes =
        {
            "Не копай вниз. Серьёзно.",
            "Кофе остывает ☕",
            "Осторожно: лава",
            "Алмазы не растут на деревьях. Пока."
        };

        private static readonly string[] SecretQuotes =
        {
            "☕ Кофе готов. Можно играть!",
            "Ты нашёл секрет. Никому не говори.",
            "Тень за спиной? Это просто тень.",
            "Совет дня: всегда носи с собой ведро воды."
        };

        private int _konamiProgress;
        private int _titleClicks;
        private DateTime _lastTitleClick = DateTime.MinValue;
        private DispatcherTimer? _rainbowTimer;
        private string _activeThemeKey = "CoffeeDark";

        // Тема, которая реально должна быть включена сейчас: праздничная (если включено и сезон идёт) или выбранная пользователем
        private string GetEffectiveThemeKey()
        {
            if (_settings.AutoSeasonalTheme)
            {
                var seasonal = SeasonalService.ThemeKey(SeasonalService.Current(DateTime.Now));
                if (seasonal != null)
                    return seasonal;
            }

            return _settings.Theme;
        }

        private void InitExtras()
        {
            Closing += (_, _) => FlushSettingsSave();

            var extra = new List<string>();

            switch (SeasonalService.Current(DateTime.Now))
            {
                case Season.Halloween:
                    extra.Add(Localization.Get("Home_SeasonHalloween"));
                    break;
                case Season.NewYear:
                    extra.Add(Localization.Get("Home_SeasonNewYear"));
                    break;
            }

            extra.AddRange(RareQuotes);
            HomeQuotes = HomeQuotes.Concat(extra).ToArray();

            AutoSeasonalCheck.IsChecked = _settings.AutoSeasonalTheme;
            KeyDown += OnExtrasKeyDown;
        }

        private void OnAutoSeasonalClick(object? sender, RoutedEventArgs e)
        {
            _settings.AutoSeasonalTheme = AutoSeasonalCheck.IsChecked == true;
            _settingsService.Save(_settings);

            ApplyTheme(GetEffectiveThemeKey());
            BuildThemeSwatches();
            RebuildLogView();
        }

        // Семь быстрых кликов по названию на главном экране
        private void OnHomeTitlePressed(object? sender, PointerPressedEventArgs e)
        {
            var now = DateTime.UtcNow;
            _titleClicks = (now - _lastTitleClick).TotalMilliseconds < 600 ? _titleClicks + 1 : 1;
            _lastTitleClick = now;

            if (_titleClicks < 7)
                return;

            _titleClicks = 0;
            HomeQuoteText.Text = SecretQuotes[Random.Shared.Next(SecretQuotes.Length)];
        }

        // Классический код: вверх, вверх, вниз, вниз, влево, вправо, влево, вправо, B, A
        private void OnExtrasKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == KonamiCode[_konamiProgress])
            {
                _konamiProgress++;

                if (_konamiProgress == KonamiCode.Length)
                {
                    _konamiProgress = 0;
                    StartRainbow();
                }
            }
            else
            {
                _konamiProgress = e.Key == KonamiCode[0] ? 1 : 0;
            }
        }

        // Несколько секунд акцентный цвет переливается всеми цветами, потом возвращается прежний (в настройки ничего не пишется)
        private void StartRainbow()
        {
            _rainbowTimer?.Stop();

            int tick = 0;
            HomeQuoteText.Text = "🌈";

            _rainbowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
            _rainbowTimer.Tick += (_, _) =>
            {
                tick++;

                if (tick > 110)
                {
                    _rainbowTimer?.Stop();
                    ApplyAccentColor(_settings.AccentColor);
                    return;
                }

                ApplyAccentColor(HueToHex(tick * 9 % 360));
            };
            _rainbowTimer.Start();
        }

        private static string HueToHex(double hue)
        {
            double c = 0.85;
            double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));

            (double r, double g, double b) = hue switch
            {
                < 60 => (c, x, 0.0),
                < 120 => (x, c, 0.0),
                < 180 => (0.0, c, x),
                < 240 => (0.0, x, c),
                < 300 => (x, 0.0, c),
                _ => (c, 0.0, x)
            };

            double m = 0.15;
            return $"#{(int)((r + m) * 255):X2}{(int)((g + m) * 255):X2}{(int)((b + m) * 255):X2}";
        }
    
        // ---------- Праздничное оформление: иконки меню и фоновые элементы ----------

        private sealed class DecorItem
        {
            public TextBlock Block = null!;
            public double X, Y, Speed, Phase, Size;
            public int Kind; // 0 — падает, 1 — плавает на месте, 2 — летит вбок, 3 — паук на нити, 4 — угол
            public double AnchorX, AnchorY; // относительные (0..1) для плавающих и угловых
        }

        private readonly List<DecorItem> _decorItems = new();
        private DispatcherTimer? _decorTimer;
        private Border? _spiderThread;
        private readonly Random _rng = new();
        private double _decorTime;

        private static readonly Dictionary<string, string[]> NavEmoji = new()
        {
            ["Halloween"] = new[] { "🎃", "🦇", "📜", "🕸" },
            ["NewYear"] = new[] { "🎄", "⛄", "🔔", "❄" }
        };

        private void ApplyDecor(string themeKey)
        {
            if (DecorCanvas == null)
                return;

            _decorTimer?.Stop();
            _decorTimer = null;
            DecorCanvas.Children.Clear();
            _decorItems.Clear();
            _spiderThread = null;

            var navPaths = new Control[] { NavHomeIcon, NavProfilesIcon, NavConsoleIcon, NavSettingsIcon };
            var navEmoji = new[] { NavHomeEmoji, NavProfilesEmoji, NavConsoleEmoji, NavSettingsEmoji };
            var hasEmoji = NavEmoji.TryGetValue(themeKey, out var icons);

            for (int i = 0; i < 4; i++)
            {
                navPaths[i].IsVisible = !hasEmoji;
                navEmoji[i].IsVisible = hasEmoji;
                if (hasEmoji)
                    navEmoji[i].Text = icons![i];
            }

            if (themeKey == "Halloween")
            {
                AddDecor("🕸", 4, 0.0, 0.0, 70, 0.55);
                AddDecor("🕸", 4, 1.0, 0.0, 54, 0.4);
                AddDecor("🎃", 1, 0.04, 0.88, 44, 0.8);
                AddDecor("👻", 1, 0.93, 0.80, 40, 0.5);
                AddDecor("🦇", 2, 0.0, 0.18, 26, 0.55);
                AddDecor("🦇", 2, 0.0, 0.62, 20, 0.45);
                AddDecor("🕯", 1, 0.90, 0.92, 30, 0.6);

                _spiderThread = new Border
                {
                    Width = 1,
                    Background = new SolidColorBrush(Color.Parse("#99FFFFFF")),
                    Opacity = 0.5
                };
                DecorCanvas.Children.Add(_spiderThread);
                AddDecor("🕷", 3, 0.82, 0.0, 22, 0.8);
            }
            else if (themeKey == "NewYear")
            {
                for (int i = 0; i < 22; i++)
                    AddDecor(i % 3 == 0 ? "❅" : "❄", 0, _rng.NextDouble(), _rng.NextDouble(), 9 + _rng.Next(0, 12), 0.25 + _rng.NextDouble() * 0.4);

                AddDecor("🎄", 1, 0.95, 0.86, 54, 0.75);
                AddDecor("🎁", 1, 0.86, 0.94, 26, 0.7);
                AddDecor("⛄", 1, 0.04, 0.88, 44, 0.75);
            }

            if (_decorItems.Count == 0)
                return;

            _decorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _decorTimer.Tick += (_, _) => TickDecor();
            _decorTimer.Start();
            TickDecor();
        }

        private void AddDecor(string glyph, int kind, double ax, double ay, double size, double opacity)
        {
            var tb = new TextBlock
            {
                Text = glyph,
                FontSize = size,
                Opacity = opacity,
                IsHitTestVisible = false
            };

            var item = new DecorItem
            {
                Block = tb,
                Kind = kind,
                AnchorX = ax,
                AnchorY = ay,
                Size = size,
                Phase = _rng.NextDouble() * Math.PI * 2,
                Speed = kind == 0 ? 0.5 + size / 14.0 : kind == 2 ? 1.2 + _rng.NextDouble() : 1.0
            };

            // для «падающих» и «летящих» якорь — стартовая позиция (в долях от размера области)
            item.X = ax;
            item.Y = ay;

            _decorItems.Add(item);
            DecorCanvas.Children.Add(tb);
        }

        private void TickDecor()
        {
            // Не анимируем, когда окно свёрнуто или не в фокусе (например, пока идёт игра) — не тратим процессор впустую
            if (WindowState == WindowState.Minimized || !IsVisible || !IsActive)
                return;

            var w = DecorCanvas.Bounds.Width;
            var h = DecorCanvas.Bounds.Height;
            if (w < 50 || h < 50)
                return;

            _decorTime += 0.05;

            foreach (var d in _decorItems)
            {
                double x, y;
                switch (d.Kind)
                {
                    case 0: // снежинка
                        d.Y += d.Speed * 0.6 / h * 1.0;
                        if (d.Y > 1.05) { d.Y = -0.05; d.X = _rng.NextDouble(); }
                        x = d.X * w + Math.Sin(_decorTime * 0.8 + d.Phase) * 14;
                        y = d.Y * h;
                        break;
                    case 1: // плавает на месте
                        x = d.AnchorX * w - d.Size / 2;
                        y = d.AnchorY * h - d.Size / 2 + Math.Sin(_decorTime * 1.2 + d.Phase) * 4;
                        break;
                    case 2: // летучая мышь
                        d.X += d.Speed * 0.0018;
                        if (d.X > 1.1) { d.X = -0.1; d.Y = 0.1 + _rng.NextDouble() * 0.6; }
                        x = d.X * w;
                        y = d.Y * h + Math.Sin(_decorTime * 3 + d.Phase) * 12;
                        break;
                    case 3: // паук на нити
                        var length = 50 + (Math.Sin(_decorTime * 0.7 + d.Phase) + 1) * 35;
                        x = d.AnchorX * w;
                        y = length;
                        if (_spiderThread != null)
                        {
                            _spiderThread.Height = length + 4;
                            Canvas.SetLeft(_spiderThread, x + d.Size / 2);
                            Canvas.SetTop(_spiderThread, 0);
                        }
                        break;
                    default: // угол
                        x = d.AnchorX * w - d.AnchorX * d.Size * 1.2;
                        y = d.AnchorY * h;
                        break;
                }

                Canvas.SetLeft(d.Block, x);
                Canvas.SetTop(d.Block, y);
            }
        }
    
        // ---------- Полоски прокрутки и всплывающие меню в цвет лаунчера ----------

        private Color _chromeText = Color.Parse("#F0DCC4");
        private Color _chromePanel = Color.Parse("#2E1815");
        private Color _chromeBorder = Color.Parse("#D9A066");
        private Color _chromeAccent = Color.Parse("#E8A33D");

        private static SolidColorBrush Tint(Color c, double opacity) =>
            new(Color.FromArgb((byte)(255 * opacity), c.R, c.G, c.B));

        private void ApplyChromeBrushes()
        {
            var r = Application.Current?.Resources;
            if (r == null)
                return;

            // Полоса прокрутки: ползунок — цвет текста, при наведении и нажатии — акцент
            r["ScrollBarThumbFill"] = Tint(_chromeText, 0.35);
            r["ScrollBarThumbFillPointerOver"] = Tint(_chromeAccent, 0.85);
            r["ScrollBarThumbFillPressed"] = Tint(_chromeAccent, 1.0);
            r["ScrollBarThumbFillDisabled"] = Tint(_chromeText, 0.15);
            r["ScrollBarPanningThumbBackground"] = Tint(_chromeAccent, 0.8);
            r["ScrollBarTrackFill"] = Brushes.Transparent;
            r["ScrollBarTrackFillPointerOver"] = Tint(_chromeText, 0.07);
            r["ScrollBarTrackStroke"] = Brushes.Transparent;
            r["ScrollBarTrackStrokePointerOver"] = Brushes.Transparent;
            r["ScrollBarButtonArrowForeground"] = Tint(_chromeText, 0.6);
            r["ScrollBarButtonArrowForegroundPointerOver"] = Tint(_chromeAccent, 1.0);
            r["ScrollBarButtonArrowForegroundPressed"] = Tint(_chromeAccent, 1.0);

            // Контекстные меню, выпадающие списки: фон панели лаунчера вместо серого
            var panel = new SolidColorBrush(_chromePanel);
            var border = Tint(_chromeBorder, 0.6);
            var text = new SolidColorBrush(_chromeText);
            var hover = Tint(_chromeAccent, 0.30);
            var pressed = Tint(_chromeAccent, 0.45);

            foreach (var key in new[]
                     {
                         "ContextMenuBackground", "MenuFlyoutPresenterBackground", "FlyoutPresenterBackground",
                         "ComboBoxDropDownBackground", "MenuBarBackground"
                     })
                r[key] = panel;

            foreach (var key in new[]
                     {
                         "ContextMenuBorderBrush", "MenuFlyoutPresenterBorderBrush", "FlyoutBorderThemeBrush",
                         "ComboBoxDropDownBorderBrush"
                     })
                r[key] = border;

            foreach (var key in new[]
                     {
                         "MenuFlyoutItemForeground", "MenuFlyoutItemForegroundPointerOver", "MenuFlyoutItemForegroundPressed",
                         "MenuItemForeground", "MenuItemForegroundPointerOver", "MenuItemForegroundPressed"
                     })
                r[key] = text;

            r["MenuFlyoutItemBackground"] = Brushes.Transparent;
            r["MenuFlyoutItemBackgroundPointerOver"] = hover;
            r["MenuFlyoutItemBackgroundPressed"] = pressed;
            r["MenuItemBackground"] = Brushes.Transparent;
            r["MenuItemBackgroundPointerOver"] = hover;
            r["MenuItemBackgroundPressed"] = pressed;
            r["MenuFlyoutSeparatorBackground"] = border;
        }

        // ---------- Отложенное сохранение (ползунки не пишут файл на каждый шаг) ----------

        private DispatcherTimer? _saveTimer;

        private void ScheduleSettingsSave()
        {
            _saveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveTimer.Stop();
            _saveTimer.Tick -= OnSaveTimerTick;
            _saveTimer.Tick += OnSaveTimerTick;
            _saveTimer.Start();
        }

        private void OnSaveTimerTick(object? sender, EventArgs e)
        {
            _saveTimer?.Stop();
            _settingsService.Save(_settings);
        }

        private void FlushSettingsSave()
        {
            if (_saveTimer is { IsEnabled: true })
            {
                _saveTimer.Stop();
                _settingsService.Save(_settings);
            }
        }
    }
}
