using System.Globalization;
using System.IO;
using System.Windows;

namespace StuOaMonitor;

/// <summary>
/// 程序入口：仅做命令行解析、配置加载、日志初始化、组装 Service 与 GUI。
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = Environment.GetCommandLineArgs().Skip(1).ToList();

        // Windows cmd /? 兼容
        if (args.Contains("/?"))
        {
            args.Remove("/?");
            args.Add("--help");
        }

        int days = 3, interval = 600;
        var outDir = "oa_daily";
        string? logLevelStr = null;
        var showHelp = false;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--days":
                    if (i + 1 < args.Count && int.TryParse(args[++i], out var d)) days = d;
                    break;
                case "--interval":
                    if (i + 1 < args.Count && int.TryParse(args[++i], out var iv)) interval = iv;
                    break;
                case "--out":
                    if (i + 1 < args.Count) outDir = args[++i];
                    break;
                case "--loglevel":
                    if (i + 1 < args.Count) logLevelStr = args[++i];
                    break;
            }
        }

        if (showHelp)
        {
            MessageBox.Show(
                "OA 通知监控 - 托盘应用\n\n" +
                "用法：StuOaMonitor.exe [选项]\n\n" +
                "  --days <n>          未设置最早日期时，抓取最近多少天（默认 3）\n" +
                "  --interval <sec>    检查间隔秒数（默认 600）\n" +
                "  --out <dir>         HTML 输出目录（默认 ./oa_daily）\n" +
                "  --loglevel <lvl>    DEBUG / INFO / WARNING / ERROR / CRITICAL\n" +
                "  --help, -h, /?      显示此帮助",
                "帮助");
            Shutdown();
            return;
        }

        if (days < 1) days = 1;
        if (interval < 30) interval = 30;

        var cfg = Config.Load();

        // 日志等级优先级：命令行 > 配置文件 > INFO
        string useLogLevel;
        if (logLevelStr != null)
        {
            useLogLevel = logLevelStr;
        }
        else
        {
            var cfgLevel = Config.GetString(cfg, "log_level", "INFO").ToUpperInvariant();
            useLogLevel = AppLog.LevelMap.ContainsKey(cfgLevel) ? cfgLevel : "INFO";
        }

        AppLog.Setup(AppLog.LevelMap[useLogLevel]);
        var logger = AppLog.GetLogger("stu-oa.main");
        logger.Info(new string('=', 20));
        logger.Info($"程序启动，版本{Version.Display}，日志等级[{useLogLevel}]");

        var earliestDateRaw = Config.GetString(cfg, "earliest_date", "");
        string? earliestDate = string.IsNullOrEmpty(earliestDateRaw) ? null : earliestDateRaw;
        var outDirAbs = Path.GetFullPath(outDir);

        var service = new OaService(days, interval, earliestDate, outDirAbs);

        var gui = new MainWindow(service);
        MainWindow = gui;
        gui.Show();

        // 后台启动：先 seed，再进入轮询
        _ = Task.Run(async () =>
        {
            try
            {
                logger.Debug("进入旧信息检查");
                await service.SeedAsync().ConfigureAwait(false);
                logger.Debug("进入新信息检查");
                await service.StartBackgroundLoopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Error($"后台启动异常: {ex.Message}");
            }
        });
    }
}