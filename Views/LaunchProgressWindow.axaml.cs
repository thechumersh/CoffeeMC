using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CoffeeMC.App.Models;
using CoffeeMC.App.Services;

namespace CoffeeMC.App.Views
{
    public partial class LaunchProgressWindow : Window
    {
        private const int MaxVisibleLines = 600;
        private readonly ObservableCollection<LogDisplayLine> _items = new();
        private bool _scrollPending;
        private readonly IBrush _defaultTextBrush;

        // Пустой конструктор — только для внутреннего XAML-загрузчика Avalonia, в коде не используется
        public LaunchProgressWindow() : this("#F0DCC4") { }

        public LaunchProgressWindow(string defaultTextColorHex)
        {
            InitializeComponent();
            _defaultTextBrush = new SolidColorBrush(Color.Parse(defaultTextColorHex));
            LogItemsControl.ItemsSource = _items;
            this.Opened += (_, _) => Opacity = 1;
        }

        private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        public void SetStage(string stage, int stepIndex, int totalSteps, string? currentAction = null)
        {
            StageText.Text = stage;
            StepProgressBar.Value = totalSteps > 0 ? (double)stepIndex / totalSteps * 100 : 0;
            if (currentAction != null)
                CurrentActionText.Text = currentAction;
        }

        public void AppendLine(string line)
        {
            // Раньше при каждой строке пересобирался весь список (O(n²)) — теперь добавляется только новая строка
            _items.Add(LogColorHelper.BuildDisplayLine(line, _defaultTextBrush, true, true));

            if (_items.Count > MaxVisibleLines)
                _items.RemoveAt(0);

            CurrentActionText.Text = line;

            if (_scrollPending)
                return;

            _scrollPending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _scrollPending = false;
                LogScrollViewer.ScrollToEnd();
            }, DispatcherPriority.Background);
        }

        public async Task CompleteSuccessAsync()
        {
            StepProgressBar.Value = 100;
            await Task.Delay(900);
            Close();
        }

        public void MarkFailed(string stage)
        {
            StageText.Text = stage;
            StageText.Foreground = new SolidColorBrush(Color.Parse("#E05050"));
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
    }
}