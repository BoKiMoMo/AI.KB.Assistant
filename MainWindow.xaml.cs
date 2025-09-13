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
        private LlmService _local = new();
        private string _currentView = "recent";

        public MainWindow()
        {
            InitializeComponent();
            _cfg = ConfigService.TryLoad(_configPath);

            if (!string.IsNullOrWhiteSpace(_cfg.App?.DbPath))
                _db = new DbService(_cfg.App.DbPath);

            ListView.ItemsSource = _items;

            AllowDrop = true;
            DragEnter += (_, e) => e.Effects = DragDropEffects.Copy;
            Drop += Window_Drop;

            LoadRecent();
            AddLog("啟動完成。拖曳檔案可分類與搬檔，右鍵可標記狀態。");
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
                try { await ProcessOneAsync(f, cts.Token); ok++; }
                catch (Exception ex) { fail++; AddLog($"處理失敗 {Path.GetFileName(f)}：{ex.Message}"); }
            }
            AddLog($"完成：成功 {ok}、失敗 {fail}");
            ReloadCurrentView();
        }

        private async Task ProcessOneAsync(string srcPath, CancellationToken ct)
        {
            await Task.Yield();
            var fi = new FileInfo(srcPath);
            if (!fi.Exists) throw new FileNotFoundException("來源檔不存在", srcPath);

            var (cat, conf) = await _local.ClassifyLocalAsync(fi.Name);

            var it = new Item
            {
                Path = fi.FullName,
                Filename = fi.Name,
                CreatedTs = (long)(fi.CreationTimeUtc.Subtract(DateTime.UnixEpoch).TotalSeconds),
                Category = cat,
                Confidence = conf,
                Summary = Path.GetFileNameWithoutExtension(fi.Name),
                Status = "normal",
                Tags = "",
                Project = string.IsNullOrWhiteSpace(_cfg.App?.ProjectName) ? "DefaultProject" : _cfg.App.ProjectName
            };

            // 目的地
            var targetDir = RoutingService.BuildTargetPath(_cfg, it);
            Directory.CreateDirectory(targetDir);

            var dest = Path.Combine(targetDir, SafeFileName(fi.Name));
            if (File.Exists(dest) && !_cfg.App.Overwrite)
                dest = RoutingService.ResolveCollision(dest);

            if (_cfg.App?.DryRun == true)
            {
                AddLog($"[乾跑] {fi.Name} → {dest}");
            }
            else
            {
                if (string.Equals(_cfg.App?.MoveMode, "move", StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(fi.FullName, dest, overwrite: _cfg.App.Overwrite);
                    File.Delete(fi.FullName);
                }
                else
                {
                    File.Copy(fi.FullName, dest, overwrite: _cfg.App.Overwrite);
                }
                AddLog($"已輸出：{dest}");
            }

            _db?.Add(it);
            _items.Add(it);
        }

        private static string SafeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
        #endregion

        #region Views & Search
        private void BtnRecent_Click(object sender, RoutedEventArgs e) { _currentView = "recent"; LoadRecent(); }
        private void BtnProgress_Click(object sender, RoutedEventArgs e) { _currentView = "in-progress"; LoadByStatus("in-progress"); }
        private void BtnPending_Click(object sender, RoutedEventArgs e) { _currentView = "todo"; LoadByStatus("todo"); }
        private void BtnFavorites_Click(object sender, RoutedEventArgs e) { _currentView = "favorite"; LoadByStatus("favorite"); }

        private void LoadRecent()
        {
            if (_db == null) { ListView.ItemsSource = _items; return; }
            var rows = _db.Recent(7).ToList();
            ReplaceItems(rows);
        }

        private void LoadByStatus(string status)
        {
            if (_db == null)
            {
                var rows = _items.Where(x => string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase)).ToList();
                ListView.ItemsSource = new ObservableCollection<Item>(rows);
                return;
            }
            ReplaceItems(_db.ByStatus(status).ToList());
        }

        private void ReloadCurrentView()
        {
            switch (_currentView)
            {
                case "in-progress": LoadByStatus("in-progress"); break;
                case "todo": LoadByStatus("todo"); break;
                case "favorite": LoadByStatus("favorite"); break;
                default: LoadRecent(); break;
            }
        }

        private void ReplaceItems(System.Collections.Generic.IEnumerable<Item> list)
        {
            _items.Clear();
            foreach (var it in list) _items.Add(it);
            ListView.ItemsSource = _items;
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e) => DoSearch();
        private void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) DoSearch(); }
        private void DoSearch()
        {
            var kw = (SearchBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(kw)) { ReloadCurrentView(); return; }

            if (_db == null)
            {
                var rows = _items.Where(x =>
                    (x.Filename ?? "").Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                    (x.Category ?? "").Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                    (x.Summary ?? "").Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                    (x.Tags ?? "").Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                    (x.Project ?? "").Contains(kw, StringComparison.OrdinalIgnoreCase)
                ).ToList();
                ListView.ItemsSource = new ObservableCollection<Item>(rows);
                AddLog($"[本地] 搜尋「{kw}」共 {rows.Count} 筆");
                return;
            }

            var rs = _db.Search(kw).ToList();
            ReplaceItems(rs);
            AddLog($"[DB] 搜尋「{kw}」共 {rs.Count} 筆");
        }
        #endregion

        #region Context Menu: Status
        private void CtxMarkFavorite_Click(object sender, RoutedEventArgs e) => UpdateSelectedStatus("favorite");
        private void CtxMarkTodo_Click(object sender, RoutedEventArgs e) => UpdateSelectedStatus("todo");
        private void CtxMarkInProgress_Click(object sender, RoutedEventArgs e) => UpdateSelectedStatus("in-progress");
        private void CtxMarkNormal_Click(object sender, RoutedEventArgs e) => UpdateSelectedStatus("normal");

        private void UpdateSelectedStatus(string status)
        {
            var selected = ListView.SelectedItems.Cast<Item>().ToList();
            if (selected.Count == 0) { AddLog("未選取項目"); return; }

            foreach (var it in selected) it.Status = status;
            _db?.UpdateStatusByPath(selected.Select(s => s.Path), status);

            ReloadCurrentView();
            AddLog($"已標記 {selected.Count} 筆為：{status}");
        }
        #endregion

        #region Settings / Help / Log
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var w = new SettingsWindow(_configPath, _cfg) { Owner = this };
            if (w.ShowDialog() == true)
            {
                _cfg = ConfigService.TryLoad(_configPath);
                _db?.Dispose(); _db = null;
                if (!string.IsNullOrWhiteSpace(_cfg.App?.DbPath))
                    _db = new DbService(_cfg.App.DbPath);
                ReloadCurrentView();
                AddLog("設定已重新載入。");
            }
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e) => new HelpWindow { Owner = this }.ShowDialog();

        private void AddLog(string msg)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
            TxtLog.ScrollToEnd();
        }
        #endregion
    }
}
