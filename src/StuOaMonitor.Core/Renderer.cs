using System.IO;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StuOaMonitor;

/// <summary>
/// 渲染输出模块：按日期分组 → html/*.html / meta/*.json / index.json。
/// 列表页为倒序，输出反转后由早到晚。
/// </summary>
public static class Renderer
{
    private static readonly Logger _logger = AppLog.GetLogger("stu-oa.render");

    private const string HtmlTemplate = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>$date 通知汇总</title>
<style>
  :root { color-scheme: light; }
  body { font-family: "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", sans-serif;
         margin: 24px; color: #222; }
  h1 { font-size: 20px; margin: 0 0 8px; }
  .meta { color: #666; font-size: 13px; margin-bottom: 16px; }
  table { border-collapse: collapse; width: 100%; }
  th, td { border: 1px solid #ddd; padding: 8px 12px; text-align: left; vertical-align: top; }
  th { background: #f5f5f5; font-weight: 600; }
  tbody tr:nth-child(even) { background: #fafafa; }
  a { color: #0366d6; text-decoration: none; }
  a:hover { text-decoration: underline; }
</style>
</head>
<body>
<h1>$date 通知汇总</h1>
<div class="meta">共 $count 条 · 按发布时间由早到晚排列</div>
<table>
<thead>
<tr><th style="width:72%">标题</th><th style="width:28%">发布单位</th></tr>
</thead>
<tbody>
$rows
</tbody>
</table>
</body>
</html>
""";

    private static readonly Regex HrefRe = new(
        "<a\\b[^>]*href=[\"']([^\"']+)[\"']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions MetaJsonOpt = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions IndexJsonOpt = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Dictionary<string, List<OaItem>> GroupByDate(List<OaItem> items)
    {
        var groups = new Dictionary<string, List<OaItem>>();
        foreach (var it in items)
        {
            if (string.IsNullOrEmpty(it.Date)) continue;
            if (!groups.TryGetValue(it.Date!, out var list))
            {
                list = new List<OaItem>();
                groups[it.Date!] = list;
            }
            list.Add(it);
        }
        return groups;
    }

    public static HashSet<string> ExtractUrls(string htmlText)
    {
        var set = new HashSet<string>();
        foreach (Match m in HrefRe.Matches(htmlText))
            set.Add(WebUtility.HtmlDecode(m.Groups[1].Value));
        return set;
    }

    public static string RenderDay(string dateStr, List<OaItem> items, string outputDir)
    {
        var htmlDir = Path.Combine(outputDir, "html");
        var metaDir = Path.Combine(outputDir, "meta");
        Directory.CreateDirectory(htmlDir);
        Directory.CreateDirectory(metaDir);

        var ordered = Enumerable.Reverse(items).ToList();   // 倒序 → 正序（早 → 晚）

        var rowsSb = new StringBuilder();
        foreach (var it in ordered)
        {
            var title = WebUtility.HtmlEncode(it.Title ?? "");
            var url = WebUtility.HtmlEncode(it.Url ?? "");
            var dept = WebUtility.HtmlEncode(it.Department ?? "");
            rowsSb.Append(
                $"<tr><td><a href=\"{url}\" target=\"_blank\" rel=\"noopener\">{title}</a></td>" +
                $"<td>{dept}</td></tr>\n");
        }

        var page = HtmlTemplate
            .Replace("$date", dateStr)
            .Replace("$count", ordered.Count.ToString())
            .Replace("$rows", rowsSb.ToString());

        var htmlPath = Path.Combine(htmlDir, $"{dateStr}.html");
        File.WriteAllText(htmlPath, page, Encoding.UTF8);

        var metaPath = Path.Combine(metaDir, $"{dateStr}.json");
        var metaUrls = ordered.Select(x => x.Url ?? "").ToList();
        File.WriteAllText(metaPath, JsonSerializer.Serialize(metaUrls, MetaJsonOpt), Encoding.UTF8);

        // 更新 index.json
        var indexPath = Path.Combine(outputDir, "index.json");
        Dictionary<string, int> index;
        try
        {
            if (File.Exists(indexPath))
                index = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(indexPath))
                        ?? new Dictionary<string, int>();
            else
                index = new Dictionary<string, int>();
        }
        catch { index = new Dictionary<string, int>(); }

        index[dateStr] = ordered.Count;
        File.WriteAllText(indexPath, JsonSerializer.Serialize(index, IndexJsonOpt), Encoding.UTF8);

        _logger.Info($"已保存：{htmlPath} 和 {metaPath}，共 {ordered.Count} 条");
        return htmlPath;
    }
}