using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using XinweiManager.Models;
using XinweiManager.ViewModels;
using Forms = System.Windows.Forms;

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
        viewModel.PickLargeFolder = PickLargeFolder;
        viewModel.ConfirmLargeFiles = ConfirmLargeFiles;
        SourceInitialized += (_, _) => FitToWorkArea();
    }

    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        var availableWidth = Math.Max(680, area.Width - 24);
        var availableHeight = Math.Max(320, area.Height - 24);
        MinWidth = Math.Min(850, availableWidth);
        MinHeight = Math.Min(460, availableHeight);
        Width = Math.Min(1080, availableWidth);
        Height = Math.Min(800, availableHeight);
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

    private string? PickLargeFolder()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose a personal folder to search for large, old files.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private bool ConfirmLargeFiles(int count, long bytes)
    {
        var message = $"Move {count:N0} selected file(s) ({MainViewModel.FormatBytes(bytes)}) to the Recycle Bin?\n\n" +
            "You can restore them until the Recycle Bin is emptied.";
        return System.Windows.MessageBox.Show(this, message, "Confirm move to Recycle Bin",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
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
