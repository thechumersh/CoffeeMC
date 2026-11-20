using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CoffeeMC.App.Services;

namespace CoffeeMC.App.Views
{
    public partial class PinPromptWindow : Window
    {
        private readonly bool _confirm;
        private readonly bool _allowEmpty;

        public PinPromptWindow() : this("PIN", false, false)
        {
        }

        // confirm — спросить PIN дважды (задание нового); allowEmpty — пустой ввод значит "снять защиту"
        // Результат ShowDialog: null — отмена, "" — снять защиту, иначе введённый PIN
        public PinPromptWindow(string title, bool confirm, bool allowEmpty)
        {
            InitializeComponent();
            _confirm = confirm;
            _allowEmpty = allowEmpty;

            TitleText.Text = title;
            ConfirmBox.IsVisible = confirm;
            ConfirmBox.PlaceholderText = Localization.Get("Pin_ConfirmPlaceholder");

            Opened += (_, _) =>
            {
                Opacity = 1;
                PinBox.Focus();
            };
        }

        private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        private void OnPinKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Submit();
        }

        private void OnOkClick(object? sender, RoutedEventArgs e) => Submit();

        private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

        private void Submit()
        {
            var pin = PinBox.Text ?? string.Empty;

            if (_confirm)
            {
                var again = ConfirmBox.Text ?? string.Empty;

                if (pin.Length == 0 && again.Length == 0 && _allowEmpty)
                {
                    Close(string.Empty);
                    return;
                }

                if (pin.Length < 4)
                {
                    ShowError(Localization.Get("Pin_TooShort"));
                    return;
                }

                if (pin != again)
                {
                    ShowError(Localization.Get("Pin_Mismatch"));
                    return;
                }
            }
            else if (pin.Length == 0)
            {
                return;
            }

            Close(pin);
        }

        private void ShowError(string text)
        {
            ErrorText.Text = text;
            ErrorText.IsVisible = true;
        }
    }
}
