using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using GrammarBubble.App.Interop;
using GrammarBubble.App.Services;
using GrammarBubble.App.Windows;
using Application = System.Windows.Application;

namespace GrammarBubble.App;

public partial class App : Application
{
    private const int HotkeyId = 0x6742;
    private Mutex? _singleInstance;
    private BubbleWindow? _bubble;
    private NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "GrammarBubble.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        _bubble = new BubbleWindow();
        _bubble.Park();
        _bubble.Show();
        var handle = new WindowInteropHelper(_bubble).EnsureHandle();
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);

        bool registered = Native.RegisterHotKey(handle, HotkeyId, Native.ModControl | Native.ModAlt | Native.ModNoRepeat, 0x47);
        BuildTray(registered);

        if (e.Args.Contains("--settings"))
            Dispatcher.BeginInvoke(new Action(_bubble.OpenSettings), System.Windows.Threading.DispatcherPriority.Background);
        else if (e.Args.Contains("--help"))
            Dispatcher.BeginInvoke(new Action(_bubble.OpenHelp), System.Windows.Threading.DispatcherPriority.Background);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _ = OnHotkeyAsync();
        }
        return IntPtr.Zero;
    }

    private async Task OnHotkeyAsync()
    {
        try
        {
            var foreground = Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(foreground, out uint foregroundPid);
            bool insideBubble = foregroundPid == Environment.ProcessId;

            if (insideBubble && _bubble is not null && _bubble.IsOpen && !_bubble.IsCompact)
            {
                _bubble.Hide();
                return;
            }

            var captured = await TextCapture.CaptureAsync();
            _bubble?.OpenWith(captured);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("Grammar Reviewer: " + ex.Message, "Grammar Reviewer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BuildTray(bool hotkeyRegistered)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "icon.ico");
        var menu = new ContextMenuStrip();
        menu.Items.Add("Mostrar globo", null, (_, _) => _bubble?.Toggle());
        menu.Items.Add("Escribir o pegar texto…", null, (_, _) => _bubble?.OpenWriter());
        menu.Items.Add("Chat con los modelos…", null, (_, _) => _bubble?.OpenChat());
        menu.Items.Add("Configurar claves de API…", null, (_, _) => _bubble?.OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Shutdown());

        _tray = new NotifyIcon
        {
            Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application,
            Text = "Grammar Reviewer",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => _bubble?.Toggle();

        _tray.ShowBalloonTip(
            4000,
            "Grammar Reviewer",
            hotkeyRegistered
                ? "Selecciona texto y pulsa Ctrl+Alt+G para revisarlo."
                : "No se pudo registrar Ctrl+Alt+G (otra app lo usa). Usa el icono de la bandeja.",
            hotkeyRegistered ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_bubble is not null)
        {
            var handle = new WindowInteropHelper(_bubble).Handle;
            Native.UnregisterHotKey(handle, HotkeyId);
        }
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
