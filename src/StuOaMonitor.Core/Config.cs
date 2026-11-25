using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StuOaMonitor;

/// <summary>
/// 配置读写模块
/// 负责加载与保存 config/config.json；管理 earliest_date 配置项。
/// 文件缺失、权限错误、JSON 损坏时自动返回默认配置；支持保留配置未知 key。
/// </summary>
public static class Config
{
    public const string ConfigDir = "config";
    public static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static Dictionary<string, JsonNode?> Defaults() => new()
    {
        ["earliest_date"] = "",   // YYYY-MM-DD；空字符串表示按 days 回退
        ["log_level"] = "INFO",   // DEBUG / INFO / WARNING / ERROR / CRITICAL
    };

    /// <summary>加载配置，合并默认值，保留未知 key。</summary>
    public static Dictionary<string, JsonNode?> Load()
    {
        var cfg = Defaults();
        try
        {
            if (File.Exists(ConfigPath))
            {
                var text = File.ReadAllText(ConfigPath);
                if (JsonNode.Parse(text) is JsonObject root)
                {
                    foreach (var kv in root)
                        cfg[kv.Key] = kv.Value?.DeepClone();
                }
            }
        }
        catch (Exception) { /* 文件缺失/权限/JSON 损坏：回退默认 */ }
        return cfg;
    }

    /// <summary>保存配置到 config/config.json（自动创建 config 目录）。</summary>
    public static void Save(Dictionary<string, JsonNode?>? cfg)
    {
        var data = Defaults();
        if (cfg != null)
            foreach (var kv in cfg)
                data[kv.Key] = kv.Value;

        Directory.CreateDirectory(ConfigDir);
        var obj = new JsonObject();
        foreach (var kv in data)
            obj[kv.Key] = kv.Value;

        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        File.WriteAllText(ConfigPath, obj.ToJsonString(opts));
    }

    public static string GetString(Dictionary<string, JsonNode?> cfg, string key, string defaultValue = "")
    {
        if (cfg.TryGetValue(key, out var node) && node != null)
        {
            try { return node.GetValue<string>() ?? defaultValue; }
            catch { return defaultValue; }
        }
        return defaultValue;
    }

    public static void SetString(Dictionary<string, JsonNode?> cfg, string key, string value)
        => cfg[key] = JsonValue.Create(value);
}