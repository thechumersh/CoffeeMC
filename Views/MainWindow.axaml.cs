using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CoffeeMC.App.Models;
using CoffeeMC.App.Services;

namespace CoffeeMC.App.Views
{
    public partial class MainWindow : Window
    {
        private readonly VersionManifestService _versionService = new();
        private readonly ProfileService _profileService = new();
        private readonly GameInstallerService _installerService = new();
        private readonly SettingsService _settingsService = new();
        private readonly GameLaunchService _launchService = new();
		private readonly LauncherProfilesCompatService _launcherProfilesCompatService = new();

        private VersionManifest? _manifest;
        private readonly ObservableCollection<GameProfile> _profiles = new();
        private AppSettings _settings = new();

        private bool _isRefreshingHomeUi;
        private bool _isRefreshingGameSettings;
        private bool _isRefreshingAppearance;

        private readonly HashSet<string> _verifiedVersionsThisSession = new(StringComparer.OrdinalIgnoreCase);

        private long _totalMemoryMb;
        private bool _sidebarCollapsed;

        private readonly List<LogSession> _logSessions = new();
        private LogSession _launcherSession = null!;
        private LogSession? _activeLogSession;
        private Avalonia.Threading.DispatcherTimer? _logFlushTimer;
        private bool _autoScroll = true;
        private bool _wrapLines = true;
        private bool _colorLines = true;

        private const int MaxLogLinesPerSession = 4000;
        // Старые строки обрезаются пачкой, а не по одной — иначе при спаме список пересобирался бы каждые 120 мс
        private const int LogTrimBatch = 500;

        private System.Collections.ObjectModel.ObservableCollection<LogDisplayLine> _logItems = new();
        private ScrollViewer? _logScroll;
        private IBrush? _logDefaultBrush;
        private string? _logDefaultBrushTheme;

        // Одна "сессия" журнала: либо общий лог лаунчера, либо конкретный запуск игры.
        // Строки копятся в очереди и переносятся в отображаемый список пачками по таймеру,
        // чтобы шквал строк (спам в логах игры) не вешал интерфейс перерисовкой на каждую строку.
        private sealed class LogSession
        {
            public required string Id;
            public required string Name;
            public readonly List<string> Lines = new();
            public readonly Queue<string> PendingLines = new();
            public Process? Process;
            public bool IsGame;
            public string? ProfileName;
        }

        private static readonly string[] AccentPresets =
        {
            "#E8A33D", "#E05050", "#4CAF7D", "#4C8FE8", "#9B6FE0",
            "#E85D9B", "#4CC9E8", "#E8D34C", "#8C5A3B", "#B0B0B0"
        };

        private static string[] HomeQuotes = { "Welcome to CoffeeMC", "CoffeeMC by ChumerSH" };
        private int _homeQuoteIndex;
        private TranslateTransform? _homeQuoteTransform;
        private const double HomeQuoteRestY = 0;
        private const double HomeQuoteExitY = -8;
        private const double HomeQuoteEnterY = 8;
        private const double HomeQuoteRestOpacity = 0.6;

        public MainWindow()
        {
            InitializeComponent();
            ProfilesList.ItemsSource = _profiles;

            _settings = _settingsService.Load();

            // Папка игры должна быть задана ДО чтения профилей и синхронизации launcher_profiles.json
            if (!string.IsNullOrEmpty(_settings.MinecraftPath))
                GameInstallerService.MinecraftDir = _settings.MinecraftPath;

            _launcherSession = new LogSession { Id = "launcher", Name = Localization.Get("Console_LauncherSessionName"), IsGame = false };
            _logSessions.Add(_launcherSession);
            _activeLogSession = _launcherSession;

            _logFlushTimer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _logFlushTimer.Tick += (_, _) => FlushPendingLogLines();
            _logFlushTimer.Start();

            SetupHomeQuoteTransitions();
            var quoteTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            quoteTimer.Tick += (_, _) => CycleHomeQuote();
            quoteTimer.Start();

            if (_settings.Accounts.Count == 0)
            {
                var defaultAccount = new AccountEntry { Username = "Player" };
                _settings.Accounts.Add(defaultAccount);
                _settings.ActiveAccountId = defaultAccount.Id;
                _settingsService.Save(_settings);
            }

            _totalMemoryMb = SystemInfoService.GetTotalPhysicalMemoryMb();
            RefreshGameSettingsUi();

            ApplyTheme(GetEffectiveThemeKey());
            ApplyAccentColor(_settings.AccentColor);
            ApplyCornerRadius(_settings.UiCornerRadius);
            ApplyLanguage(_settings.Language);
            InitExtras();

            LogList.ItemsSource = _logItems;
            LogList.TemplateApplied += (_, e) => _logScroll = e.NameScope.Find<ScrollViewer>("PART_LogScroll");

            ReloadProfilesFromDisk();
			_launcherProfilesCompatService.Sync(_profiles.ToList());
            RefreshHomeUi();
            RebuildSessionTabs();

            this.PropertyChanged += OnWindowPropertyChanged;
            UpdateWindowChrome();

            this.Opened += (_, _) => Opacity = 1;
        }

        // ---------- Заголовок окна ----------

        private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void OnMaximizeClick(object? sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

        private void OnToggleSidebarClick(object? sender, RoutedEventArgs e)
        {
            _sidebarCollapsed = !_sidebarCollapsed;
            SidebarBorder.Width = _sidebarCollapsed ? 0 : 200;
        }

        private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == WindowStateProperty)
            {
                UpdateWindowChrome();
            }
        }

        private void UpdateWindowChrome()
        {
            bool maximized = WindowState == WindowState.Maximized;

            var edgeSize = maximized ? 0 : 6;
            ResizeGrid.RowDefinitions[0].Height = new GridLength(edgeSize);
            ResizeGrid.RowDefinitions[2].Height = new GridLength(edgeSize);
            ResizeGrid.ColumnDefinitions[0].Width = new GridLength(edgeSize);
            ResizeGrid.ColumnDefinitions[2].Width = new GridLength(edgeSize);

            MainContentBorder.CornerRadius = new CornerRadius(maximized ? 0 : 8);
        }

        // ---------- Изменение размера окна за края ----------

        private void OnResizeTopLeft(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.NorthWest, e);
        private void OnResizeTop(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.North, e);
        private void OnResizeTopRight(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.NorthEast, e);
        private void OnResizeLeft(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.West, e);
        private void OnResizeRight(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.East, e);
        private void OnResizeBottomLeft(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.SouthWest, e);
        private void OnResizeBottom(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.South, e);
        private void OnResizeBottomRight(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.SouthEast, e);

        // ---------- Навигация между разделами ----------

        private void OnNavClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string section)
                return;

            HideAllPanels();

            switch (section)
            {
                case "Profiles":
                    ReloadProfilesFromDisk();
                    ShowPanelWithFade(ProfilesPanel);
                    break;

                case "Console":
                    RebuildSessionTabs();
                    RebuildLogView();
                    ShowPanelWithFade(ConsolePanel);
                    break;

                case "Settings":
                    ShowSettingsCategory("Appearance");
                    ShowPanelWithFade(SettingsPanel);
                    break;

                default:
                    ReloadProfilesFromDisk();
                    RefreshHomeUi();
                    ShowPanelWithFade(HomePanel);
                    break;
            }
        }

        private void HideAllPanels()
        {
            HomePanel.IsVisible = false;
            ProfilesPanel.IsVisible = false;
            ConsolePanel.IsVisible = false;
            SettingsPanel.IsVisible = false;
        }

        // Показывает раздел с плавным появлением: сначала прозрачный и видимый, потом
        // (уже после отрисовки текущего кадра) анимированно проявляется до Opacity = 1
        private void ShowPanelWithFade(Control panel)
        {
            panel.Opacity = 0;
            panel.IsVisible = true;

            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => panel.Opacity = 1,
                Avalonia.Threading.DispatcherPriority.Loaded);
        }

        private void SwitchToConsole()
        {
            HideAllPanels();
            RebuildSessionTabs();
            RebuildLogView();
            ShowPanelWithFade(ConsolePanel);
        }

        // ---------- Журнал ----------

        // Не трогает UI напрямую — просто кладёт строку в очередь сессии.
        // Фактическая отрисовка происходит пачками по таймеру в FlushPendingLogLines,
        // иначе шквал строк (спам из логов игры) вешает интерфейс перерисовкой на каждую строку.
        private void AppendLogLine(string line, LogSession? session = null)
        {
            (session ?? _launcherSession).PendingLines.Enqueue(line);
        }

        private void FlushPendingLogLines()
        {
            bool activeNeedsFullRebuild = false;
            int activeAppended = 0;

            foreach (var session in _logSessions)
            {
                if (session.PendingLines.Count == 0)
                    continue;

                int before = session.Lines.Count;

                while (session.PendingLines.Count > 0)
                    session.Lines.Add(session.PendingLines.Dequeue());

                int added = session.Lines.Count - before;

                if (session.Lines.Count > MaxLogLinesPerSession + LogTrimBatch)
                {
                    session.Lines.RemoveRange(0, session.Lines.Count - MaxLogLinesPerSession);
                    if (session == _activeLogSession)
                        activeNeedsFullRebuild = true;
                }

                if (session == _activeLogSession)
                    activeAppended = added;
            }

            if (activeAppended == 0 && !activeNeedsFullRebuild)
                return;

            // "Продолжать обновление" не просто листает вниз — пока он выключен, журнал
            // полностью замирает (ничего не дорисовывается), хотя строки продолжают копиться
            // в фоне. При включении OnAutoScrollToggled сам сделает полную пересборку и досмотрит вниз.
            if (!_autoScroll)
                return;

            var hasFilter = !string.IsNullOrEmpty(LogSearchBox?.Text?.Trim());

            if (activeNeedsFullRebuild || hasFilter || activeAppended > 1000)
                RebuildLogView();
            else
                AppendVisibleLinesIncremental(activeAppended);
        }

        private IBrush GetLogDefaultBrush()
        {
            if (_logDefaultBrush == null || _logDefaultBrushTheme != _activeThemeKey)
            {
                _logDefaultBrush = new ImmutableSolidColorBrush(Color.Parse(ThemeCatalog.Resolve(_activeThemeKey).Text));
                _logDefaultBrushTheme = _activeThemeKey;
            }

            return _logDefaultBrush;
        }

        // Полная пересборка списка журнала — при переключении сессии/фильтрации/очистке/обрезке.
        // Список виртуализирован: реально рисуются только видимые строки, поэтому размер лога почти не влияет на скорость.
        private void RebuildLogView()
        {
            if (_activeLogSession == null)
                return;

            var searchText = LogSearchBox?.Text?.Trim() ?? string.Empty;
            var defaultBrush = GetLogDefaultBrush();

            IEnumerable<string> lines = string.IsNullOrEmpty(searchText)
                ? _activeLogSession.Lines
                : _activeLogSession.Lines.Where(l => l.Contains(searchText, StringComparison.OrdinalIgnoreCase));

            var list = new List<LogDisplayLine>(_activeLogSession.Lines.Count);

            foreach (var line in lines)
                list.Add(LogColorHelper.BuildDisplayLine(line, defaultBrush, _colorLines, _wrapLines));

            // Одна замена коллекции вместо тысяч уведомлений Add
            _logItems = new System.Collections.ObjectModel.ObservableCollection<LogDisplayLine>(list);
            LogList.ItemsSource = _logItems;

            ScrollLogToEndIfNeeded();
        }

        // Быстрый путь: добавляет в конец только новые строки
        private void AppendVisibleLinesIncremental(int count)
        {
            if (_activeLogSession == null || count <= 0)
                return;

            var defaultBrush = GetLogDefaultBrush();
            int startIndex = _activeLogSession.Lines.Count - count;

            for (int i = startIndex; i < _activeLogSession.Lines.Count; i++)
                _logItems.Add(LogColorHelper.BuildDisplayLine(_activeLogSession.Lines[i], defaultBrush, _colorLines, _wrapLines));

            ScrollLogToEndIfNeeded();
        }

        private void ScrollLogToEndIfNeeded()
        {
            if (!_autoScroll)
                return;

            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => _logScroll?.ScrollToEnd(),
                Avalonia.Threading.DispatcherPriority.Background);
        }

        // ---------- Журнал: сессии (лаунчер + отдельные запуски игры) ----------

        private void RebuildSessionTabs()
        {
            LogSessionTabsPanel.Children.Clear();

            foreach (var session in _logSessions)
            {
                var group = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 2,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };

                var tab = new ToggleButton
                {
                    Classes = { "settingsTab" },
                    Content = session.Name,
                    Tag = session.Id,
                    IsChecked = session == _activeLogSession
                };
                tab.Click += OnLogSessionTabClick;
                group.Children.Add(tab);

                // Вкладку лаунчера закрыть нельзя — она общая и всегда должна быть под рукой
                if (session != _launcherSession)
                {
                    var closeButton = new Button
                    {
                        Content = "✕",
                        Tag = session.Id,
                        FontSize = 10,
                        Padding = new Thickness(6, 0),
                        Background = Brushes.Transparent,
                        Foreground = (IBrush?)Application.Current!.Resources["TextBrush"] ?? Brushes.Gray,
                        Opacity = 0.55,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    };
                    closeButton.Click += OnCloseLogSessionTabClick;
                    group.Children.Add(closeButton);
                }

                LogSessionTabsPanel.Children.Add(group);
            }

            KillGameButton.IsEnabled = _activeLogSession?.IsGame == true
                && _activeLogSession.Process != null
                && !_activeLogSession.Process.HasExited;
        }

        private void OnLogSessionTabClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control control || control.Tag is not string sessionId)
                return;

            var session = _logSessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null)
                return;

            _activeLogSession = session;
            RebuildSessionTabs();
            RebuildLogView();
        }

        // Закрыть вкладку журнала можно, только если игра в ней уже не запущена —
        // иначе просто потеряем возможность её остановить/посмотреть, что там происходит.
        private void OnCloseLogSessionTabClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control control || control.Tag is not string sessionId)
                return;

            var session = _logSessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null || session == _launcherSession)
                return;

            if (session.IsGame && session.Process != null && !session.Process.HasExited)
            {
                AppendLogLine(Localization.Get("Session_CannotCloseRunning"), session);
                return;
            }

            _logSessions.Remove(session);

            if (_activeLogSession == session)
                _activeLogSession = _launcherSession;

            RebuildSessionTabs();
            RebuildLogView();
        }

        private LogSession CreateGameSession(string profileName)
        {
            var session = new LogSession
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = $"{profileName} — {DateTime.Now:HH:mm:ss}",
                IsGame = true,
                ProfileName = profileName
            };

            _logSessions.Add(session);
            _activeLogSession = session;
            RebuildSessionTabs();

            return session;
        }

        private LogSession? FindRunningSessionForProfile(string profileName) =>
            _logSessions.FirstOrDefault(s =>
                s.IsGame &&
                s.Process != null &&
                !s.Process.HasExited &&
                string.Equals(s.ProfileName, profileName, StringComparison.Ordinal));

        private void OnKillGameClick(object? sender, RoutedEventArgs e)
        {
            var session = _activeLogSession;

            if (session == null || !session.IsGame || session.Process == null)
            {
                AppendLogLine(Localization.Get("Session_NoActiveGame"), session);
                return;
            }

            KillSession(session);
        }

        private void KillSession(LogSession session)
        {
            try
            {
                if (!session.Process!.HasExited)
                {
                    session.Process.Kill(entireProcessTree: true);
                    AppendLogLine(Localization.Get("Session_KillSuccess"), session);
                }
                else
                {
                    AppendLogLine(Localization.Get("Session_AlreadyExited"), session);
                }
            }
            catch (Exception ex)
            {
                AppendLogLine(Localization.Get("Session_KillErrorFormat", ex.Message), session);
            }

            if (_activeLogSession == session)
                KillGameButton.IsEnabled = false;
        }

        private void OnAutoScrollToggled(object? sender, RoutedEventArgs e)
        {
            _autoScroll = AutoScrollCheck.IsChecked == true;

            if (_autoScroll)
                RebuildLogView();
        }

        private void OnWrapToggled(object? sender, RoutedEventArgs e)
        {
            _wrapLines = WrapLinesCheck.IsChecked == true;
            RebuildLogView();
        }

        private void OnColorToggled(object? sender, RoutedEventArgs e)
        {
            _colorLines = ColorLinesCheck.IsChecked == true;
            RebuildLogView();
        }

        private async void OnCopyLogClick(object? sender, RoutedEventArgs e)
        {
            var text = string.Join("\n", _activeLogSession?.Lines ?? (IEnumerable<string>)Array.Empty<string>());
            IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;

            if (clipboard != null)
            {
                await clipboard.SetTextAsync(text);
            }
        }

        private void OnClearLogClick(object? sender, RoutedEventArgs e)
        {
            if (_activeLogSession == null)
                return;

            _activeLogSession.Lines.Clear();
            _activeLogSession.PendingLines.Clear();
            RebuildLogView();
        }

        private void OnLogSearchChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e) => RebuildLogView();

        private void OnScrollToBottomClick(object? sender, RoutedEventArgs e) => _logScroll?.ScrollToEnd();

        // ---------- Главный экран ----------

        private void SetupHomeQuoteTransitions()
        {
            _homeQuoteTransform ??= new TranslateTransform(0, HomeQuoteRestY);
            HomeQuoteText.RenderTransform = _homeQuoteTransform;

            HomeQuoteText.Transitions = new Avalonia.Animation.Transitions
            {
                new Avalonia.Animation.DoubleTransition { Property = TextBlock.OpacityProperty, Duration = TimeSpan.FromMilliseconds(380) }
            };
            _homeQuoteTransform.Transitions = new Avalonia.Animation.Transitions
            {
                new Avalonia.Animation.DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(380) }
            };
        }

        // Плавно "уводит" текущую цитату вверх с исчезновением, затем подменяет текст и
        // "поднимает" следующую цитату снизу с появлением — эффект карусели/перелистывания.
        // Анимируется RenderTransform (не Margin!), поэтому это чисто визуальный сдвиг —
        // он не занимает/освобождает место в layout и не двигает остальной интерфейс.
        private void CycleHomeQuote()
        {
            HomeQuoteText.Opacity = 0;
            if (_homeQuoteTransform != null) _homeQuoteTransform.Y = HomeQuoteExitY;

            var swapTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            swapTimer.Tick += (s, _) =>
            {
                ((Avalonia.Threading.DispatcherTimer)s!).Stop();

                _homeQuoteIndex = (_homeQuoteIndex + 1) % HomeQuotes.Length;
                HomeQuoteText.Text = HomeQuotes[_homeQuoteIndex];

                // Мгновенно (без анимации) переносим строку ниже и делаем невидимой...
                HomeQuoteText.Transitions = null;
                if (_homeQuoteTransform != null) _homeQuoteTransform.Transitions = null;
                HomeQuoteText.Opacity = 0;
                if (_homeQuoteTransform != null) _homeQuoteTransform.Y = HomeQuoteEnterY;

                // ...и в следующем кадре включаем анимацию обратно и поднимаем её на место
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    SetupHomeQuoteTransitions();
                    HomeQuoteText.Opacity = HomeQuoteRestOpacity;
                    if (_homeQuoteTransform != null) _homeQuoteTransform.Y = HomeQuoteRestY;
                }, Avalonia.Threading.DispatcherPriority.Render);
            };
            swapTimer.Start();
        }

        private void RefreshHomeUi()
        {
            _isRefreshingHomeUi = true;

            HomeAccountCombo.ItemsSource = null;
            HomeAccountCombo.ItemsSource = _settings.Accounts.ToList();
            HomeAccountCombo.SelectedItem = _settings.Accounts
                .FirstOrDefault(a => a.Id == _settings.ActiveAccountId);

            HomeProfileCombo.ItemsSource = null;
            HomeProfileCombo.ItemsSource = _profiles.ToList();
            HomeProfileCombo.SelectedItem = _profiles
                .FirstOrDefault(p => p.Name == _settings.SelectedProfileName)
                ?? _profiles.FirstOrDefault();

            _isRefreshingHomeUi = false;
        }

        private void OnHomeAccountChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingHomeUi)
                return;

            if (HomeAccountCombo.SelectedItem is AccountEntry account)
            {
                _settings.ActiveAccountId = account.Id;
                _settingsService.Save(_settings);
            }
        }

        private void OnHomeProfileChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingHomeUi)
                return;

            if (HomeProfileCombo.SelectedItem is GameProfile profile)
            {
                _settings.SelectedProfileName = profile.Name;
                _settingsService.Save(_settings);
            }
        }

        private bool _isLaunching;

        private async void OnPlayClick(object? sender, RoutedEventArgs e)
        {
            // Повторный клик, пока идёт подготовка запуска, не должен запускать вторую игру
            if (_isLaunching)
                return;

            _isLaunching = true;
            try
            {
                await PlayAsync();
            }
            finally
            {
                _isLaunching = false;
            }
        }

        private async Task PlayAsync()
        {
            if (HomeProfileCombo.SelectedItem is not GameProfile profile)
            {
                SwitchToConsole();
                AppendLogLine(Localization.Get("Play_NoProfileWarning"));
                return;
            }

            if (HomeAccountCombo.SelectedItem is not AccountEntry account)
            {
                SwitchToConsole();
                AppendLogLine(Localization.Get("Play_NoAccountWarning"));
                return;
            }

            if (!string.IsNullOrEmpty(account.PinHash))
            {
                SwitchToConsole();
                AppendLogLine(Localization.Get("Account_PinRequiredWarning"));

                if (!await VerifyAccountPinAsync(account))
                    return;
            }

            if (account.Type == "Microsoft" && account.McAccessTokenExpiresUtc <= DateTime.UtcNow)
            {
                SwitchToConsole();
                AppendLogLine(Localization.Get("Account_RefreshingMicrosoftToken"));
                MicrosoftAuthResult refreshed;
                try
                {
                    refreshed = await new MicrosoftAuthService().RefreshAsync(account.MsRefreshToken);
                }
                catch (Exception ex)
                {
                    refreshed = new MicrosoftAuthResult { Success = false, Error = ex.Message };
                }

                if (!refreshed.Success)
                {
                    AppendLogLine(Localization.Get("Account_MicrosoftTokenRefreshFailed"));
                    return;
                }

                account.McAccessToken = refreshed.McAccessToken;
                account.McAccessTokenExpiresUtc = refreshed.McAccessTokenExpiresUtc;
                account.MsRefreshToken = refreshed.MsRefreshToken;
                account.Username = refreshed.Username;
                account.Uuid = refreshed.Uuid;
                _settingsService.Save(_settings);
            }

            var session = CreateGameSession(profile.Name);

            var progressWindow = new LaunchProgressWindow(ThemeCatalog.Resolve(_activeThemeKey).Text);
            progressWindow.Show(this);

            void Report(string line)
            {
                AppendLogLine(line, session);
                progressWindow.AppendLine(line);
            }

            const int totalSteps = 2;
            int step = 0;

            try
            {
                progressWindow.SetStage(Localization.Get("Play_CheckingFilesStage"), step, totalSteps, Localization.Get("Play_CheckingFilesDetail"));

                var versionFolder = Path.Combine(GameInstallerService.MinecraftDir, "versions", profile.VersionId);
                var jarPath = Path.Combine(versionFolder, $"{profile.VersionId}.jar");
                var jsonPath = Path.Combine(versionFolder, $"{profile.VersionId}.json");
                bool filesAlreadyExist = File.Exists(jarPath) && File.Exists(jsonPath);

                // Уже проверяли эту версию в текущем сеансе работы лаунчера — не гоняем сеть заново
                // на каждый повторный клик "Играть", файлы за последние секунды не испортились.
                if (_verifiedVersionsThisSession.Contains(profile.VersionId))
                {
                    Report(Localization.Get("Play_VerifySkippedCached", profile.VersionId));
                }
                else
                {
                    step++;
                    progressWindow.SetStage(Localization.Get("Play_CheckingVersionStageFormat", profile.VersionId), step, totalSteps);

                    try
                    {
                        if (_manifest == null)
                            _manifest = await _versionService.GetManifestAsync();

                        var versionEntry = _manifest?.Versions.Find(v => v.Id == profile.VersionId);

                        if (versionEntry != null)
                        {
                            await _installerService.InstallVersionAsync(versionEntry, new Progress<string>(Report));
                        }
                        else
                        {
                            Report(Localization.Get("Play_NotInManifestFormat", profile.VersionId));
                            await _installerService.EnsureCustomVersionBaseInstalledAsync(profile.VersionId, new Progress<string>(Report));
                        }

                        _verifiedVersionsThisSession.Add(profile.VersionId);
                    }
                    catch (Exception verifyEx) when (filesAlreadyExist)
                    {
                        // Нет сети (или Mojang недоступен) — но файлы уже стоят на диске, так что
                        // играть можно и офлайн, не заваливая весь запуск сетевой ошибкой.
                        Report(Localization.Get("Play_OfflineFallbackFormat", verifyEx.Message));
                        _verifiedVersionsThisSession.Add(profile.VersionId);
                    }
                }

                step++;
                progressWindow.SetStage(Localization.Get("Play_LaunchingStage"), step, totalSteps, Localization.Get("Play_LaunchingDetail"));
                var process = await _launchService.LaunchAsync(profile, account, _settings, new Progress<string>(Report));

                session.Process = process;

                if (process == null)
                {
                    // Java не стартовала / файлы не найдены — причина уже в журнале, окно прогресса не должно врать про успех
                    progressWindow.MarkFailed(Localization.Get("Play_LaunchFailedStage"));
                    RebuildSessionTabs();
                    return;
                }

                {
                    process.EnableRaisingEvents = true;
                    process.Exited += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        AppendLogLine(Localization.Get("Session_ExitedFormat", process.ExitCode), session);
                        if (_activeLogSession == session)
                            KillGameButton.IsEnabled = false;
                    });
                }

                RebuildSessionTabs();

                await progressWindow.CompleteSuccessAsync();
            }
            catch (Exception ex)
            {
                Report(Localization.Get("Play_LaunchFailedFormat", ex.Message));
                progressWindow.MarkFailed(Localization.Get("Play_LaunchFailedStage"));
                return;
            }

            if (_settings.LaunchBehavior == "Minimize")
                WindowState = WindowState.Minimized;
            else if (_settings.LaunchBehavior == "Close")
                Close();
        }

        // ---------- Профили ----------

        private void ReloadProfilesFromDisk()
        {
            _profiles.Clear();
            foreach (var profile in _profileService.LoadProfiles())
                _profiles.Add(profile);
        }

        private void SaveProfiles()
        {
            _profileService.SaveProfiles(_profiles.ToList());
            _launcherProfilesCompatService.Sync(_profiles.ToList());
        }

        private async void OnCreateProfileClick(object? sender, RoutedEventArgs e)
        {
            var dialog = new CreateProfileWindow(_settings, _settingsService);
            var result = await dialog.ShowDialog<ProfileCreationResult?>(this);

            if (result == null)
                return;

            _profiles.Add(result.Profile);
            SaveProfiles();

            if (result.InstallImmediately)
                await InstallProfileAsync(result.Profile);
        }

        private async void OnEditProfileClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not GameProfile profile)
                return;

            if (FindRunningSessionForProfile(profile.Name) != null)
            {
                SwitchToConsole();
                AppendLogLine(Localization.Get("Profile_CannotEditRunning", profile.Name));
                return;
            }

            var otherNames = _profiles.Where(p => !ReferenceEquals(p, profile)).Select(p => p.Name).ToList();
            var dialog = new EditProfileWindow(profile, otherNames, _settings, _settingsService);
            var updated = await dialog.ShowDialog<GameProfile?>(this);

            if (updated == null)
                return;

            var index = _profiles.IndexOf(profile);
            if (index < 0)
                return;

            bool versionChanged = updated.VersionId != profile.VersionId;

            if (_settings.SelectedProfileName == profile.Name)
                _settings.SelectedProfileName = updated.Name;

            _settingsService.Save(_settings);

            // Заменяем элемент целиком — так список профилей сразу обновляет подпись «Название (версия)»
            _profiles[index] = updated;
            SaveProfiles();
            RefreshHomeUi();

            if (versionChanged && updated.VersionType != "local" && !IsVersionInstalled(updated.VersionId))
                await InstallProfileAsync(updated);
        }

        private static bool IsVersionInstalled(string versionId) =>
            File.Exists(Path.Combine(GameInstallerService.MinecraftDir, "versions", versionId, $"{versionId}.json"));

        private void OnDeleteProfileClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is GameProfile profile)
            {
                if (FindRunningSessionForProfile(profile.Name) != null)
                {
                    SwitchToConsole();
                    AppendLogLine(Localization.Get("Profile_CannotDeleteRunning", profile.Name));
                    return;
                }

                _profiles.Remove(profile);
                SaveProfiles();
            }
        }

        private void OnKillProfileClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not GameProfile profile)
                return;

            var session = FindRunningSessionForProfile(profile.Name);

            if (session == null)
            {
                SwitchToConsole();
                AppendLogLine(Localization.Get("Session_NoActiveGame"));
                return;
            }

            _activeLogSession = session;
            SwitchToConsole();
            KillSession(session);
        }

        private async void OnInstallProfileClick(object? sender, RoutedEventArgs e)
        {
            if (ProfilesList.SelectedIndex < 0 || ProfilesList.SelectedIndex >= _profiles.Count)
                return;

            await InstallProfileAsync(_profiles[ProfilesList.SelectedIndex]);
        }

        private async Task InstallProfileAsync(GameProfile profile)
        {
            var progressWindow = new LaunchProgressWindow(ThemeCatalog.Resolve(_activeThemeKey).Text);
            progressWindow.Show(this);
            progressWindow.SetStage(Localization.Get("Install_InstallingStageFormat", profile.VersionId), 0, 1);

            void Report(string line)
            {
                AppendLogLine(line);
                progressWindow.AppendLine(line);
            }

            try
            {
                if (_manifest == null)
                    _manifest = await _versionService.GetManifestAsync();

                var versionEntry = _manifest?.Versions.Find(v => v.Id == profile.VersionId);
                if (versionEntry == null)
                {
                    Report(Localization.Get("Install_VersionNotFound"));
                    progressWindow.MarkFailed(Localization.Get("Install_FailedStage"));
                    return;
                }

                await _installerService.InstallVersionAsync(versionEntry, new Progress<string>(Report));
                await progressWindow.CompleteSuccessAsync();
            }
            catch (Exception ex)
            {
                Report(Localization.Get("Install_ErrorFormat", ex.Message));
                progressWindow.MarkFailed(Localization.Get("Install_FailedStage"));
            }
        }

        // ---------- Настройки: навигация по категориям ----------

        private void OnSettingsCategoryClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Control control && control.Tag is string category)
                ShowSettingsCategory(category);
        }

        private void ShowSettingsCategory(string category)
        {
            SettingsTabAppearance.IsChecked = category == "Appearance";
            SettingsTabGeneral.IsChecked = category == "General";
            SettingsTabGame.IsChecked = category == "Game";
            SettingsTabAbout.IsChecked = category == "About";

            AppearanceSettingsPanel.IsVisible = false;
            GeneralSettingsPanel.IsVisible = false;
            GameSettingsPanel.IsVisible = false;
            AboutSettingsPanel.IsVisible = false;

            Control targetPanel = category switch
            {
                "Appearance" => AppearanceSettingsPanel,
                "General" => GeneralSettingsPanel,
                "Game" => GameSettingsPanel,
                _ => AboutSettingsPanel
            };
            ShowPanelWithFade(targetPanel);

            if (category == "Appearance")
            {
                BuildThemeSwatches();
                BuildAccentSwatches();

                _isRefreshingAppearance = true;
                CornerRadiusSlider.Value = _settings.UiCornerRadius;
                CornerRadiusValueText.Text = $"{_settings.UiCornerRadius}px";
                _isRefreshingAppearance = false;
            }
            else if (category == "General")
            {
                BuildAccountsList();
                RefreshLaunchSettingsUi();

                MinecraftPathBox.Text = string.IsNullOrEmpty(_settings.MinecraftPath)
                    ? GameInstallerService.MinecraftDir
                    : _settings.MinecraftPath;
            }
            else if (category == "Game")
            {
                RefreshGameSettingsUi();
            }
        }

        // ---------- Настройки: Аккаунты ----------

        private void BuildAccountsList()
        {
            var items = _settings.Accounts
                .Select(a => new AccountDisplayItem
                {
                    Account = a,
                    IsActive = a.Id == _settings.ActiveAccountId
                })
                .ToList();

            AccountsList.ItemsSource = null;
            AccountsList.ItemsSource = items;
        }

        private void OnAddAccountClick(object? sender, RoutedEventArgs e)
        {
            var name = NewAccountNameBox.Text?.Trim();
            if (string.IsNullOrEmpty(name))
                return;

            var account = new AccountEntry { Username = name, Type = "Offline" };
            _settings.Accounts.Add(account);
            _settingsService.Save(_settings);

            NewAccountNameBox.Text = string.Empty;
            BuildAccountsList();
        }

        private async void OnMicrosoftLoginClick(object? sender, RoutedEventArgs e)
        {
            var loginWindow = new MicrosoftLoginWindow();
            await loginWindow.ShowDialog(this);
            var result = loginWindow.Result;

            if (result is not { Success: true })
                return;

            // Если этот же аккаунт Microsoft уже был добавлен раньше — просто обновляем токены,
            // а не плодим дубликаты записей на каждый повторный вход
            var existing = _settings.Accounts.FirstOrDefault(a =>
                a.Type == "Microsoft" && string.Equals(a.Uuid, result.Uuid, StringComparison.OrdinalIgnoreCase));

            var account = existing ?? new AccountEntry { Type = "Microsoft" };

            account.Username = result.Username;
            account.Uuid = result.Uuid;
            account.McAccessToken = result.McAccessToken;
            account.McAccessTokenExpiresUtc = result.McAccessTokenExpiresUtc;
            account.MsRefreshToken = result.MsRefreshToken;

            if (existing == null)
                _settings.Accounts.Add(account);

            _settings.ActiveAccountId = account.Id;
            _settingsService.Save(_settings);

            BuildAccountsList();
            RefreshHomeUi();
            AppendLogLine(Localization.Get("Account_MicrosoftLoginSuccessFormat", result.Username));
        }

        private readonly Dictionary<string, (int Fails, DateTime LockedUntil)> _pinAttempts = new();

        // Спрашивает PIN аккаунта. 3 ошибки подряд — блокировка на 30 секунд. Если PIN не задан — сразу true.
        private async Task<bool> VerifyAccountPinAsync(AccountEntry account)
        {
            if (string.IsNullOrEmpty(account.PinHash))
                return true;

            _pinAttempts.TryGetValue(account.Id, out var state);

            if (state.LockedUntil > DateTime.UtcNow)
            {
                var left = (int)Math.Ceiling((state.LockedUntil - DateTime.UtcNow).TotalSeconds);
                AppendLogLine(Localization.Get("Pin_LockedFormat", left));
                return false;
            }

            var prompt = new PinPromptWindow(Localization.Get("Pin_EnterTitle"), false, false);
            var pin = await prompt.ShowDialog<string?>(this);

            if (pin == null)
                return false;

            if (AccountService.VerifyPin(pin, account.PinHash))
            {
                _pinAttempts.Remove(account.Id);
                return true;
            }

            var fails = state.Fails + 1;

            if (fails >= 3)
            {
                _pinAttempts[account.Id] = (0, DateTime.UtcNow.AddSeconds(30));
                AppendLogLine(Localization.Get("Pin_LockedFormat", 30));
            }
            else
            {
                _pinAttempts[account.Id] = (fails, DateTime.MinValue);
                AppendLogLine(Localization.Get("Pin_Wrong"));
            }

            return false;
        }

        private async void OnSetPinClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not AccountEntry account)
                return;

            // сменить или снять PIN можно только зная текущий
            if (!await VerifyAccountPinAsync(account))
                return;

            var hasPin = !string.IsNullOrEmpty(account.PinHash);
            var prompt = hasPin
                ? new PinPromptWindow(Localization.Get("Pin_NewTitle"), true, true)
                : new PinPromptWindow(Localization.Get("Pin_SetTitle"), true, false);

            var pin = await prompt.ShowDialog<string?>(this);

            if (pin == null)
                return; // диалог отменили

            if (pin.Length == 0)
            {
                account.PinHash = null;
                AppendLogLine(Localization.Get("Account_PinRemovedFormat", account.Username));
            }
            else
            {
                account.PinHash = AccountService.HashPin(pin);
                AppendLogLine(Localization.Get("Account_PinSetFormat", account.Username));
            }

            _settingsService.Save(_settings);
            BuildAccountsList();
        }

        private void OnSelectAccountClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is AccountEntry account)
            {
                _settings.ActiveAccountId = account.Id;
                _settingsService.Save(_settings);

                BuildAccountsList();
            }
        }

        private async void OnDeleteAccountClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not AccountEntry account)
                return;

            if (!await VerifyAccountPinAsync(account))
                return;

            _settings.Accounts.Remove(account);

            if (_settings.Accounts.Count == 0)
            {
                var fallback = new AccountEntry { Username = "Player" };
                _settings.Accounts.Add(fallback);
                _settings.ActiveAccountId = fallback.Id;
            }
            else if (_settings.ActiveAccountId == account.Id)
            {
                _settings.ActiveAccountId = _settings.Accounts.First().Id;
            }

            _settingsService.Save(_settings);
            BuildAccountsList();
        }

        // ---------- Настройки: Игра (память + Java) ----------

        private void RefreshGameSettingsUi()
        {
            _isRefreshingGameSettings = true;

            if (_totalMemoryMb > 0)
            {
                MinMemorySlider.Maximum = _totalMemoryMb;
                MaxMemorySlider.Maximum = _totalMemoryMb;
                TotalMemoryText.Text = Localization.Get("Game_TotalRamFormat", _totalMemoryMb, (_totalMemoryMb / 1024.0).ToString("0.#"));
            }
            else
            {
                TotalMemoryText.Text = Localization.Get("Game_RamUnknown");
            }

            MinMemorySlider.Value = Math.Min(_settings.MinMemoryMb, MinMemorySlider.Maximum);
            MaxMemorySlider.Value = Math.Min(_settings.MaxMemoryMb, MaxMemorySlider.Maximum);

            // Сохранённое значение больше, чем есть ОЗУ (например, настройки с другого ПК) — приводим к реальному,
            // иначе игра запускалась бы с -Xmx больше памяти компьютера, а ползунок показывал бы другое число
            if (_settings.MaxMemoryMb > (int)MaxMemorySlider.Value || _settings.MinMemoryMb > (int)MinMemorySlider.Value)
            {
                _settings.MaxMemoryMb = Math.Min(_settings.MaxMemoryMb, (int)MaxMemorySlider.Value);
                _settings.MinMemoryMb = Math.Min(_settings.MinMemoryMb, _settings.MaxMemoryMb);
                _settingsService.Save(_settings);
            }
            MinMemoryValueText.Text = Localization.Get("Game_MemoryMbFormat", (int)MinMemorySlider.Value);
            MaxMemoryValueText.Text = Localization.Get("Game_MemoryMbFormat", (int)MaxMemorySlider.Value);

            JavaPathBox.Text = string.IsNullOrEmpty(_settings.JavaPath) ? string.Empty : _settings.JavaPath;
            AutoDownloadJavaCheck.IsChecked = _settings.AutoDownloadJava;

            _isRefreshingGameSettings = false;

            UpdateMemoryWarning();
        }

        private void UpdateMemoryWarning()
        {
            if (MemoryWarningText == null)
                return;

            // Берём значение прямо с ползунка — предупреждение меняется во время движения, а не после перезапуска
            long max = MaxMemorySlider != null ? (long)MaxMemorySlider.Value : _settings.MaxMemoryMb;
            string? text = null;
            string color = "#E05050"; // красный — серьёзно, оранжевый — предупреждение

            // Порядок важен: сначала «слишком мало» (<768 МБ), потом «слишком много» в долях от ОЗУ компьютера
            // (чтобы на слабых ноутбуках с 2 ГБ выделение всей памяти не считалось нормой), и только потом «маловато»
            if (max < 768)
            {
                text = Localization.Get("Game_MemoryTooLowFormat", max);
            }
            else if (_totalMemoryMb > 0 && max > _totalMemoryMb)
            {
                text = Localization.Get("Game_MemoryWarningFormat", max, _totalMemoryMb);
            }
            else if (_totalMemoryMb > 0 && max * 100 >= _totalMemoryMb * 80)
            {
                text = Localization.Get("Game_MemoryVeryHighFormat", max * 100 / _totalMemoryMb);
            }
            else if (_totalMemoryMb > 0 && max * 100 > _totalMemoryMb * 50)
            {
                text = Localization.Get("Game_MemoryHighFormat", max * 100 / _totalMemoryMb);
                color = "#E8A33D";
            }
            else if (max < 1024)
            {
                text = Localization.Get("Game_MemoryLowFormat", max);
                color = "#E8A33D";
            }

            if (text == null)
            {
                MemoryWarningText.IsVisible = false;
                return;
            }

            MemoryWarningText.Text = text;
            MemoryWarningText.Foreground = new SolidColorBrush(Color.Parse(color));
            MemoryWarningText.IsVisible = true;
        }

        private void OnMinMemoryChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isRefreshingGameSettings || MaxMemorySlider == null || MinMemoryValueText == null)
                return;

            var value = (int)MinMemorySlider.Value;

            if (value > MaxMemorySlider.Value)
                MaxMemorySlider.Value = value;

            _settings.MinMemoryMb = value;
            MinMemoryValueText.Text = Localization.Get("Game_MemoryMbFormat", value);
            ScheduleSettingsSave();

            UpdateMemoryWarning();
        }

        private void OnMaxMemoryChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isRefreshingGameSettings || MinMemorySlider == null || MaxMemoryValueText == null)
                return;

            var value = (int)MaxMemorySlider.Value;

            if (value < MinMemorySlider.Value)
                MinMemorySlider.Value = value;

            _settings.MaxMemoryMb = value;
            MaxMemoryValueText.Text = Localization.Get("Game_MemoryMbFormat", value);
            ScheduleSettingsSave();

            UpdateMemoryWarning();
        }

        private async void OnBrowseJavaClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Localization.Get("Game_ChooseJavaTitle"),
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Java (javaw.exe)") { Patterns = new[] { "javaw.exe", "java.exe" } }
                    }
                });

                if (files.Count == 0)
                    return;

                var path = files[0].Path.LocalPath;
                _settings.JavaPath = path;
                _settingsService.Save(_settings);
                JavaPathBox.Text = path;
            }
            catch (Exception ex)
            {
                AppendLogLine(Localization.Get("Game_JavaSelectErrorFormat", ex.Message));
            }
        }

        private async void OnOpenJavaManagerClick(object? sender, RoutedEventArgs e)
        {
            var window = new JavaManagerWindow(_settings, _settingsService, _settings.JavaPath);
            var result = await window.ShowDialog<string?>(this);

            if (result == null)
                return;

            _settings.JavaPath = result;
            _settingsService.Save(_settings);
            JavaPathBox.Text = result;
        }

        private void OnAutoDownloadJavaClick(object? sender, RoutedEventArgs e)
        {
            _settings.AutoDownloadJava = AutoDownloadJavaCheck.IsChecked == true;
            _settingsService.Save(_settings);
        }

        private void OnAutoDetectJavaClick(object? sender, RoutedEventArgs e)
        {
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");

            if (!string.IsNullOrEmpty(javaHome))
            {
                var candidate = Path.Combine(javaHome, "bin", "javaw.exe");
                if (File.Exists(candidate))
                {
                    _settings.JavaPath = candidate;
                    _settingsService.Save(_settings);
                    JavaPathBox.Text = candidate;
                    AppendLogLine(Localization.Get("Game_JavaAutoDetectedFormat", candidate));
                    return;
                }
            }

            AppendLogLine(Localization.Get("Game_JavaAutoDetectFailed"));
        }

        // ---------- Настройки: Запуск ----------

        private void RefreshLaunchSettingsUi()
        {
            LaunchKeepRadio.IsChecked = _settings.LaunchBehavior == "Keep";
            LaunchMinimizeRadio.IsChecked = _settings.LaunchBehavior == "Minimize";
            LaunchCloseRadio.IsChecked = _settings.LaunchBehavior == "Close";
        }

        private void OnLaunchBehaviorChanged(object? sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radio && radio.Tag is string behavior)
            {
                _settings.LaunchBehavior = behavior;
                _settingsService.Save(_settings);
            }
        }

        // ---------- Настройки: Внешний вид ----------

        private void BuildAccentSwatches()
        {
            AccentSwatchesPanel.Children.Clear();

            foreach (var hex in AccentPresets)
            {
                var swatch = new Button
                {
                    Classes = { "swatch" },
                    Background = new SolidColorBrush(Color.Parse(hex)),
                    Tag = hex
                };

                if (string.Equals(hex, _settings.AccentColor, StringComparison.OrdinalIgnoreCase))
                {
                    swatch.Classes.Add("swatch-selected");
                }

                swatch.Click += OnAccentSwatchClick;
                AccentSwatchesPanel.Children.Add(swatch);
            }
        }

        private void OnAccentSwatchClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string hex)
                return;

            _settings.AccentColor = hex;
            _settingsService.Save(_settings);

            ApplyAccentColor(hex);
            BuildAccentSwatches();
        }

        private void OnApplyCustomAccentClick(object? sender, RoutedEventArgs e)
        {
            var text = CustomAccentBox.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(text))
                return;

            if (!text.StartsWith("#"))
                text = "#" + text;

            try
            {
                Color.Parse(text);

                _settings.AccentColor = text;
                _settingsService.Save(_settings);

                ApplyAccentColor(text);
                BuildAccentSwatches();

                CustomAccentErrorText.IsVisible = false;
                CustomAccentBox.Text = string.Empty;
            }
            catch
            {
                CustomAccentErrorText.Text = Localization.Get("Appearance_ColorParseError");
                CustomAccentErrorText.IsVisible = true;
            }
        }

        private void OnCornerRadiusChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isRefreshingAppearance || CornerRadiusValueText == null)
                return;

            var value = (int)CornerRadiusSlider.Value;
            _settings.UiCornerRadius = value;
            ScheduleSettingsSave();

            CornerRadiusValueText.Text = $"{value}px";
            ApplyCornerRadius(value);
        }

        private void ApplyCornerRadius(int value)
        {
            Application.Current!.Resources["UiCornerRadius"] = new CornerRadius(value);
        }

        private void BuildThemeSwatches()
        {
            ThemeSwatchesPanel.Children.Clear();

            foreach (var theme in ThemeCatalog.All)
            {
                var container = new StackPanel
                {
                    Spacing = 4,
                    Width = 68,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                };

                var themeDisplayName = Localization.Get($"Theme_{theme.Key}");

                var swatch = new Button
                {
                    Classes = { "swatch" },
                    Width = 44,
                    Height = 44,
                    Background = new SolidColorBrush(Color.Parse(theme.Background)),
                    BorderBrush = new SolidColorBrush(Color.Parse(theme.Border)),
                    BorderThickness = new Thickness(1),
                    Tag = theme.Key,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                };
                ToolTip.SetTip(swatch, themeDisplayName);

                if (string.Equals(theme.Key, _activeThemeKey, StringComparison.OrdinalIgnoreCase))
                {
                    swatch.Classes.Add("swatch-selected");
                }

                swatch.Click += OnThemeSwatchClick;

                var label = new TextBlock
                {
                    Text = themeDisplayName,
                    FontSize = 10,
                    Opacity = 0.7,
                    TextAlignment = Avalonia.Media.TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (IBrush?)Application.Current!.Resources["TextBrush"] ?? Brushes.Gray
                };

                container.Children.Add(swatch);
                container.Children.Add(label);
                ThemeSwatchesPanel.Children.Add(container);
            }
        }

        private void OnThemeSwatchClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string themeKey)
                return;

            _settings.Theme = themeKey;

            // Если сейчас идёт праздник и тема подставлялась автоматически, ручной выбор отключает автоподстановку
            if (_settings.AutoSeasonalTheme && SeasonalService.Current(DateTime.Now) != Season.None)
            {
                _settings.AutoSeasonalTheme = false;
                AutoSeasonalCheck.IsChecked = false;
            }

            _settingsService.Save(_settings);

            ApplyTheme(themeKey);
            BuildThemeSwatches();
            RebuildLogView();
        }

        private void ApplyTheme(string themeKey)
        {
            var theme = ThemeCatalog.Resolve(themeKey);
            _activeThemeKey = theme.Key;

            Application.Current!.RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

            var resources = Application.Current!.Resources;
            resources["BackgroundBrush"] = new SolidColorBrush(Color.Parse(theme.Background));
            resources["PanelBrush"] = new SolidColorBrush(Color.Parse(theme.Panel));
            resources["BorderColorBrush"] = new SolidColorBrush(Color.Parse(theme.Border));
            resources["TextBrush"] = new SolidColorBrush(Color.Parse(theme.Text));

            _chromeText = Color.Parse(theme.Text);
            _chromePanel = Color.Parse(theme.Panel);
            _chromeBorder = Color.Parse(theme.Border);
            ApplyChromeBrushes();

            ApplyDecor(theme.Key);
        }

        private void ApplyLanguage(string language)
        {
            Localization.SetLanguage(language);
            Localization.ApplyToResources(Application.Current!);

            _launcherSession.Name = Localization.Get("Console_LauncherSessionName");
            RebuildSessionTabs();

            if (AppearanceSettingsPanel.IsVisible)
                BuildThemeSwatches();

            if (GeneralSettingsPanel.IsVisible)
                BuildAccountsList();

            LanguageRuButton.IsChecked = Localization.CurrentLanguage == "ru";
            LanguageEnButton.IsChecked = Localization.CurrentLanguage == "en";
        }

        private void OnLanguageButtonClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control control || control.Tag is not string language)
                return;

            _settings.Language = language;
            _settingsService.Save(_settings);
            ApplyLanguage(language);
        }

        private void ApplyAccentColor(string hex)
        {
            var color = Color.Parse(hex);
            var resources = Application.Current!.Resources;

            resources["AccentBrush"] = new SolidColorBrush(color);
            resources["SystemAccentColor"] = color;
            resources["SystemAccentColorLight1"] = Lighten(color, 0.20);
            resources["SystemAccentColorLight2"] = Lighten(color, 0.40);
            resources["SystemAccentColorLight3"] = Lighten(color, 0.60);
            resources["SystemAccentColorDark1"] = Darken(color, 0.15);
            resources["SystemAccentColorDark2"] = Darken(color, 0.30);
            resources["SystemAccentColorDark3"] = Darken(color, 0.45);

            _chromeAccent = color;
            ApplyChromeBrushes();
        }

        private static Color Lighten(Color c, double amount)
        {
            byte Mix(byte channel) => (byte)(channel + (255 - channel) * amount);
            return new Color(c.A, Mix(c.R), Mix(c.G), Mix(c.B));
        }

        private static Color Darken(Color c, double amount)
        {
            byte Mix(byte channel) => (byte)(channel * (1 - amount));
            return new Color(c.A, Mix(c.R), Mix(c.G), Mix(c.B));
        }

        // ---------- Настройки: Хранение ----------

        private async void OnBrowseMinecraftPathClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = Localization.Get("Storage_ChooseFolderTitle"),
                    AllowMultiple = false
                });

                if (folders.Count == 0)
                    return;

                var path = folders[0].Path.LocalPath;

                _settings.MinecraftPath = path;
                _settingsService.Save(_settings);

                GameInstallerService.MinecraftDir = path;
                MinecraftPathBox.Text = path;
            }
            catch (Exception ex)
            {
                MinecraftPathBox.Text = Localization.Get("Storage_FolderPickErrorFormat", ex.Message);
            }
        }

        private void OnOpenMinecraftFolderClick(object? sender, RoutedEventArgs e)
        {
            OpenFolder(GameInstallerService.MinecraftDir);
        }

        private void OnOpenLogsFolderClick(object? sender, RoutedEventArgs e)
        {
            var logsPath = Path.Combine(GameInstallerService.MinecraftDir, "logs");
            OpenFolder(logsPath);
        }

        private void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppendLogLine(Localization.Get("Storage_OpenFolderErrorFormat", path, ex.Message));
            }
        }
    }
}