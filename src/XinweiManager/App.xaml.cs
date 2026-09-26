using System.Drawing;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Forms;
using XinweiManager.Services;
using XinweiManager.ViewModels;
using Application = System.Windows.Application;

namespace XinweiManager;

public partial class App : Application
{
    private const string PipeName = "SystemPulse.Session";
    private Mutex? _singleInstance;
    private CancellationTokenSource? _pipeLife;
    private NotifyIcon? _tray;
    private Icon? _icon;
    private MainWindow? _main;
    private FloatingShortcutWindow? _floating;
    private MainViewModel? _viewModel;
    private ToolStripMenuItem? _startupMenu;
    private ToolStripMenuItem? _floatingMenu;
    private bool _floatingEnabled = true;
    public bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture =
            System.Globalization.CultureInfo.GetCultureInfo("en-GB");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture =
            System.Globalization.CultureInfo.GetCultureInfo("en-GB");

        _singleInstance = new Mutex(true, @"Local\SystemPulse", out bool created);
        if (!created)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out,
                    PipeOptions.CurrentUserOnly);
                client.Connect(1500);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine("show");
            }
            catch (Exception ex) { LocalLog.Error("single instance", ex); }
            Shutdown();
            return;
        }

        _pipeLife = new CancellationTokenSource();
        _ = ListenForSecondInstanceAsync(_pipeLife.Token);
        var startup = new StartupService();
        _viewModel = new MainViewModel(new SystemMetricsService(), new SensorService(),
            new ProcessMetricsService(), new HardwareInventoryService(),
            CleanupService.CreateDefault(), new LargeFileService(), startup);
        _main = new MainWindow(_viewModel);
        MainWindow = _main;
        _floating = new FloatingShortcutWindow(ShowMain, _viewModel);
        _main.IsVisibleChanged += (_, _) => UpdateFloatingShortcut();
        _main.StateChanged += (_, _) => UpdateFloatingShortcut();
        CreateTray();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.StartupEnabled) && _startupMenu is not null)
                _startupMenu.Checked = _viewModel.StartupEnabled;
        };
        if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase)) ShowMain();
        else UpdateFloatingShortcut();
    }

    private async Task ListenForSecondInstanceAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(token);
                using var reader = new StreamReader(server);
                if (await reader.ReadLineAsync(token) == "show")
                    await Dispatcher.InvokeAsync(ShowMain);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { LocalLog.Error("single instance listener", ex); }
        }
    }

    private void CreateTray()
    {
        _icon = CreateIcon();
        _startupMenu = new ToolStripMenuItem("Launch at sign-in")
        { Checked = _viewModel!.StartupEnabled, Enabled = _viewModel.StartupAvailable, CheckOnClick = false };
        _startupMenu.Click += (_, _) => _viewModel.ToggleStartupCommand.Execute(null);
        _floatingMenu = new ToolStripMenuItem("Floating shortcut")
        { Checked = _floatingEnabled, CheckOnClick = false };
        _floatingMenu.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            _floatingEnabled = !_floatingEnabled;
            _floatingMenu.Checked = _floatingEnabled;
            UpdateFloatingShortcut();
        });
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open System Pulse", null, (_, _) => Dispatcher.Invoke(ShowMain));
        menu.Items.Add(_floatingMenu);
        menu.Items.Add(_startupMenu);
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitApp));
        _tray = new NotifyIcon { Icon = _icon, Text = "System Pulse", Visible = true, ContextMenuStrip = menu };
        _tray.MouseClick += (_, args) => { if (args.Button == MouseButtons.Left) Dispatcher.Invoke(ShowMain); };
    }

    private static Icon CreateIcon()
    {
        var uri = new Uri("/SystemPulse;component/Assets/Brand/SystemPulse.ico", UriKind.Relative);
        var resource = GetResourceStream(uri)
            ?? throw new InvalidOperationException("The application icon is missing.");
        using (resource.Stream)
        using (var icon = new Icon(resource.Stream))
            return (Icon)icon.Clone();
    }

    private void ShowMain()
    {
        if (_main is null) return;
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
        UpdateFloatingShortcut();
    }

    private void UpdateFloatingShortcut()
    {
        if (_floating is null || _main is null || IsExiting) return;
        var shouldShow = _floatingEnabled &&
            (!_main.IsVisible || _main.WindowState == WindowState.Minimized);
        if (shouldShow)
        {
            _floating.MoveToWorkArea();
            _floating.Show();
        }
        else _floating.Hide();
    }

    private void ExitApp()
    {
        IsExiting = true;
        _floating?.Close();
        _main?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeLife?.Cancel();
        _tray?.Dispose();
        _icon?.Dispose();
        _viewModel?.Dispose();
        _singleInstance?.Dispose();
        _pipeLife?.Dispose();
        base.OnExit(e);
    }
}
