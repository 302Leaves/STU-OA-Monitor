using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace StuOaMonitor;

/// <summary>
/// GUI 层：全部界面、托盘交互、表格 UI 逻辑；不实现抓取/轮询业务。
/// 依赖外部传入 OaService 业务服务实例。
/// </summary>
public partial class MainWindow : Window
{
    private static readonly Logger _logger = AppLog.GetLogger("stu-oa.gui");

    private readonly OaService _service;
    private readonly DesktopNotificationService _notification;
    private readonly TrayApp _tray;

    private string? _earliestDate;
    private SettingsWindow? _settingsWindow;
    private bool _allowClose;

    private int _checkInProgress;

    public MainWindow(OaService service)
    {
        InitializeComponent();
        _service = service;

        var cfg = Config.Load();
        _earliestDate = NormalizeDateString(Config.GetString(cfg, "earliest_date", ""));

        _notification = new DesktopNotificationService();

        Title = $"汕头大学 OA 通知汇总 {Version.Display}";

        // 将回调绑定到 UI Dispatcher —— 后台线程 → 主线程
        _service.OnNewItems = items =>
            Dispatcher.BeginInvoke(new Action(() => HandleNewItems(items)));
        _service.OnStatusChanged = text =>
            Dispatcher.BeginInvoke(new Action(() => StatusText.Text = text));
        _service.OnItemsReady = _ =>
            Dispatcher.BeginInvoke(new Action(RefreshGui));

        _tray = new TrayApp(
            tooltip: "OA 通知监控",
            onClick: () => Dispatcher.BeginInvoke(new Action(ShowMainWindow)),
            onQuit: () => Dispatcher.BeginInvoke(new Action(RequestQuit)),
            onMenuCheck: () => Dispatcher.BeginInvoke(new Action(RequestCheck)));

        _notification.Backend = _tray;

        StatusText.Text = $"就绪 · 范围：{GetRangeText()}";
    }

    // ---------- 日期 / 范围工具 ----------
    private static string? NormalizeDateString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // 把各种奇葩破折号替换为 ASCII '-'
        var text = raw
            .Replace('\u2011', '-')
            .Replace('\u2013', '-')
            .Replace('\u2014', '-')
            .Replace('\u2212', '-');
        return DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd")
            : null;
    }

    private string GetRangeText()
        => string.IsNullOrEmpty(_earliestDate)
            ? $"最近 {_service.Days} 天"
            : $"{_earliestDate} 起";

    // ---------- UI 刷新 ----------
    private void RefreshGui()
    {
        var items = _service.LatestItems;
        ItemsGrid.ItemsSource = items.ToList();   // 复制以避免潜在并发修改
        StatusText.Text =
            $"最近更新 {DateTime.Now:HH:mm:ss} · 共 {items.Count} 条 · 范围：{GetRangeText()}";
    }

    private void HandleNewItems(List<OaItem> newItems)
    {
        RefreshGui();
        _notification.NotifyNewOaItems(newItems);
    }

    private void ShowMainWindow()
    {
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void RequestCheck()
    {
        if (Interlocked.CompareExchange(ref _checkInProgress, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try { await _service.CheckOnceAsync().ConfigureAwait(false); }
            catch (Exception e) { _logger.Error($"check_once 异常: {e.Message}"); }
            finally { Interlocked.Exchange(ref _checkInProgress, 0); }
        });
    }

    private void RequestQuit()
    {
        _service.Stop();
        _allowClose = true;
        try { _tray.Dispose(); } catch { }
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();          // 关闭 → 隐藏到托盘
            return;
        }
        base.OnClosing(e);
    }

    // ---------- 工具栏按钮 ----------
    private void OnTestNotification(object sender, RoutedEventArgs e)
    {
        var ok = _notification.NotifyTest();
        StatusText.Text = ok
            ? $"已发送测试通知 · {DateTime.Now:HH:mm:ss}"
            : "测试通知发送失败（托盘未就绪或系统通知被关闭）";

        // 2500ms 后自动恢复稳态状态栏
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RefreshGui();
        };
        timer.Start();
    }

    private void OnCheckNow(object sender, RoutedEventArgs e) => RequestCheck();
    private void OnHideToTray(object sender, RoutedEventArgs e) => Hide();

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow != null && _settingsWindow.IsLoaded)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(this, _service, _earliestDate)
        {
            Owner = this,
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.ShowDialog();
    }

    /// <summary>由 SettingsWindow 回调：保存后重新检查。</summary>
    public void ApplySettings(string? newEarliestDate)
    {
        _earliestDate = newEarliestDate;
        var cfg = Config.Load();
        Config.SetString(cfg, "earliest_date", newEarliestDate ?? "");
        Config.Save(cfg);
        StatusText.Text = $"设置已保存（{GetRangeText()}），正在重新检查……";
        RequestCheck();
    }

    // ---------- 表格交互 ----------
    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // ── 1. 找 DataGridCell（Visual 树里向上）
        DependencyObject? src = e.OriginalSource as DependencyObject;
        DataGridCell? cell = null;
        while (src != null)
        {
            if (src is DataGridCell c) { cell = c; break; }
            src = (src is Visual || src is System.Windows.Media.Media3D.Visual3D)
                ? VisualTreeHelper.GetParent(src)
                : LogicalTreeHelper.GetParent(src);
        }

        if (cell == null) return;

        // ── 2. 只处理「标题」列（DisplayIndex == 0）
        if (cell.Column.DisplayIndex != 0) return;

        // ── 3. 找 DataGridRow → 取 Item（比 SelectedItem 可靠）
        DependencyObject? cur = cell;
        DataGridRow? row = null;
        while (cur != null)
        {
            if (cur is DataGridRow r) { row = r; break; }
            cur = VisualTreeHelper.GetParent(cur);
        }

        if (row?.Item is not OaItem item || string.IsNullOrEmpty(item.Url)) return;

        // ── 4. 打开链接
        try
        {
            Process.Start(new ProcessStartInfo(item.Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Warning($"打开链接失败: {ex.Message}");
        }
    }
}