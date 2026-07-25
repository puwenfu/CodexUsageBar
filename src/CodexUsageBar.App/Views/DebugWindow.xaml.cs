using System.Windows;
using System.Windows.Input;
using CodexUsageBar.App.ViewModels;

namespace CodexUsageBar.App.Views;

public partial class DebugWindow : Window
{
    public DebugWindow(DebugViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs eventArgs) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape)
        {
            Close();
        }
    }
}
