using System.IO;
using System.Text;

namespace StuOaMonitor;

public enum LogLevel
{
    Debug = 10,
    Info = 20,
    Warning = 30,
    Error = 40,
    Critical = 50,
}

/// <summary>
/// 日志配置模块：级别常量别名，等价于 python logging.*
/// </summary>
public static class AppLog
{
    public static readonly Dictionary<string, LogLevel> LevelMap = new()
    {
        ["DEBUG"] = LogLevel.Debug,
        ["INFO"] = LogLevel.Info,
        ["WARNING"] = LogLevel.Warning,
        ["ERROR"] = LogLevel.Error,
        ["CRITICAL"] = LogLevel.Critical,
    };

    private const string LogFormat = "[{0}][{1}][{2}]\t{3}";
    private const string DateFmt = "yyyy-MM-dd HH:mm:ss";

    private static LogLevel _minLevel = LogLevel.Info;
    private static StreamWriter? _fileWriter;
    private static bool _setup;
    private static readonly object _lock = new();

    /// <summary>日志初始化，只在程序入口调用</summary>
    public static void Setup(LogLevel level, string? logFilename = "oa_monitor.log")
    {
        if (_setup) return;      // 防止重复初始化（等价于 python: if handlers: return）
        _setup = true;
        _minLevel = level;
        if (!string.IsNullOrEmpty(logFilename))
        {
            try
            {
                _fileWriter = new StreamWriter(logFilename, append: true, encoding: Encoding.UTF8)
                {
                    AutoFlush = true,
                };
            }
            catch { _fileWriter = null; }
        }
    }

    public static Logger GetLogger(string name) => new(name);

    internal static void Write(LogLevel level, string loggerName, string message)
    {
        if (level < _minLevel) return;
        var line = string.Format(LogFormat,
            DateTime.Now.ToString(DateFmt), LevelToString(level), loggerName, message);
        lock (_lock)
        {
            try { Console.WriteLine(line); } catch { }
            try { _fileWriter?.WriteLine(line); } catch { }
        }
    }

    private static string LevelToString(LogLevel l) => l switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO",
        LogLevel.Warning => "WARNING",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRITICAL",
        _ => "INFO",
    };
}

public sealed class Logger
{
    private readonly string _name;
    internal Logger(string name) { _name = name; }

    public void Debug(string msg) => AppLog.Write(LogLevel.Debug, _name, msg);
    public void Info(string msg) => AppLog.Write(LogLevel.Info, _name, msg);
    public void Warning(string msg) => AppLog.Write(LogLevel.Warning, _name, msg);
    public void Error(string msg) => AppLog.Write(LogLevel.Error, _name, msg);
    public void Critical(string msg) => AppLog.Write(LogLevel.Critical, _name, msg);
}