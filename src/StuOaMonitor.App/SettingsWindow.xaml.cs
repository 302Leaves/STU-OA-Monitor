using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace StuOaMonitor;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _parent;
    private readonly OaService _service;

    public SettingsWindow(MainWindow parent, OaService service, string? initialDate)
    {
        InitializeComponent();
        _parent = parent;
        _service = service;

        DateInput.Text = initialDate ?? "";
        HintText.Text = string.IsNullOrEmpty(initialDate)
            ? $"留空则回退为「最近 {_service.Days} 天」；当前生效：最近 {_service.Days} 天"
            : $"留空则回退为「最近 {_service.Days} 天」；当前生效：{initialDate} 起";

        Loaded += (_, _) => DateInput.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Return) { DoSave(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
    }

    private void OnSave(object sender, RoutedEventArgs e) => DoSave();
    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void DoSave()
    {
        var text = (DateInput.Text ?? "").Trim();
        // 各类破折号 → ASCII '-'
        foreach (var ch in new[] { '\u2011', '\u2013', '\u2014', '\u2212' })
            text = text.Replace(ch, '-');

        string? newVal;
        if (string.IsNullOrEmpty(text))
        {
            newVal = null;
        }
        else
        {
            if (!DateTime.TryParseExact(text, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                MessageBox.Show(this,
                    "请使用 YYYY-MM-DD 格式，例如 2025-01-01",
                    "日期格式错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (d.Date > DateTime.Today.Date)
            {
                MessageBox.Show(this, "最早日期不能晚于今天",
                    "日期无效", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            newVal = d.ToString("yyyy-MM-dd");
        }

        try
        {
            _parent.ApplySettings(newVal);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"无法写入配置文件：{ex.Message}",
                "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        Close();
    }
}