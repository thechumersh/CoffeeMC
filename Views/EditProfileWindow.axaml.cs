using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CoffeeMC.App.Models;
using CoffeeMC.App.Services;

namespace CoffeeMC.App.Views
{
    // Результат ShowDialog<GameProfile?>: новый (изменённый) профиль или null, если нажали «Отмена»
    public partial class EditProfileWindow : Window
    {
        private readonly HashSet<string> _otherNames;
        private readonly AppSettings _settings;
        private readonly SettingsService _settingsService;
        private readonly VersionManifestService _versionService = new();
        private readonly LocalVersionService _localVersionService = new();

        private VersionManifest? _manifest;
        private string _javaPath;

        // Что выбрано в списке версий (запоминаем отдельно, чтобы выбор не терялся при смене фильтров)
        private string _selectedId;
        private string _selectedType;

        // Пустой конструктор — только для XAML-загрузчика
        public EditProfileWindow() : this(new GameProfile(), Array.Empty<string>(), new AppSettings(), new SettingsService())
        {
        }

        public EditProfileWindow(GameProfile profile, IEnumerable<string> otherNames, AppSettings settings, SettingsService settingsService)
        {
            InitializeComponent();

            _otherNames = new HashSet<string>(otherNames, StringComparer.OrdinalIgnoreCase);
            _settings = settings;
            _settingsService = settingsService;
            _javaPath = profile.JavaPath ?? string.Empty;
            _selectedId = profile.VersionId;
            _selectedType = profile.VersionType;

            NameBox.Text = profile.Name;
            JvmBox.Text = profile.JvmArguments;
            GameArgsBox.Text = profile.GameArguments;
            UpdateJavaText();

            // Включаем фильтры так, чтобы текущая версия профиля попала в список
            FilterBeta.IsChecked = profile.VersionType == "old_beta";
            FilterAlpha.IsChecked = profile.VersionType == "old_alpha";
            FilterLocal.IsChecked = profile.VersionType == "local";
            if (profile.VersionType == "local")
            {
                FilterRelease.IsChecked = false;
                FilterSnapshot.IsChecked = false;
            }

            Loaded += async (_, _) => await LoadVersionsAsync();
            Opened += (_, _) => Opacity = 1;
        }

        private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        private async Task LoadVersionsAsync()
        {
            try
            {
                _manifest = await _versionService.GetManifestAsync();
            }
            catch
            {
                // Нет сети — всё равно можно менять остальные поля и локальные версии; версия профиля останется прежней
            }

            ApplyFilters();
        }

        private async void OnRefreshVersionsClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button)
                button.IsEnabled = false;

            try
            {
                VersionManifestService.Invalidate();
                await LoadVersionsAsync();
            }
            finally
            {
                if (sender is Button b)
                    b.IsEnabled = true;
            }
        }

        private void OnFilterChanged(object? sender, RoutedEventArgs e) => ApplyFilters();

        private void OnSearchChanged(object? sender, TextChangedEventArgs e) => ApplyFilters();

        private void ApplyFilters()
        {
            var search = SearchBox.Text?.Trim() ?? string.Empty;
            bool Matches(string id) => string.IsNullOrEmpty(search) || id.Contains(search, StringComparison.OrdinalIgnoreCase);

            var items = new List<object>();

            if (_manifest != null)
            {
                var allowed = new List<string>();
                if (FilterRelease.IsChecked == true) allowed.Add("release");
                if (FilterSnapshot.IsChecked == true) allowed.Add("snapshot");
                if (FilterBeta.IsChecked == true) allowed.Add("old_beta");
                if (FilterAlpha.IsChecked == true) allowed.Add("old_alpha");

                items.AddRange(_manifest.Versions
                    .Where(v => allowed.Contains(v.Type) && Matches(v.Id))
                    .Cast<object>());
            }

            if (FilterLocal.IsChecked == true)
            {
                items.AddRange(_localVersionService.ScanLocalVersions()
                    .Where(v => Matches(v.Id))
                    .Cast<object>());
            }

            // Выбранная версия всегда первой в списке — не нужно её искать прокруткой
            var selected = items.FirstOrDefault(IsSelectedItem);
            if (selected != null)
            {
                items.Remove(selected);
                items.Insert(0, selected);
            }

            VersionsList.ItemsSource = items;
            VersionsList.SelectedItem = selected;
        }

        private bool IsSelectedItem(object item) => item switch
        {
            VersionEntry v => v.Id == _selectedId && _selectedType != "local",
            LocalVersionInfo l => l.Id == _selectedId && _selectedType == "local",
            _ => false
        };

        private void OnVersionSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // При пересборке списка приходит null — игнорируем, чтобы не потерять выбор
            switch (VersionsList.SelectedItem)
            {
                case VersionEntry v:
                    _selectedId = v.Id;
                    _selectedType = v.Type;
                    break;
                case LocalVersionInfo l:
                    _selectedId = l.Id;
                    _selectedType = "local";
                    break;
            }
        }

        private void UpdateJavaText()
        {
            JavaBox.Text = string.IsNullOrEmpty(_javaPath) ? Localization.Get("EditProfile_JavaAuto") : _javaPath;
        }

        private async void OnChooseJavaClick(object? sender, RoutedEventArgs e)
        {
            var window = new JavaManagerWindow(_settings, _settingsService, _javaPath);
            var result = await window.ShowDialog<string?>(this);

            if (result == null)
                return;

            _javaPath = result;
            UpdateJavaText();
        }

        private void OnResetJavaClick(object? sender, RoutedEventArgs e)
        {
            _javaPath = string.Empty;
            UpdateJavaText();
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

        private void OnSaveClick(object? sender, RoutedEventArgs e)
        {
            var name = NameBox.Text?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                ShowError(Localization.Get("CreateProfile_ErrorNoName"));
                return;
            }

            if (_otherNames.Contains(name))
            {
                ShowError(Localization.Get("EditProfile_NameTaken"));
                return;
            }

            ErrorText.IsVisible = false;

            Close(new GameProfile
            {
                Name = name,
                VersionId = _selectedId,
                VersionType = _selectedType,
                JvmArguments = JvmBox.Text?.Trim() ?? string.Empty,
                GameArguments = GameArgsBox.Text?.Trim() ?? string.Empty,
                JavaPath = _javaPath
            });
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.IsVisible = true;
        }
    }
}
