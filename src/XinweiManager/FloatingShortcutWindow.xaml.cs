using System.Windows;
using System.Windows.Input;
using XinweiManager.ViewModels;

namespace XinweiManager;

public partial class FloatingShortcutWindow : Window
{
    private readonly Action _openMain;

    public FloatingShortcutWindow(Action openMain, MainViewModel viewModel)
    {
        InitializeComponent();
        _openMain = openMain;
        DataContext = viewModel;
    }

    public void MoveToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - Width - 20);
        Top = Math.Max(area.Top, area.Bottom - Height - 20);
    }

    private void Shortcut_Click(object sender, MouseButtonEventArgs e)
    {
        _openMain();
        e.Handled = true;
    }
}
