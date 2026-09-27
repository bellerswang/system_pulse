using System.Windows;
using System.Windows.Input;
using System.Text.Json;
using XinweiManager.ViewModels;

namespace XinweiManager;

public partial class FloatingShortcutWindow : Window
{
    private readonly Action _openMain;
    private readonly string _positionFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SystemPulse", "floating-position.json");
    private System.Windows.Point _pointerDownAt;
    private bool _pointerDown;
    private bool _dragStarted;
    private bool _suppressClick;
    private bool _positionInitialized;

    public FloatingShortcutWindow(Action openMain, MainViewModel viewModel)
    {
        InitializeComponent();
        _openMain = openMain;
        DataContext = viewModel;
    }

    public void MoveToWorkArea()
    {
        if (_positionInitialized) return;
        _positionInitialized = true;

        if (TryRestorePosition()) return;

        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - Width - 20);
        Top = Math.Max(area.Top, area.Bottom - Height - 20);
    }

    private bool TryRestorePosition()
    {
        try
        {
            if (!File.Exists(_positionFile)) return false;
            var saved = JsonSerializer.Deserialize<FloatingPosition>(File.ReadAllText(_positionFile));
            if (saved is null || !double.IsFinite(saved.Left) || !double.IsFinite(saved.Top)) return false;

            var left = SystemParameters.VirtualScreenLeft;
            var top = SystemParameters.VirtualScreenTop;
            var right = left + SystemParameters.VirtualScreenWidth;
            var bottom = top + SystemParameters.VirtualScreenHeight;
            const double visibleEdge = 48;
            if (saved.Left + Width < left + visibleEdge || saved.Left > right - visibleEdge ||
                saved.Top + Height < top + visibleEdge || saved.Top > bottom - visibleEdge)
                return false;

            Left = saved.Left;
            Top = saved.Top;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void Floating_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _pointerDownAt = e.GetPosition(this);
        _pointerDown = true;
        _dragStarted = false;
        _suppressClick = false;
        CaptureMouse();
        e.Handled = true;
    }

    private void Floating_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_pointerDown || e.LeftButton != MouseButtonState.Pressed || _dragStarted) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _pointerDownAt.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _pointerDownAt.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragStarted = true;
        _suppressClick = true;
        if (IsMouseCaptured) ReleaseMouseCapture();
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // A fast release can race the native window drag loop; keep the click suppressed.
        }
        finally
        {
            _pointerDown = false;
            SavePosition();
        }
    }

    private void Floating_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (_suppressClick)
        {
            _suppressClick = false;
            _pointerDown = false;
            e.Handled = true;
            return;
        }

        if (_pointerDown && e.ChangedButton == MouseButton.Left)
        {
            _pointerDown = false;
            _openMain();
            e.Handled = true;
        }
    }

    private void SavePosition()
    {
        try
        {
            var folder = Path.GetDirectoryName(_positionFile)!;
            Directory.CreateDirectory(folder);
            File.WriteAllText(_positionFile, JsonSerializer.Serialize(new FloatingPosition(Left, Top)));
        }
        catch (Exception ex)
        {
            Services.LocalLog.Error("floating window position", ex);
        }
    }

    private sealed record FloatingPosition(double Left, double Top);
}
