using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AI.KB.Assistant.Helpers;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using AI.KB.Assistant.Views;

namespace AI.KB.Assistant
{
    public partial class MainWindow : Window
    {
        private readonly string _configPath = "config.json";
        private AppConfig _cfg = new();
        private DbService _db = null!;
        private LlmService _llm = null!;

        private readonly ObservableCollection<Item> _items = new();

        public MainWindow()
        {
            InitializeComponent();

            _cfg = ConfigService.TryLoad(_configPath);
            _db = new DbService(string.IsNullOrWhiteSpace(_cfg.App.DbPath) ? "kb.db" : _cfg.App.DbPath);
            _llm = new LlmService(_cfg);

            ListView.ItemsSource = _items;
            LoadRecent();

            Log("AI.KB.Assistant 已啟動。拖檔案進來即可分類。");
        }

        /* ============ Drag & Drop ============ */

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Copy;
            else e.Effects = DragDropEffects.None;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(10, _cfg.OpenAI.TimeoutSeconds)));
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
                    Log($"處理失敗：{Path.GetFileName(f)} -> {ex.Message}");
                }
            }

            Log($"完成：成功 {ok}，失敗 {fail}");
            RefreshCurrentView();
        }

        private async Task ProcessOneAsync(string srcPath, CancellationToken ct)
        {
            var fi = new FileInfo(srcPath);
            var created = DateResolver.FromFilenameOrNow(srcPath).ToUnixTimeSeconds();

            // 取得分類 / 摘要 / 標籤
            var (cat, conf, sum, reason) = await _llm.ClassifyAsync(fi.Name, null, ct);
            var summary = await _llm.SummarizeAsync(fi.Name, null, ct);
            var tagsArr = await _llm.SuggestTagsAsync(fi.Name, cat, summary, ct);
            var tags = string.Join(",", tagsArr);

            var it = new Item
            {
                Filename = fi.Name,
                Path = fi.FullName, // 初始為來源，搬檔後會更新
                Category = cat,
                Confidence = conf,
                CreatedTs = created,
                Summary = string.IsNullOrWhiteSpace(sum) ? summary : sum,
                Reasoning = reason,
                Status = conf < _cfg.Classification.ConfidenceThreshold ? "pending" : "normal",
                Tags = tags,
                Project = _cfg.App.ProjectName
            };

            // 計算目的地路徑
            var targetDir = RoutingService.BuildTargetPath(_cfg, it);
            var dest = System.IO.Path.Combine(targetDir, fi.Name);

            if (_cfg.App.DryRun)
            {
                Log($"[模擬] {fi.Name} → {targetDir}");
            }
            else
            {
                Directory.CreateDirectory(targetDir);
                bool overwrite = _cfg.App.Overwrite;
                if ((_cfg.App.MoveMode ?? "copy").Equals("copy", StringComparison.OrdinalIgnoreCase))
                    File.Copy(srcPath, dest, overwrite);
                else
                    File.Move(srcPath, dest, overwrite);
                it.Path = dest;
            }

            _db.Add(it);
            _items.Insert(0, it);
        }

        /* ============ 左欄操作 ============ */

        private void BtnRecent_Click(object? sender, RoutedEventArgs e) => LoadRecent();
        private void BtnPending_Click(object? sender, RoutedEventArgs e) => LoadStatus("pending");
        private void BtnProgress_Click(object? sender, RoutedEventArgs e) => LoadStatus("in-progress");
        private void BtnTodo_Click(object? sender, RoutedEventArgs e) => LoadStatus("todo");
        private void BtnFavorite_Click(object? sender, RoutedEventArgs e) => LoadStatus("favorite");

        private void LoadRecent()
        {
            _items.Clear();
            foreach (var it in _db.Recent(7)) _items.Add(it);
            TxtStatus.Text = $"顯示：最近 7 天（{_items.Count} 筆）";
        }

        private void LoadStatus(string status)
        {
            _items.Clear();
            foreach (var it in _db.ByStatus(status)) _items.Add(it);
            TxtStatus.Text = $"顯示：{status}（{_items.Count} 筆）";
        }

        private void RefreshCurrentView()
        {
            // 以狀態條顯示為準（簡化）
            LoadRecent();
        }

        /* ============ 搜尋（關鍵字 / 對話） ============ */

        private void BtnSearch_Click(object sender, RoutedEventArgs e) => DoSearch();
        private void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) DoSearch(); }

        private async void DoSearch()
        {
            var kw = (SearchBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(kw)) { LoadRecent(); return; }

            if (ChkChatSearch.IsChecked == true && _cfg.Classification.EnableChatSearch)
            {
                var (keyword, cats, tags, from, to) = await _llm.ParseQueryAsync(kw);
                var result = _db.AdvancedSearch(keyword, cats, tags, from, to).ToList();

                _items.Clear(); foreach (var it in result) _items.Add(it);
                TxtStatus.Text = $"對話搜尋結果：{_items.Count} 筆";
                Log($"[對話搜尋] 解析 → kw={keyword}, cats=[{string.Join("/", cats ?? Array.Empty<string>())}], tags=[{string.Join("/", tags ?? Array.Empty<string>())}], from={from}, to={to}");
            }
            else
            {
                var result = _db.Search(kw).ToList();
                _items.Clear(); foreach (var it in result) _items.Add(it);
                TxtStatus.Text = $"關鍵字搜尋：{_items.Count} 筆";
            }
        }

        /* ============ 設定 / 說明 ============ */

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow(_configPath, _cfg) { Owner = this };
            if (win.ShowDialog() == true)
            {
                _cfg = ConfigService.Load(_configPath);
                _llm = new LlmService(_cfg);
                _db = new DbService(string.IsNullOrWhiteSpace(_cfg.App.DbPath) ? "kb.db" : _cfg.App.DbPath);
                Log("設定已重新載入。");
                LoadRecent();
            }
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            var msg =
@"【AI 知識庫助手－使用說明】

1) 拖曳檔案到頂部框框 → 系統自動分類
2) 低於信心門檻的檔案 → 自動放入 _自整理，並顯示「pending」
3) 左側可快速檢視：最近新增 / 需要整理 / 執行中 / 代辦 / 我的最愛
4) 搜尋列：
   - 預設：關鍵字搜尋（檔名/類別/摘要/標籤）
   - 勾選「對話搜尋」：可輸入『上個月的會議 #專案A』
5) 設定：
   - 可開關 LLM、調整信心門檻、標籤數、模型與 API Key
6) 搬檔模式：
   - 預設為『乾跑』（只模擬）；要實際搬檔，請關閉 乾跑，並選擇 copy/move 與覆寫策略。
";
            MessageBox.Show(msg, "使用說明", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /* ============ Log ============ */
        private void Log(string msg)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
            TxtLog.ScrollToEnd();
        }
    }
}
