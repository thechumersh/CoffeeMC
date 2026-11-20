using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using CoffeeMC.App.Services;

namespace CoffeeMC.App.Views
{
    public partial class MicrosoftLoginWindow : Window
    {
        private readonly MicrosoftAuthService _authService = new();
        private CancellationTokenSource? _cts;
        private string _currentCode = string.Empty;
        private string _currentUrl = "https://microsoft.com/link";

        public MicrosoftAuthResult? Result { get; private set; }

        public MicrosoftLoginWindow()
        {
            InitializeComponent();
            this.Opened += (_, _) => Opacity = 1;
            Loaded += async (_, _) => await StartFlowAsync();
            Closing += (_, _) => _cts?.Cancel();
        }

        private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        private async Task StartFlowAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            CodeBorder.IsVisible = false;
            UrlHintText.IsVisible = false;
            ActionButtonsPanel.IsVisible = false;
            ErrorText.IsVisible = false;
            RetryButton.IsVisible = false;
            StatusText.Text = "Получаю код для входа...";

            try
            {
                var device = await _authService.RequestDeviceCodeAsync();

                _currentCode = device.UserCode;
                _currentUrl = device.VerificationUri;

                CodeText.Text = _currentCode;
                UrlHintText.Text = $"Перейди на {_currentUrl} и введи код выше";
                StatusText.Text = "Ожидаю подтверждения входа в браузере...";

                CodeBorder.IsVisible = true;
                UrlHintText.IsVisible = true;
                ActionButtonsPanel.IsVisible = true;

                var result = await _authService.PollForTokenAsync(device, token);

                if (token.IsCancellationRequested)
                    return;

                if (result.Success)
                {
                    Result = result;
                    Close();
                    return;
                }

                ShowError(result.Error);
            }
            catch (OperationCanceledException)
            {
                // окно закрыли/перезапустили поток — ничего не делаем
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void ShowError(string? code)
        {
            CodeBorder.IsVisible = false;
            UrlHintText.IsVisible = false;
            ActionButtonsPanel.IsVisible = false;
            RetryButton.IsVisible = true;

            StatusText.Text = "Не удалось войти";

            ErrorText.Text = code switch
            {
                "timeout" => "Время на ввод кода истекло. Попробуй ещё раз.",
                "authorization_declined" => "Вход отменён в браузере.",
                "no_xbox_account" => "На этом аккаунте Microsoft нет профиля Xbox. Создай его на xbox.com и попробуй снова.",
                "region_blocked" => "Xbox Live недоступен в этом регионе на этом аккаунте.",
                "adult_verification_needed" => "Аккаунту требуется подтверждение возраста на account.live.com.",
                "underage" => "Аккаунт требует присоединения к семейной группе (family) на account.live.com.",
                "no_game_ownership" => "На этом аккаунте Microsoft не куплена Minecraft.",
                "app_not_approved" => "Minecraft отклонил приложение (403). Новый Azure-клиент нужно одобрить у Mojang: форма на aka.ms/AppRegInfo, одобрение занимает несколько дней.",
                "no_profile" => "Не удалось получить профиль Minecraft (ник/UUID).",
                "refresh_failed" => "Сессия входа устарела — нужно войти заново.",
                _ => string.IsNullOrEmpty(code) ? "Неизвестная ошибка." : $"Ошибка: {code}"
            };

            ErrorText.IsVisible = true;
        }

        private async void OnRetryClick(object? sender, RoutedEventArgs e) => await StartFlowAsync();

        private void OnOpenBrowserClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Открываем только страницы Microsoft по https: адрес пришёл из ответа сервера
                if (CoffeeMC.App.Services.NetClient.IsHostOrSubdomain(_currentUrl, "microsoft.com", "live.com", "microsoftonline.com"))
                    Process.Start(new ProcessStartInfo(_currentUrl) { UseShellExecute = true });
            }
            catch
            {
                // не получилось открыть браузер автоматически — у пользователя всё ещё есть адрес и код на экране
            }
        }

        private async void OnCopyCodeClick(object? sender, RoutedEventArgs e)
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null && !string.IsNullOrEmpty(_currentCode))
                await clipboard.SetTextAsync(_currentCode);
        }

        private void OnCancelClick(object? sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            Result = null;
            Close();
        }
    }
}
