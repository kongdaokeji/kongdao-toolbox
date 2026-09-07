using System.Drawing;
using System.Windows.Forms;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public static class TrayIconService
{
    private static NotifyIcon? _notifyIcon;
    private static bool _isInitialized;
    private static string _toolName = "游戏防晕3D";
    private static Action? _stopAction;
    private static Action? _restoreAction;

    public static bool IsVisible => _notifyIcon?.Visible == true;

    public static void Show(
        string toolName = "游戏防晕3D",
        Action? stopAction = null,
        Action? restoreAction = null)
    {
        _toolName = toolName;
        _stopAction = stopAction ?? AntiMotionSicknessOverlay.CloseOverlay;
        _restoreAction = restoreAction;

        if (_isInitialized && _notifyIcon is not null)
        {
            _notifyIcon.Text = $"{_toolName}：已最小化到系统托盘";
            _notifyIcon.Visible = true;
            return;
        }

        _notifyIcon = new NotifyIcon
        {
            Icon = GetIcon(),
            Text = $"{_toolName}：已最小化到系统托盘",
            Visible = true
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("打开主窗口", null, (_, _) => RestoreMainWindow());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("停止并退出", null, (_, _) => StopAndExit());
        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (_, _) => RestoreMainWindow();

        _isInitialized = true;
    }

    public static void Hide()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
        }
    }

    public static void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
            _isInitialized = false;
        }

        _stopAction = null;
        _restoreAction = null;
    }

    private static void RestoreMainWindow()
    {
        if (App.MainWindow is not { } mainWindow) return;

        mainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            App.IsLiteMode = false;
            try
            {
                _restoreAction?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TrayIcon] Restore failed: {ex.Message}");
            }

            Hide();
            mainWindow.AppWindow.Show();
            mainWindow.Activate();
        });
    }

    private static void StopAndExit()
    {
        App.IsLiteMode = false;
        try
        {
            _stopAction?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TrayIcon] Stop failed: {ex.Message}");
        }

        Dispose();
        Microsoft.UI.Xaml.Application.Current.Exit();
        System.Diagnostics.Process.GetCurrentProcess().Kill();
    }

    private static Icon GetIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
            return new Icon(iconPath);

        return (Icon)SystemIcons.Application.Clone();
    }
}
