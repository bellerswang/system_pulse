using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using XinweiManager.Models;
using XinweiManager.ViewModels;

namespace XinweiManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _hardwareLoaded;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.ConfirmClean = ConfirmClean;
        SourceInitialized += (_, _) => FitToWorkArea();
    }

    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(MinWidth, area.Width - 24));
        Height = Math.Min(Height, Math.Max(MinHeight, area.Height - 24));
        Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
        Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
    }

    private void Minimise_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximise_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (MaximiseButton is null) return;
        MaximiseButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        MaximiseButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximise";
    }

    private bool ConfirmClean(CleanupScanResult result)
    {
        var dialog = new ConfirmCleanupWindow(result) { Owner = this };
        return dialog.ShowDialog() == true;
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!((App)System.Windows.Application.Current).IsExiting) { e.Cancel = true; Hide(); }
    }

    private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        _viewModel.WindowVisible = IsVisible;

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is System.Windows.Controls.TabControl tabs && tabs.SelectedIndex == 2 && !_hardwareLoaded)
        {
            _hardwareLoaded = true;
            _ = _viewModel.RefreshHardwareAsync();
        }
    }
}
