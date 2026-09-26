using System.Windows;
using System.Windows.Input;

namespace XinweiManager;

public partial class FloatingShortcutWindow : Window
{
    private readonly Action _openMain;

    public FloatingShortcutWindow(Action openMain)
    {
        InitializeComponent();
        _openMain = openMain;
    }

    public void MoveToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 20;
        Top = area.Bottom - Height - 20;
    }

    private void Shortcut_Click(object sender, MouseButtonEventArgs e)
    {
        _openMain();
        e.Handled = true;
    }
}
