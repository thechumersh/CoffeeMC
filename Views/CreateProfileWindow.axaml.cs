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
    public partial class CreateProfileWindow : Window
    {
        private readonly VersionManifestService _versionService = new();
        private VersionManifest? _manifest;
		private readonly LocalVersionService _localVersionService = new();

        private readonly AppSettings? _settings;
        private readonly SettingsService? _settingsService;
        private bool _installPref = true;

        // Пустой конструктор — только для XAML-загрузчика; без настроек ничего не запоминает
        public CreateProfileWindow() : this(null, null) { }

        public CreateProfileWindow(AppSettings? settings, SettingsService? settingsService)
        {
            InitializeComponent();
            _settings = settings;
            _settingsService = settingsService;

            if (_settings != null)
            {
                FilterRelease.IsChecked = _settings.ProfileFilterRelease;
                FilterSnapshot.IsChecked = _settings.ProfileFilterSnapshot;
                FilterBeta.IsChecked = _settings.ProfileFilterBeta;
                FilterAlpha.IsChecked = _settings.ProfileFilterAlpha;
                FilterLocal.IsChecked = _settings.ProfileFilterLocal;
                _installPref = _settings.ProfileInstallImmediately;
                InstallCheck.IsChecked = _installPref;
            }

            Loaded += async (_, _) => await LoadVersionsAsync();
            this.Opened += (_, _) => Opacity = 1;
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
                ApplyFilters();
            }
            catch (Exception ex)
            {
                VersionsList.ItemsSource = new[] { Localization.Get("CreateProfile_LoadVersionsErrorFormat", ex.Message) };
            }
        }

        // Сбрасываем кэш списка версий и перечитываем и онлайн-список, и локальные версии на диске
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

        private void OnFilterChanged(object? sender, RoutedEventArgs e)
        {
            SavePreferences();
            ApplyFilters();
        }

        private void SavePreferences()
        {
            if (_settings == null || _settingsService == null)
                return;

            _settings.ProfileFilterRelease = FilterRelease.IsChecked == true;
            _settings.ProfileFilterSnapshot = FilterSnapshot.IsChecked == true;
            _settings.ProfileFilterBeta = FilterBeta.IsChecked == true;
            _settings.ProfileFilterAlpha = FilterAlpha.IsChecked == true;
            _settings.ProfileFilterLocal = FilterLocal.IsChecked == true;
            _settings.ProfileInstallImmediately = _installPref;
            _settingsService.Save(_settings);
        }

        private void OnInstallCheckClick(object? sender, RoutedEventArgs e)
        {
            _installPref = InstallCheck.IsChecked == true;
            SavePreferences();
        }

        // Локальная версия уже стоит на компьютере — скачивать нечего, поэтому "Установить сразу" блокируется
        private void OnVersionSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (VersionsList.SelectedItem is LocalVersionInfo)
            {
                InstallCheck.IsChecked = false;
                InstallCheck.IsEnabled = false;
            }
            else
            {
                InstallCheck.IsEnabled = true;
                InstallCheck.IsChecked = _installPref;
            }
        }

        private void OnFilterChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            var searchText = SearchBox.Text?.Trim() ?? string.Empty;
            var combined = new List<object>();

            if (_manifest != null)
            {
                var allowedTypes = new List<string>();
                if (FilterRelease.IsChecked == true) allowedTypes.Add("release");
                if (FilterSnapshot.IsChecked == true) allowedTypes.Add("snapshot");
                if (FilterBeta.IsChecked == true) allowedTypes.Add("old_beta");
                if (FilterAlpha.IsChecked == true) allowedTypes.Add("old_alpha");

                combined.AddRange(_manifest.Versions
                    .Where(v => allowedTypes.Contains(v.Type))
                    .Where(v => string.IsNullOrEmpty(searchText) ||
                                v.Id.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .Cast<object>());
            }

            if (FilterLocal.IsChecked == true)
            {
                var local = _localVersionService.ScanLocalVersions()
                    .Where(v => string.IsNullOrEmpty(searchText) ||
                                v.Id.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .Cast<object>();

                combined.AddRange(local);
            }
        
            VersionsList.ItemsSource = combined;
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            Close(null);
        }

        private void OnCreateClick(object? sender, RoutedEventArgs e)
        {
            var name = NameBox.Text?.Trim();

            string? versionId = null;
            string versionType = "release";

            if (VersionsList.SelectedItem is VersionEntry onlineVersion)
            {
                versionId = onlineVersion.Id;
                versionType = onlineVersion.Type;
            }
            else if (VersionsList.SelectedItem is LocalVersionInfo localVersion)
            {
                versionId = localVersion.Id;
                versionType = "local";
            }

            if (string.IsNullOrEmpty(name))
            {
                ShowError(Localization.Get("CreateProfile_ErrorNoName"));
                return;
            }

            if (versionId == null)
            {
                ShowError(Localization.Get("CreateProfile_ErrorNoVersion"));
                return;
            }

            HideError();

            var result = new ProfileCreationResult
            {
                Profile = new GameProfile
                {
                    Name = name,
                    VersionId = versionId,
                    VersionType = versionType
                },
                InstallImmediately = InstallCheck.IsChecked == true && versionType != "local"
            };

            Close(result);
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.IsVisible = true;
        }

        private void HideError()
        {
            ErrorText.IsVisible = false;
        }
    }
}