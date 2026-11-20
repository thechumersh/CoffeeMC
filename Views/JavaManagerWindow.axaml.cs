using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CoffeeMC.App.Models;
using CoffeeMC.App.Services;

namespace CoffeeMC.App.Views
{
    // Результат ShowDialog<string?>: null — закрыли без выбора, "" — «Автоматически», иначе путь к javaw.exe
    public partial class JavaManagerWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly SettingsService _settingsService;
        private readonly string _currentPath;
        private List<JavaInstall> _installs = new();
        private CancellationTokenSource? _cts;
        private bool _deleteArmed;

        // Пустой конструктор — только для XAML-загрузчика
        public JavaManagerWindow() : this(new AppSettings(), new SettingsService(), string.Empty)
        {
        }

        public JavaManagerWindow(AppSettings settings, SettingsService settingsService, string currentPath)
        {
            InitializeComponent();
            _settings = settings;
            _settingsService = settingsService;
            _currentPath = currentPath ?? string.Empty;

            Opened += async (_, _) =>
            {
                Opacity = 1;
                await LoadDownloadVersionsAsync();
                await LoadInstallsAsync(_currentPath);
            };

            Closing += (_, _) => _cts?.Cancel();
        }

        private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        private async Task LoadDownloadVersionsAsync()
        {
            var majors = await JavaDownloadService.GetAvailableMajorsAsync();
            DownloadVersionCombo.ItemsSource = majors;
            DownloadVersionCombo.SelectedItem = majors.Contains(21) ? 21 : majors.LastOrDefault();
        }

        private async Task LoadInstallsAsync(string? selectPath)
        {
            StatusText.Text = Localization.Get("Java_Searching");
            ErrorText.IsVisible = false;

            var custom = _settings.CustomJavaPaths.ToList();
            _installs = await Task.Run(() => JavaDiscoveryService.FindAll(custom));
            JavaList.ItemsSource = _installs;

            if (!string.IsNullOrEmpty(selectPath))
            {
                var normalized = JavaDiscoveryService.NormalizeExecutable(selectPath);
                JavaList.SelectedItem = _installs.FirstOrDefault(i =>
                    string.Equals(i.Executable, normalized, StringComparison.OrdinalIgnoreCase));
            }

            StatusText.Text = _installs.Count == 0
                ? Localization.Get("Java_NoneFound")
                : Localization.Get("Java_FoundFormat", _installs.Count);
        }

        private void OnJavaSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var selected = JavaList.SelectedItem as JavaInstall;
            SelectButton.IsEnabled = selected != null;
            DeleteButton.IsEnabled = selected is { IsManaged: true };
            ResetDeleteButton();
        }

        private void ResetDeleteButton()
        {
            _deleteArmed = false;
            DeleteButton.Content = Localization.Get("Java_Delete");
        }

        private async void OnRefreshClick(object? sender, RoutedEventArgs e)
        {
            await LoadInstallsAsync((JavaList.SelectedItem as JavaInstall)?.Executable);
        }

        private async void OnAddManualClick(object? sender, RoutedEventArgs e)
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

                var path = JavaDiscoveryService.NormalizeExecutable(files[0].Path.LocalPath);
                var info = await Task.Run(() => JavaDiscoveryService.Inspect(path, JavaSource.Custom));

                if (info == null)
                {
                    ShowError(Localization.Get("Java_NotJava"));
                    return;
                }

                if (!_settings.CustomJavaPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    _settings.CustomJavaPaths.Add(path);
                    _settingsService.Save(_settings);
                }

                await LoadInstallsAsync(path);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private async void OnDownloadClick(object? sender, RoutedEventArgs e)
        {
            if (DownloadVersionCombo.SelectedItem is not int major)
                return;

            ErrorText.IsVisible = false;
            DownloadButton.IsEnabled = false;
            DownloadProgress.Value = 0;
            DownloadProgress.IsVisible = true;

            _cts = new CancellationTokenSource();

            // Progress<T> создан в UI-потоке, поэтому обработчики выполняются тоже в нём
            var percent = new Progress<double>(p => DownloadProgress.Value = p);
            var log = new Progress<string>(text => StatusText.Text = text);

            try
            {
                var install = await JavaDownloadService.DownloadAsync(major, log, percent, _cts.Token);
                await LoadInstallsAsync(install.Executable);
                StatusText.Text = Localization.Get("Java_DownloadDoneFormat", major, install.Executable);
            }
            catch (OperationCanceledException)
            {
                // окно закрыли во время загрузки
            }
            catch (Exception ex)
            {
                ShowError(Localization.Get("Java_DownloadErrorFormat", ex.Message));
            }
            finally
            {
                DownloadButton.IsEnabled = true;
                DownloadProgress.IsVisible = false;
            }
        }

        // Удалять можно только Java, скачанные самим лаунчером, и только после повторного нажатия
        private void OnDeleteClick(object? sender, RoutedEventArgs e)
        {
            if (JavaList.SelectedItem is not JavaInstall { IsManaged: true } install)
                return;

            if (!_deleteArmed)
            {
                _deleteArmed = true;
                DeleteButton.Content = Localization.Get("Java_DeleteConfirm");
                return;
            }

            try
            {
                var root = Path.GetFullPath(JavaDiscoveryService.ManagedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var home = Path.GetFullPath(install.HomeDir);

                // защита: ни при каких условиях не трогаем ничего за пределами папки Java лаунчера
                if (!home.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return;

                Directory.Delete(home, true);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }

            ResetDeleteButton();
            _ = LoadInstallsAsync(null);
        }

        private void OnSelectClick(object? sender, RoutedEventArgs e)
        {
            if (JavaList.SelectedItem is JavaInstall install)
                Close(install.Executable);
        }

        private void OnUseAutoClick(object? sender, RoutedEventArgs e) => Close(string.Empty);

        private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.IsVisible = true;
        }
    }
}
