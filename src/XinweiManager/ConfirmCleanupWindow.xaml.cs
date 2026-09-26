using System.Windows;
using XinweiManager.Models;
using XinweiManager.ViewModels;

namespace XinweiManager;

public partial class ConfirmCleanupWindow : Window
{
    public ConfirmCleanupWindow(CleanupScanResult result)
    {
        InitializeComponent();
        Summary.Text = $"Permanently delete {result.Candidates.Count:N0} eligible temporary file(s) " +
            $"(estimated {MainViewModel.FormatBytes(result.EstimatedBytes)})?\n\nThis action cannot be undone.";
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
