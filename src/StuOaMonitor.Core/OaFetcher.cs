using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace StuOaMonitor;

/// <summary>
/// OA 网页抓取模块
/// 请求 OA 通知列表分页接口，正则解析 HTML，按 earliest_date 停止翻页。
/// </summary>
public static class OaFetcher
{
    private const string ListUrl = "http://oa.stu.edu.cn/csweb/list.jsp";
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                                     "AppleWebKit/537.36 (KHTML, like Gecko) " +
                                     "Chrome/110.0.0.0 Safari/537.36";

    private static readonly Logger _logger = AppLog.GetLogger("stu-oa.fetcher");

    private static readonly Regex DateRe = new(
        @"(\d{4})[-/年](\d{1,2})[-/月](\d{1,2})",
        RegexOptions.Compiled);

    private static readonly Regex LinkRe = new(
        "<a[^>]*href=[\"']([^\"']*newstemplateprotal\\.jsp[^\"']*)[\"'][^>]*title=[\"']([^\"']+)[\"']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RowRe = new(
        @"<tr\b[^>]*>(.*?)</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TdRe = new(
        @"<td\b[^>]*>(.*?)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TagRe = new(@"<[^>]+>", RegexOptions.Compiled);

    private static readonly HttpClient _http;

    static OaFetcher()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    private static string StripTags(string s)
    {
        var text = WebUtility.HtmlDecode(TagRe.Replace(s, ""));
        return text.Replace('\xa0', ' ').Replace('\u3000', ' ').Trim();
    }

    private static string? NormalizeDate(string y, string mo, string d)
    {
        try
        {
            var dt = new DateTime(int.Parse(y), int.Parse(mo), int.Parse(d));
            return dt.ToString("yyyy-MM-dd");
        }
        catch { return null; }
    }

    private static string? ExtractDate(string text)
    {
        var m = DateRe.Match(text);
        return m.Success ? NormalizeDate(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value) : null;
    }

    private static OaItem? ParseRow(string trHtml)
    {
        var lm = LinkRe.Match(trHtml);
        if (!lm.Success) return null;

        var link = WebUtility.HtmlDecode(lm.Groups[1].Value.Trim());
        var title = WebUtility.HtmlDecode(lm.Groups[2].Value.Trim());
        var url = new Uri(new Uri(ListUrl), link).ToString();

        string? date = null;
        string? department = null;

        foreach (Match tdm in TdRe.Matches(trHtml))
        {
            var tdRaw = tdm.Groups[1].Value;
            var tdText = StripTags(tdRaw);
            if (string.IsNullOrEmpty(tdText)) continue;
            if (LinkRe.IsMatch(tdRaw)) continue;    // 标题列跳过

            var d = ExtractDate(tdText);
            if (d != null && date == null)
            {
                date = d;
                var remainder = DateRe.Replace(tdText, "")
                    .Trim(' ', '-', '/', '年', '月', '日', '\u3000', '\xa0');
                if (!string.IsNullOrEmpty(remainder)
                    && department == null
                    && Regex.IsMatch(remainder, @"[\u4e00-\u9fff]"))
                    department = remainder;
                continue;
            }

            if (department == null)
                department = tdText;
        }

        if (date == null)
            date = ExtractDate(trHtml);

        return new OaItem
        {
            Date = date,
            Title = title,
            Url = url,
            Department = (department ?? "").Trim(),
        };
    }

    private static async Task<List<string>> FetchPageAsync(int pageIndex, int pageSize = 20)
    {
        var form = $"pageindex={pageIndex}&pagesize={pageSize}";
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, ListUrl);
                req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                req.Content = new StringContent(form, Encoding.ASCII, "application/x-www-form-urlencoded");

                using var resp = await _http.SendAsync(req).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                var content = Encoding.GetEncoding("GBK").GetString(bytes);

                return RowRe.Matches(content).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            }
            catch (Exception e)
            {
                _logger.Warning($"[警告] 第 {pageIndex} 页请求失败（尝试 {attempt + 1}/3）：{e.Message}");
                if (attempt < 2)
                    await Task.Delay(1000 << attempt).ConfigureAwait(false);   // 1s / 2s
            }
        }
        _logger.Warning($"[警告] 第 {pageIndex} 页请求失败（已重试 3 次）");
        return new List<string>();
    }

    /// <summary>
    /// 抓取通知列表。
    /// earliest_date: 可选 YYYY-MM-DD 字符串；提供且合法时以该日期为最早边界（含当天）；
    /// 否则回退到「最近 days 天」。
    /// </summary>
    public static async Task<List<OaItem>> FetchNewsListAsync(int days = 3, string? earliestDate = null)
    {
        DateTime? startDate = null;
        if (!string.IsNullOrWhiteSpace(earliestDate))
        {
            try
            {
                startDate = DateTime.ParseExact(
                    earliestDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture).Date;
            }
            catch { startDate = null; }
        }

        if (startDate == null)
        {
            var d = Math.Max(1, days);
            startDate = DateTime.Today.AddDays(-(d - 1)).Date;
        }

        var results = new List<OaItem>();
        var pageIndex = 1;
        const int pageSize = 20;
        var emptyStreak = 0;

        while (true)
        {
            var trs = await FetchPageAsync(pageIndex, pageSize).ConfigureAwait(false);
            if (trs.Count == 0) break;

            var pageItems = new List<OaItem>();
            var stop = false;

            foreach (var tr in trs)
            {
                var item = ParseRow(tr);
                if (item == null) continue;
                if (!string.IsNullOrEmpty(item.Date))
                {
                    var d = DateTime.ParseExact(item.Date, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture).Date;
                    if (d < startDate.Value) { stop = true; break; }
                    pageItems.Add(item);
                }
            }

            if (pageItems.Count > 0)
            {
                results.AddRange(pageItems);
                emptyStreak = 0;
            }
            else
            {
                if (++emptyStreak >= 2) break;
            }

            if (stop) break;

            if (++pageIndex > 100) break;
        }

        return results;
    }
}