using System.IO;
using System.Text.Json;
using System.Threading;

namespace StuOaMonitor;

/// <summary>
/// OA 业务服务层，纯业务逻辑，无 GUI、无 tkinter、无托盘依赖。
/// 回调：OnNewItems / OnStatusChanged / OnItemsReady
/// </summary>
public class OaService
{
    private static readonly Logger _logger = AppLog.GetLogger("stu-oa.service");

    public int Days { get; set; }
    public int Interval { get; set; }
    public string? EarliestDate { get; set; }
    public string OutDir { get; set; }

    public Action<List<OaItem>>? OnNewItems { get; set; }
    public Action<string>? OnStatusChanged { get; set; }
    public Action<List<OaItem>>? OnItemsReady { get; set; }

    public HashSet<string> SeenUrls { get; } = new();
    public List<OaItem> LatestItems { get; private set; } = new();

    private readonly object _checkLock = new();
    private readonly CancellationTokenSource _stopCts = new();

    public OaService(int days, int interval, string? earliestDate, string outDir)
    {
        Days = days;
        Interval = interval;
        EarliestDate = earliestDate;
        OutDir = outDir;

        LoadExistingUrls();
    }

    public bool IsStopped => _stopCts.IsCancellationRequested;
    public void Stop() => _stopCts.Cancel();

    /// <summary>从 meta/*.json 与 html/*.html 加载已抓取 url 集合。</summary>
    private void LoadExistingUrls()
    {
        OnStatusChanged?.Invoke("正在加载本地已抓取记录");
        if (!Directory.Exists(OutDir)) return;

        var metaDir = Path.Combine(OutDir, "meta");
        if (Directory.Exists(metaDir))
        {
            foreach (var fname in Directory.GetFiles(metaDir).OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!fname.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(fname));
                    if (list != null)
                        foreach (var u in list)
                            SeenUrls.Add(u);
                }
                catch { /* 忽略损坏文件 */ }
            }
        }

        // index.json 保留读取（不使用）
        var indexPath = Path.Combine(OutDir, "index.json");
        if (File.Exists(indexPath))
        {
            try { _ = File.ReadAllText(indexPath); } catch { }
        }

        var htmlDir = Path.Combine(OutDir, "html");
        if (Directory.Exists(htmlDir))
        {
            foreach (var fname in Directory.GetFiles(htmlDir))
            {
                if (!fname.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var content = File.ReadAllText(fname);
                    foreach (var u in Renderer.ExtractUrls(content))
                        SeenUrls.Add(u);
                }
                catch { }
            }
        }
    }

    /// <summary>初始化种子抓取：程序启动首次抓取，补全历史、标记已读，写入本地文件。</summary>
    public async Task SeedAsync()
    {
        OnStatusChanged?.Invoke("正在初始化抓取……");
        await RunCheckCoreAsync().ConfigureAwait(false);
    }

    /// <summary>单次执行检查业务逻辑。</summary>
    public async Task CheckOnceAsync()
    {
        OnStatusChanged?.Invoke("正在检查……");
        await RunCheckCoreAsync().ConfigureAwait(false);
    }

    private async Task RunCheckCoreAsync()
    {
        List<OaItem> items;
        try
        {
            items = await OaFetcher.FetchNewsListAsync(Days, EarliestDate).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // 网络问题：保留旧 latest_items，并触发 GUI 恢复稳态
            _logger.Error($"单次检查抓取异常: {e.Message}");
            OnItemsReady?.Invoke(LatestItems);
            return;
        }

        List<OaItem> newItems;
        lock (_checkLock)
        {
            newItems = items
                .Where(it => !string.IsNullOrEmpty(it.Url) && !SeenUrls.Contains(it.Url!))
                .ToList();
            foreach (var it in newItems)
                SeenUrls.Add(it.Url!);
            LatestItems = items;
        }

        OnItemsReady?.Invoke(LatestItems);   // 无新消息也刷新 GUI

        if (newItems.Count > 0)
            OnNewItems?.Invoke(newItems);

        var newDates = newItems
            .Where(x => !string.IsNullOrEmpty(x.Date))
            .Select(x => x.Date!)
            .ToHashSet();
        WriteDailyFiles(items, newDates);
    }

    /// <summary>后台轮询线程。</summary>
    public Task StartBackgroundLoopAsync()
    {
        return Task.Run(async () =>
        {
            while (!_stopCts.IsCancellationRequested)
            {
                try { await CheckOnceAsync().ConfigureAwait(false); }
                catch (Exception e) { _logger.Error($"轮询检查异常: {e.Message}"); }

                for (var i = 0; i < Interval; i++)
                {
                    if (_stopCts.IsCancellationRequested) return;
                    try { await Task.Delay(1000, _stopCts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
            }
        });
    }

    private void WriteDailyFiles(List<OaItem> items, HashSet<string> onlyDates)
    {
        if (onlyDates.Count == 0) return;
        var groups = Renderer.GroupByDate(items);
        foreach (var kv in groups)
        {
            if (!onlyDates.Contains(kv.Key)) continue;
            try { Renderer.RenderDay(kv.Key, kv.Value, OutDir); }
            catch (Exception e)
            {
                _logger.Warning($"写入 {kv.Key} html 与 json 文件失败：{e.Message}");
            }
        }
    }
}