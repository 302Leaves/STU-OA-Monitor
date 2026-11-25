namespace StuOaMonitor;

/// <summary>数据模型：OA 通知条目</summary>
public class OaItem
{
    public string? Date { get; set; }
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string? Department { get; set; }
}

/// <summary>通知后端协议</summary>
public interface INotificationBackend
{
    bool ShowBalloon(string title, string message);
}

/// <summary>
/// 桌面通知统一服务，上层业务/GUI 只调用本类。
/// 底部通过后端隔离（托盘 / WinRT Toast / mock 空实现）。
/// </summary>
public class DesktopNotificationService
{
    public const int MaxTitle = 63;
    public const int MaxBody = 255;

    private readonly Logger _logger = AppLog.GetLogger("stu-oa.notification");
    private INotificationBackend? _backend;

    public INotificationBackend? Backend
    {
        get => _backend;
        set
        {
            _backend = value;
            _logger.Debug(value == null
                ? "通知后端已置为 None，通知将被禁用"
                : "通知后端已设置完毕");
        }
    }

    public bool IsAvailable => _backend != null;

    public bool Show(string title, string message)
    {
        if (!IsAvailable)
        {
            _logger.Debug("通知后端未就绪，跳过通知");
            return false;
        }

        var rawTitle = title ?? "";
        var rawMsg = message ?? "";
        var safeTitle = rawTitle.Trim();
        var safeMsg = rawMsg.Trim();
        if (safeTitle.Length > MaxTitle) safeTitle = safeTitle[..MaxTitle];
        if (safeMsg.Length > MaxBody) safeMsg = safeMsg[..MaxBody];

        if (rawTitle.Length > MaxTitle)
            _logger.Debug($"通知标题被截断，原始长度{rawTitle.Length}，截断至{MaxTitle}字符");
        if (rawMsg.Length > MaxBody)
            _logger.Debug($"通知内容被截断，原始长度{rawMsg.Length}，截断至{MaxBody}字符");

        try
        {
            var ok = _backend!.ShowBalloon(safeTitle, safeMsg);
            if (ok) _logger.Info($"桌面通知已发送|title:{safeTitle}|msg:{safeMsg}");
            else _logger.Warning($"桌面通知底层调用返回失败|title:{safeTitle}");
            return ok;
        }
        catch (Exception e)
        {
            _logger.Warning($"发送桌面通知发生异常: {e.Message}");
            return false;
        }
    }

    public bool NotifyNewOaItems(List<OaItem> items)
    {
        if (items == null || items.Count == 0)
        {
            _logger.Debug("notify_new_oa_items 函数传入空条目列表,不弹出通知");
            return false;
        }
        const string title = "OA 新通知";
        var body = items.Count == 1
            ? (items[0].Title ?? "")
            : $"{(items[0].Title ?? "")} 等 {items.Count} 条";
        _logger.Debug($"准备触发OA新通知，共{items.Count}条");
        return Show(title, body);
    }

    public bool NotifyTest()
    {
        _logger.Debug("触发测试桌面通知");
        return Show("OA 通知监控 · 测试", "这是一条测试通知。点击可打开窗口。");
    }
}