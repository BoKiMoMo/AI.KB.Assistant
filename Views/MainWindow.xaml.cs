using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using AI.KB.Assistant.Helpers;
using AI.KB.Assistant.Views; // HelpWindow / SettingsWindow
using System.Windows.Controls; // 確保有這行

namespace AI.KB.Assistant.Views
{
    public partial class MainWindow : Window
    {
        private readonly CheckBox ChkDryRun = new CheckBox();
        private readonly ListBox ListFiles = new ListBox();
        private readonly TextBox SearchBox = new TextBox();

        private AppConfig _cfg;
        private DbService _db;
        private RoutingService _router;
        private LlmService _llm;

        private string _currentView = "recent"; // recent/search/status

        public MainWindow()
        {
            InitializeComponent();

            _cfg = ConfigService.TryLoad("config.json");
            EnsureDirs();
            _db = new DbService(_cfg.App.DbPath);
            _router = new RoutingService(_cfg);
            _llm = new LlmService(_cfg);

            ChkDryRun.IsChecked = _cfg.App.DryRun;
            LoadRecent(7);
        }

        private void EnsureDirs()
        {
            if (!string.IsNullOrWhiteSpace(_cfg.App.RootDir))
                Directory.CreateDirectory(_cfg.App.RootDir);
            if (!string.IsNullOrWhiteSpace(_cfg.App.InboxDir))
                Directory.CreateDirectory(_cfg.App.InboxDir);
            var dbDir = Path.GetDirectoryName(_cfg.App.DbPath);
            if (!string.IsNullOrWhiteSpace(dbDir))
                Directory.CreateDirectory(dbDir!);
        }

        /* ========== 拖放（A） ========== */
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);

            int ok = 0, fail = 0;
            foreach (var f in files)
            {
                try
                {
                    await ProcessOneAsync(f);
                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    MessageBox.Show($"處理失敗：{Path.GetFileName(f)}\n{ex.Message}");
                }
            }

            // 拖放後預設回到最近 7 天
            LoadRecent(7);
            MessageBox.Show($"📂 拖曳完成：成功 {ok}，失敗 {fail}");
        }

        private async Task ProcessOneAsync(string srcPath)
        {
            // 內容先用檔名；之後可接 OCR/ASR/全文
            var text = Path.GetFileNameWithoutExtension(srcPath);

            // 分類
            var res = await _llm.ClassifyAsync(text);
            var when = DateResolver.FromFilenameOrNow(srcPath);

            // 目的地（依分類風格）
            var dest = _router.BuildDestination(srcPath, res.primary_category, when);
            dest = _router.ResolveCollision(dest);

            // 乾跑：只顯示不搬檔
            bool isDry = (ChkDryRun.IsChecked == true) || _cfg.App.DryRun;
            if (isDry)
            {
                // 直接把預覽項目放到第一列
                var list = (List<Item>)(ListFiles.ItemsSource as IEnumerable<Item>)?.ToList() ?? new List<Item>();
                list.Insert(0, new Item
                {
                    Path = dest,
                    Filename = $"[DRY RUN] {Path.GetFileName(srcPath)} → {Path.GetDirectoryName(dest)}",
                    Category = res.primary_category,
                    Confidence = res.confidence,
                    CreatedTs = when.ToUnixTimeSeconds(),
                    Summary = res.summary,
                    Reasoning = res.reasoning,
                    Status = "pending",
                    Project = _cfg.App.ProjectName
                });
                ListFiles.ItemsSource = list;
                return;
            }

            // 真搬檔
            var overwrite = _cfg.App.Overwrite.Equals("overwrite", StringComparison.OrdinalIgnoreCase);
            if (_cfg.App.MoveMode.Equals("copy", StringComparison.OrdinalIgnoreCase))
                File.Copy(srcPath, dest, overwrite);
            else
                File.Move(srcPath, dest, overwrite);

            // 寫入 DB
            var item = new Item
            {
                Path = dest,
                Filename = Path.GetFileName(dest),
                Category = res.primary_category,
                Confidence = res.confidence,
                CreatedTs = when.ToUnixTimeSeconds(),
                Summary = res.summary,
                Reasoning = res.reasoning,
                Status = "normal",
                Tags = "",
                Project = _cfg.App.ProjectName
            };
            _db.Add(item);
        }

        /* ========== 快速視圖 / 搜尋 ========== */
        private void BtnSearch_Click(object sender, RoutedEventArgs e) => DoSearch();
        private void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) DoSearch(); }
        private void BtnRecent_Click(object sender, RoutedEventArgs e) { _currentView = "recent"; LoadRecent(7); }
        private void BtnPending_Click(object sender, RoutedEventArgs e) { _currentView = "status:pending"; LoadByStatus("pending"); }
        private void BtnProgress_Click(object sender, RoutedEventArgs e) { _currentView = "status:in-progress"; LoadByStatus("in-progress"); }
        private void BtnTodo_Click(object sender, RoutedEventArgs e) { _currentView = "status:todo"; LoadByStatus("todo"); }
        private void BtnFavorite_Click(object sender, RoutedEventArgs e) { _currentView = "status:favorite"; LoadByStatus("favorite"); }

        private void DoSearch()
        {
            var kw = (SearchBox.Text ?? string.Empty).Trim();
            if (kw.Length == 0) { _currentView = "recent"; LoadRecent(7); return; }

            var items = _db.Search(kw).ToList();
            RenderItems(items, $"搜尋「{kw}」共 {items.Count} 筆");
            _currentView = "search";
        }

        private void LoadRecent(int days)
        {
            var items = _db.Recent(days).ToList();
            RenderItems(items, $"最近 {days} 天，共 {items.Count} 筆");
        }

        private void LoadByStatus(string status)
        {
            var items = _db.ByStatus(status).ToList();
            RenderItems(items, $"狀態：{status}（{items.Count}）");
        }

        private void RenderItems(IEnumerable<Item> items, string header)
        {
            // 顯示在 ListView：第一列顯示標題（用一條虛擬 Item）
            var list = new List<Item> { new Item { Filename = $"── {header} ──" } };
            list.AddRange(items);
            ListFiles.ItemsSource = list;
        }

        /* ========== 設定 / 說明 ========== */
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow("config.json") { Owner = this };
            if (win.ShowDialog() == true)
            {
                _cfg = ConfigService.TryLoad("config.json");
                ChkDryRun.IsChecked = _cfg.App.DryRun;

                _db?.Dispose();
                _db = new DbService(_cfg.App.DbPath);
                _router = new RoutingService(_cfg);
                _llm = new LlmService(_cfg);

                LoadRecent(7);
            }
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            var win = new HelpWindow { Owner = this };
            win.ShowDialog();
        }

        /* ========== 右鍵狀態（B） ========== */
        private void CtxSetFavorite_Click(object sender, RoutedEventArgs e) => UpdateStatusForSelection("favorite");
        private void CtxSetTodo_Click(object sender, RoutedEventArgs e) => UpdateStatusForSelection("todo");
        private void CtxSetInProgress_Click(object sender, RoutedEventArgs e) => UpdateStatusForSelection("in-progress");
        private void CtxSetPending_Click(object sender, RoutedEventArgs e) => UpdateStatusForSelection("pending");
        private void CtxSetNormal_Click(object sender, RoutedEventArgs e) => UpdateStatusForSelection("normal");

        private void UpdateStatusForSelection(string status)
        {
            var selected = ListFiles.SelectedItems.Cast<Item>()
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Path)) // 過濾標題列
                .ToList();
            if (selected.Count == 0) { MessageBox.Show("請先選取要變更狀態的項目。"); return; }

            var affected = _db.UpdateStatusByPath(selected.Select(x => x.Path), status);

            // 重新整理目前視圖
            if (_currentView.StartsWith("status:", StringComparison.OrdinalIgnoreCase))
                LoadByStatus(_currentView.Split(':')[1]);
            else if (_currentView == "search")
                DoSearch();
            else
                LoadRecent(7);

            MessageBox.Show($"已更新 {affected} 筆為「{status}」。");
        }

        /* ========== 測試分類風格（C） ========== */
        private void BtnTestRouting_Click(object sender, RoutedEventArgs e)
        {
            var now = DateTimeOffset.Now;
            var samples = new[]
            {
                ("會議記錄-產品路線圖.docx", "會議"),
                ("發票2025-09-10.pdf",     "財務"),
                ("UI提案.pptx",            "簡報"),
                ("示意圖.png",             "圖片"),
            };

            var lines = new List<string>
            {
                $"分類風格：{_cfg.App.ClassificationMode}，時間粒度：{_cfg.App.TimeGranularity}，專案：{_cfg.App.ProjectName}",
                $"根目錄：{_cfg.App.RootDir}",
                ""
            };

            foreach (var (name, cat) in samples)
            {
                var fakeSrc = Path.Combine(_cfg.App.InboxDir ?? "", name);
                var dest = _router.BuildDestination(fakeSrc, cat, now);
                lines.Add($"{name} →");
                lines.Add($"    {dest}");
            }

            MessageBox.Show(string.Join(Environment.NewLine, lines), "路徑預覽（不搬檔）",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
