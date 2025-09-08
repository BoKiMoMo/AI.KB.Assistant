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

namespace AI.KB.Assistant
{
    public partial class MainWindow : Window
    {
        private AppConfig _cfg;
        private DbService _db;
        private RoutingService _router;
        private LlmService _llm;

        public MainWindow()
        {
            InitializeComponent();

            // 讀設定（若不存在則用預設並建立）
            _cfg = ConfigService.TryLoad("config.json");
            EnsureDirs();
            _db = new DbService(_cfg.App.DbPath);
            _router = new RoutingService(_cfg);
            _llm = new LlmService(_cfg);

            // UI 初始化
            ChkDryRun.IsChecked = _cfg.App.DryRun;

            // 預設載入最近 7 天
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

        /* ================== 拖放處理 ================== */
        private async void DropInbox(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);

            foreach (var f in files)
            {
                try
                {
                    await ProcessOneAsync(f);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"處理檔案失敗：{Path.GetFileName(f)}\n{ex.Message}");
                }
            }

            LoadRecent(7);
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
            if (ChkDryRun.IsChecked == true || _cfg.App.DryRun)
            {
                ListFiles.Items.Insert(0, new Item
                {
                    Path = dest,
                    Filename = $"[DRY RUN] {Path.GetFileName(srcPath)}",
                    Category = res.primary_category,
                    Confidence = res.confidence,
                    CreatedTs = when.ToUnixTimeSeconds(),
                    Summary = res.summary,
                    Reasoning = res.reasoning,
                    Status = "pending",
                    Tags = "",
                    Project = _cfg.App.ProjectName
                });
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

        /* ================== 快速視圖 / 搜尋 ================== */
        private void BtnSearch_Click(object sender, RoutedEventArgs e) => DoSearch();
        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) DoSearch();
        }
        private void BtnRecent_Click(object sender, RoutedEventArgs e) => LoadRecent(7);
        private void BtnPending_Click(object sender, RoutedEventArgs e) => LoadByStatus("pending");
        private void BtnProgress_Click(object sender, RoutedEventArgs e) => LoadByStatus("in-progress");
        private void BtnTodo_Click(object sender, RoutedEventArgs e) => LoadByStatus("todo");
        private void BtnFavorite_Click(object sender, RoutedEventArgs e) => LoadByStatus("favorite");

        private void DoSearch()
        {
            var kw = (SearchBox.Text ?? string.Empty).Trim();
            if (kw.Length == 0)
            {
                LoadRecent(7);
                return;
            }
            var items = _db.Search(kw).ToList();
            RenderItems(items, $"搜尋「{kw}」共 {items.Count} 筆");
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
            var list = new List<Item>
            {
                new Item { Filename = $"── {header} ──", Category = "", Confidence = 0, CreatedTs = 0, Status = "", Tags = "", Project = "" }
            };
            list.AddRange(items);
            ListFiles.ItemsSource = list;
        }

        /* ================== 設定 / 說明 ================== */
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow("config.json") { Owner = this };
            if (win.ShowDialog() == true)
            {
                // 重新載入設定、更新服務
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
    }
}
