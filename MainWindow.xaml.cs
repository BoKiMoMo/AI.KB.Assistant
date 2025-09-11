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
        private DbService? _db;
        private string _currentView = "recent"; // recent / in-progress / pending / favorite

        public MainWindow()
        {
            InitializeComponent();

            // 載入設定
            _cfg = ConfigService.TryLoad(_configPath);

            // 建立 DB Service
            if (!string.IsNullOrWhiteSpace(_cfg.App.DbPath))
                _db = new DbService(_cfg.App.DbPath);

            // 綁定清單
            var lv = FindName("ListView") as ListView;
            if (lv != null) lv.ItemsSource = _items;

            // 允許拖曳
            AllowDrop = true;
            DragEnter += (_, e) => e.Effects = DragDropEffects.Copy;
            Drop += Window_Drop;

            AddLog("AI.KB.Assistant 已啟動。把檔案拖進來即可分類並存入 DB。");

            LoadRecent(); // 啟動時載入最近
        }

        #region Drag & Process

        private async void Window_Drop(object sender, DragEventArgs e)
        {
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
            LoadRecent(); // 重新整理列表
        }

        private async Task ProcessOneAsync(string srcPath, CancellationToken ct)
        {
            await Task.Yield();

            var fi = new FileInfo(srcPath);
            var it = new Item
            {
                Path = fi.FullName,
                Filename = fi.Name,
                CreatedTs = (long)(fi.CreationTimeUtc.Subtract(DateTime.UnixEpoch).TotalSeconds),
                Category = GuessCategory(fi.Name),
                Confidence = 0.8,
                Summary = Path.GetFileNameWithoutExtension(fi.Name),
                Status = "normal",
                Tags = "",
                Project = _cfg.App.ProjectName ?? "DefaultProject"
            };

            // 計算目標路徑
            var targetDir = RoutingService.BuildTargetPath(_cfg, it);

            if (_cfg.App.DryRun)
            {
                AddLog($"[模擬搬檔] {fi.Name} -> {targetDir}");
            }
            else
            {
                Directory.CreateDirectory(targetDir);
                var dest = Path.Combine(targetDir, fi.Name);

                if (File.Exists(dest) && !_cfg.App.Overwrite)
                {
                    AddLog($"檔案已存在，略過：{dest}");
                }
                else
                {
                    if (_cfg.App.MoveMode == "move")
                        File.Move(fi.FullName, dest, overwrite: _cfg.App.Overwrite);
                    else
                        File.Copy(fi.FullName, dest, overwrite: _cfg.App.Overwrite);

                    AddLog($"搬檔完成：{fi.Name} -> {dest}");
                }
            }

            // 存入 DB
            _db?.Add(it);

            // 更新清單
            _items.Add(it);
        }

        private static string GuessCategory(string name)
        {
            var n = name.ToLowerInvariant();
            if (n.Contains("invoice") || n.Contains("receipt") || n.Contains("發票")) return "票據";
            if (n.Contains("report") || n.Contains("報表")) return "報表";
            if (n.Contains("contract") || n.Contains("合約")) return "合約";
            if (n.EndsWith(".png") || n.EndsWith(".jpg")) return "照片";
            if (n.EndsWith(".cs") || n.EndsWith(".ts") || n.EndsWith(".py")) return "程式碼";
            return "其他";
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

        private void BtnFavorites_Click(object sender, RoutedEventArgs e)
        {
            _currentView = "favorite";
            LoadByStatus("favorite");
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e) => DoSearch();

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) DoSearch();
        }

        private void DoSearch()
        {
            var tb = FindName("SearchBox") as TextBox;
            var keyword = tb?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(keyword))
            {
                LoadRecent();
                return;
            }

            var results = _db?.Search(keyword).ToList() ?? new();
            var lv = FindName("ListView") as ListView;
            if (lv != null) lv.ItemsSource = new ObservableCollection<Item>(results);

            AddLog($"搜尋「{keyword}」, {results.Count} 筆。");
        }

        private void LoadRecent()
        {
            var results = _db?.Recent(7).ToList() ?? new();
            var lv = FindName("ListView") as ListView;
            if (lv != null) lv.ItemsSource = new ObservableCollection<Item>(results);
        }

        private void LoadByStatus(string status)
        {
            var results = _db?.ByStatus(status).ToList() ?? new();
            var lv = FindName("ListView") as ListView;
            if (lv != null) lv.ItemsSource = new ObservableCollection<Item>(results);
        }

        #endregion

        #region Settings / Help

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new Views.SettingsWindow(_configPath, _cfg) { Owner = this };
            if (win.ShowDialog() == true)
            {
                _cfg = ConfigService.TryLoad(_configPath);
                if (!string.IsNullOrWhiteSpace(_cfg.App.DbPath))
                    _db = new DbService(_cfg.App.DbPath);

                AddLog("已重新載入設定與資料庫。");
                LoadRecent();
            }
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            var win = new HelpWindow { Owner = this };
            win.ShowDialog();
        }

        #endregion

        #region Logger

        private void AddLog(string msg)
        {
            var tb = FindName("TxtLog") as TextBox;
            if (tb == null) return;
            tb.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
            tb.ScrollToEnd();
        }

        #endregion
    }
}
