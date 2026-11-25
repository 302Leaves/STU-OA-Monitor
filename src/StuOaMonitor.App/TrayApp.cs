using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace StuOaMonitor;

/// <summary>
/// Windows 系统托盘封装（纯 WPF 实现，不依赖 WinForms）。
/// 托盘图标、右键菜单、Windows 10+ Toast/气泡通知；回调投递事件给上层。
/// 实现 Core 的 INotificationBackend 协议，供 DesktopNotificationService 注入。
/// </summary>
public class TrayApp : INotificationBackend, IDisposable
{
    private readonly Logger _logger = AppLog.GetLogger("stu-oa.tray");
    private readonly TaskbarIcon _tray;
    private bool _disposed;

    public TrayApp(string tooltip, Action onClick, Action onQuit, Action? onMenuCheck = null)
    {
        // ---- 右键菜单（纯 WPF ContextMenu）----
        var menu = new ContextMenu();

        var showItem = new MenuItem { Header = "显示" };
        showItem.Click += (_, _) => SafeInvoke(onClick, "on_click");
        menu.Items.Add(showItem);

        var checkItem = new MenuItem { Header = "立即检查" };
        checkItem.Click += (_, _) => SafeInvoke(onMenuCheck, "on_menu_check");
        menu.Items.Add(checkItem);

        menu.Items.Add(new Separator());

        var quitItem = new MenuItem { Header = "退出" };
        quitItem.Click += (_, _) => SafeInvoke(onQuit, "on_quit");
        menu.Items.Add(quitItem);

        // ---- 托盘本体 ----
        _tray = new TaskbarIcon
        {
            IconSource = LoadTrayIcon(),
            ToolTipText = tooltip.Length > 63 ? tooltip[..63] : tooltip,
            ContextMenu = menu,
            NoLeftClickDelay = true,
        };

        // 点击 / 双击 / 点击通知，均触发"显示窗口"
        _tray.TrayLeftMouseUp += (_, _) => SafeInvoke(onClick, "on_click");
        _tray.TrayMouseDoubleClick += (_, _) => SafeInvoke(onClick, "on_click");
        _tray.TrayBalloonTipClicked += (_, _) => SafeInvoke(onClick, "on_click");

        // 立即创建托盘图标（不依赖加入可视化树）
        _tray.ForceCreate();
    }

    /// <summary>
    /// 加载托盘图标：优先用嵌入的 app.ico；失败则退化为系统默认图标。
    /// </summary>
    private ImageSource? LoadTrayIcon()
    {
        // 优先：嵌入资源 app.ico（pack URI）
        try
        {
            var img = new BitmapImage(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
            img.Freeze(); // 冻结，确保跨线程安全
            return img;
        }
        catch
        {
            // 如果 app.ico 不存在或加载失败，直接返回 null
            // TaskbarIcon 会使用其内置的默认图标，避免我们手动创建 InteropBitmap
            _logger.Warning("未能加载 app.ico，使用默认托盘图标。");
            return null;
        }
    }

    private void SafeInvoke(Action? cb, string label)
    {
        try { cb?.Invoke(); }
        catch (Exception e) { _logger.Warning($"callback error ({label}): {e.Message}"); }
    }

    /// <summary>显示气泡 / Toast 通知（Win10/11 以系统 Toast 呈现）。</summary>
    public bool ShowBalloon(string title, string message)
    {
        if (_disposed) return false;
        try
        {
            _tray.ShowNotification(
                title ?? "",
                message ?? "",
                NotificationIcon.Info);
            return true;
        }
        catch (Exception e)
        {
            _logger.Warning($"show_balloon 异常: {e.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _tray.Dispose(); } catch { }
    }

    private static class NativeMethods
    {
        public const int IDI_APPLICATION = 32512;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);
    }
}