using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using AI.KB.Assistant.Views;

namespace AI.KB.Assistant
{
    public partial class MainWindow : Window
    {
        private readonly string _configPath = "config.json";
        private AppConfig _cfg = new();

        private readonly ObservableCollection<Item> _items = new();
        private string _currentView = "recent"; // recent / in-progress / pending

        public MainWindow()
        {
            InitializeComponent();

            // 載入設定
            _cfg = ConfigService.TryLoad(_configPath);

            // 綁定清單
            LvItems.ItemsSource = _items;

            AddLog("AI.KB.Assistant 啟動；可將檔案拖曳到視窗以模擬分類。");
        }

        #region Drag & Process

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            // 放一個 await，避免 CS1998
            await Task.Yield();

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

            using var cts = new CancellationTokenSource();
            int ok = 0, fail = 0;

            foreach (var f in files)
            {
                try
                {
                    await ProcessOneAsync(f, cts.Token);
                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    AddLog($"處理失敗：{Path.GetFileName(f)} -> {ex.Message}");
                }
            }

            AddLog($"完成。成功 {ok}，失敗 {fail}");
        }

        private async Task ProcessOneAsync(string srcPath, CancellationToken ct)
        {
            await Task.Yield();

            var fi = new FileInfo(srcPath);
            var it = new Item
            {
                Path = fi.FullName,
                Filename = fi.Name,
                CreatedTs = new DateTimeOffset(fi.CreationTimeUtc).ToUnixTimeSeconds(),
                Category = GuessCategory(fi.Name),
                Confidence = 0.8,
                Summary = Path.GetFileNameWithoutExtension(fi.Name),
                Status = "normal",
                Tags = "",
                Project = "DefaultProject"
            };

            var targetDir = RoutingService.BuildTargetPath(_cfg, it);
            AddLog($"[模擬] {fi.Name} → {targetDir}");

            _items.Add(it);
        }

        private string GuessCategory(string name)
        {
            var n = (name ?? "").ToLowerInvariant();
            if (n.Contains("invoice") || n.Contains("receipt") || n.Contains("發票")) return "票據";
            if (n.Contains("report") || n.Contains("報表")) return "報表";
            if (n.Contains("contract") || n.Contains("合約")) return "合約";
            if (n.EndsWith(".png") || n.EndsWith(".jpg") || n.EndsWith(".jpeg")) return "照片";
            if (n.EndsWith(".cs") || n.EndsWith(".ts") || n.EndsWith(".py")) return "程式碼";
            return _cfg.Classification?.FallbackCategory ?? "其他";
        }

        #endregion

        #region Top Buttons / Search

        private void BtnPending_Click(object sender, RoutedEventArgs e)
        {
            _currentView = "pending";
            LoadByStatus("pending");
        }

        private void BtnProgress_Click(object sender, RoutedEventArgs e)
        {
            _currentView = "in-progress";
            LoadByStatus("in-progress");
        }

        private void BtnRecent_Click(object sender, RoutedEventArgs e)
        {
            _currentView = "recent";
            LoadRecent();
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e) => DoSearch();

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) DoSearch();
        }

        private void DoSearch()
        {
            var keyword = (SearchBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(keyword))
            {
                LoadRecent();
                return;
            }

            var filtered = _items.Where(x =>
                    (x.Filename ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (x.Category ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (x.Summary ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (x.Tags ?? "").IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            LvItems.ItemsSource = new ObservableCollection<Item>(filtered);
            AddLog($"搜尋「{keyword}」→ {filtered.Count} 筆。");
        }

        private void LoadRecent()
        {
            LvItems.ItemsSource = _items;
        }

        private void LoadByStatus(string status)
        {
            var filtered = _items
                .Where(x => string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase))
                .ToList();

            LvItems.ItemsSource = new ObservableCollection<Item>(filtered);
        }

        #endregion

        #region Settings / Help

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            // 有你的 SettingsWindow 就開；沒有就提示
            try
            {
                var win = new Views.SettingsWindow(_configPath, _cfg) { Owner = this };
                if (win.ShowDialog() == true)
                {
                    _cfg = ConfigService.TryLoad(_configPath);
                    AddLog("已重新載入設定。");
                }
            }
            catch
            {
                MessageBox.Show("找不到 SettingsWindow 或建構子簽章不符，請先加入設定視窗。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new Views.HelpWindow { Owner = this };
                win.ShowDialog();
            }
            catch
            {
                MessageBox.Show("找不到 HelpWindow，請先加入使用教學視窗。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        #endregion

        #region Logger

        private void AddLog(string msg)
        {
            if (TxtLog == null) return;
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
            TxtLog.ScrollToEnd();
        }

        #endregion
    }
}
