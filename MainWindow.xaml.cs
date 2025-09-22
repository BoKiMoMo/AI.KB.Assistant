using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using AI.KB.Assistant.Views;

namespace AI.KB.Assistant
{
    public partial class MainWindow : Window
    {
        private readonly string _configPath = "config.json";
        private AppConfig _cfg = new();
        private DbService? _db;
        private LlmService? _llm;

        private readonly ObservableCollection<Item> _items = new();
        private string _currentView = "recent";

        // 背景佇列
        private readonly ConcurrentQueue<string> _queue = new();
        private CancellationTokenSource? _cts;
        private volatile bool _pause;

        // Undo/Redo（僅最近一批）
        private readonly Stack<ActionBatch> _undo = new();
        private readonly Stack<ActionBatch> _redo = new();

        public MainWindow()
        {
            InitializeComponent();

            _cfg = ConfigService.TryLoad(_configPath);
            var dbPath = string.IsNullOrWhiteSpace(_cfg.App.DbPath)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "main.db")
                : _cfg.App.DbPath;

            _db = new DbService(dbPath);
            _llm = new LlmService(_cfg);

            ListView.ItemsSource = _items;

            LoadRecent();
            UpdateStats();
            AddLog("第二階段啟動：拖檔進視窗即可加入佇列處理。");
            InitShortcuts();
        }

        #region Drag & Queue
        private void Window_DragEnter(object sender, DragEventArgs e) => e.Effects = DragDropEffects.Copy;

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

            foreach (var f in files) if (File.Exists(f)) _queue.Enqueue(f);
            AddLog($"加入佇列：{files.Length} 個。");
            StartWorkerIfNeeded();
        }

        private void StartWorkerIfNeeded()
        {
            if (_cts != null && !_cts.IsCancellationRequested) return;

            _pause = false;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(async () =>
            {
                int total = Math.Max(1, _queue.Count);
                int done = 0;

                while (!token.IsCancellationRequested)
                {
                    if (_pause) { await Task.Delay(150, token); continue; }
                    if (!_queue.TryDequeue(out var path)) break;

                    try
                    {
                        await ProcessOneAsync(path);
                        done++;
                    }
                    catch (Exception ex)
                    {
                        AddLog($"處理失敗：{Path.GetFileName(path)} → {ex.Message}");
                    }

                    Dispatcher.Invoke(() => Progress.Value = done * 100.0 / total);
                }

                Dispatcher.Invoke(() => Progress.Value = 0);
                UpdateStats();
                AddLog("佇列處理結束。");
            }, token);
        }

        private async Task ProcessOneAsync(string srcPath)
        {
            await Task.Yield();

            var fi = new FileInfo(srcPath);
            var text = Path.GetFileNameWithoutExtension(fi.Name);

            // 分類：以 ClassificationResult 物件回傳
            var result = await _llm!.ClassifyAsync(fi.Name, text);
            var status = result.Confidence < _cfg.Classification.ConfidenceThreshold ? "auto-sorted" : "normal";

            var it = new Item
            {
                Path = fi.FullName,
                Filename = fi.Name,
                Category = result.Category,
                Confidence = result.Confidence,
                CreatedTs = DateTimeOffset.Now.ToUnixTimeSeconds(),
                Summary = text,
                Reasoning = result.Reason,
                Status = status,
                Tags = "",
                Project = _cfg.App.ProjectName
            };

            _db!.Add(it);
            Dispatcher.Invoke(() => _items.Insert(0, it));
        }
        #endregion

        #region Toolbar buttons
        private void BtnSearch_Click(object sender, RoutedEventArgs e) => DoSearch();
        private void BtnClearSearch_Click(object sender, RoutedEventArgs e) { SearchBox.Text = ""; ReloadCurrentView(); }

        private void BtnRecent_Click(object sender, RoutedEventArgs e) { _currentView = "recent"; LoadRecent(); }
        private void BtnProgress_Click(object sender, RoutedEventArgs e) { _currentView = "in-progress"; LoadByStatus("in-progress"); }
        private void BtnPending_Click(object sender, RoutedEventArgs e) { _currentView = "pending"; LoadByStatus("pending"); }
        private void BtnFavorite_Click(object sender, RoutedEventArgs e) { _currentView = "favorite"; LoadByStatus("favorite"); }
        private void BtnAutoSorted_Click(object sender, RoutedEventArgs e) { _currentView = "auto-sorted"; LoadByStatus("auto-sorted"); }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            var w = new HelpWindow { Owner = this };
            w.ShowDialog();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var w = new SettingsWindow(_configPath, _cfg) { Owner = this };
            if (w.ShowDialog() == true)
            {
                _cfg = ConfigService.TryLoad(_configPath);
                AddLog("設定已重新載入。");
            }
        }

        private void BtnCancelQueue_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            AddLog("已要求取消佇列。");
        }

        private async void BtnExecuteMove_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0) { AddLog("沒有可搬檔的項目。"); return; }

            var list = _items.ToList();
            var dry = _cfg.App.DryRun;
            var mode = _cfg.App.MoveMode;
            var policy = _cfg.App.OverwritePolicy;

            int ok = 0, skip = 0, fail = 0;

            foreach (var it in list)
            {
                try
                {
                    string targetDir = it.Status == "auto-sorted"
                        ? RoutingService.BuildAutoFolder(_cfg)
                        : RoutingService.BuildTargetDirectory(_cfg, it);

                    Directory.CreateDirectory(targetDir);
                    var destPath = Path.Combine(targetDir, RoutingService.Safe(it.Filename));

                    if (policy == "rename") destPath = RoutingService.ResolveCollision(destPath);
                    if (File.Exists(destPath))
                    {
                        if (policy == "skip") { skip++; continue; }
                        if (policy == "replace") { /* 覆蓋 */ }
                    }

                    if (dry)
                    {
                        AddLog($"[DryRun] {it.Filename} → {destPath}");
                        ok++;
                        continue;
                    }

                    if (mode == "copy") File.Copy(it.Path, destPath, policy == "replace");
                    else File.Move(it.Path, destPath, policy == "replace");

                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    AddLog($"搬檔失敗：{it.Filename} → {ex.Message}");
                }

                await Task.Yield();
            }

            AddLog($"搬檔完成：成功 {ok}，跳過 {skip}，失敗 {fail}。");
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (_undo.Count == 0) { AddLog("沒有可復原的操作。"); return; }
            var act = _undo.Pop();
            act.Undo(_db!);
            _redo.Push(act);
            ReloadCurrentView();
            AddLog($"已復原：{act.Name}");
        }

        private void BtnRedo_Click(object sender, RoutedEventArgs e)
        {
            if (_redo.Count == 0) { AddLog("沒有可重做的操作。"); return; }
            var act = _redo.Pop();
            act.Redo(_db!);
            _undo.Push(act);
            ReloadCurrentView();
            AddLog($"已重做：{act.Name}");
        }
        #endregion

        #region Search & Load
        private void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) DoSearch(); }

        private void DoSearch()
        {
            var q = (SearchBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(q)) { ReloadCurrentView(); return; }

            // 簡易語法：filename:*.pdf  tag:發票  status:pending  category:財務
            string? f = null, c = null, s = null, t = null;
            foreach (var token in q.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = token.Split(':', 2);
                if (p.Length != 2) continue;
                var key = p[0].ToLowerInvariant();
                var val = p[1];
                if (key == "filename") f = val;
                else if (key == "category") c = val;
                else if (key == "status") s = val;
                else if (key == "tag") t = val;
            }

            if (f == null && c == null && s == null && t == null)
            {
                var res = _db!.Search(q).ToList();
                ReplaceItems(res);
                AddLog($"搜尋「{q}」共 {res.Count} 筆。");
            }
            else
            {
                var res = _db!.SearchAdvanced(f, c, s, t).ToList();
                ReplaceItems(res);
                AddLog($"進階搜尋完成（{q}）：{res.Count} 筆。");
            }
        }

        private void LoadRecent()
        {
            var res = _db!.Recent(14).ToList();
            ReplaceItems(res);
            AddLog("載入最近 14 天。");
        }

        private void LoadByStatus(string status)
        {
            var res = _db!.ByStatus(status).ToList();
            ReplaceItems(res);
            AddLog($"載入狀態：{status}。");
        }

        private void ReloadCurrentView()
        {
            switch (_currentView)
            {
                case "in-progress": LoadByStatus("in-progress"); break;
                case "pending": LoadByStatus("pending"); break;
                case "favorite": LoadByStatus("favorite"); break;
                case "auto-sorted": LoadByStatus("auto-sorted"); break;
                default: LoadRecent(); break;
            }
        }

        private void ReplaceItems(List<Item> list)
        {
            _items.Clear();
            foreach (var it in list) _items.Add(it);
            UpdateStats();
        }
        #endregion

        #region Context Menu (favorite/tags/status/reclassify)
        private Item? GetSelectedOne() => ListView.SelectedItem as Item;
        private List<Item> GetSelectedMany() => ListView.SelectedItems.Cast<Item>().ToList();

        private void MarkFavorite_Click(object sender, RoutedEventArgs e) => BatchSetStatus("favorite", "加入最愛");
        private void MarkInProgress_Click(object sender, RoutedEventArgs e) => BatchSetStatus("in-progress", "設為處理中");
        private void MarkPending_Click(object sender, RoutedEventArgs e) => BatchSetStatus("pending", "設為待處理");

        private void BatchSetStatus(string status, string actionName)
        {
            var list = GetSelectedMany();
            if (list.Count == 0) return;

            var before = list.Select(it => (it.Path, it.Status)).ToList();
            _db!.UpdateStatusByPath(list.Select(x => x.Path), status);
            foreach (var it in list) it.Status = status;

            PushUndo(new ActionBatch
            {
                Name = $"{actionName}（{list.Count}）",
                Do = db => db.UpdateStatusByPath(list.Select(x => x.Path), status),
                UndoDo = db => { foreach (var (p, s) in before) db.UpdateStatusByPath(new[] { p }, s); }
            });

            ReloadCurrentView();
            AddLog($"{actionName}完成：{list.Count} 筆。");
        }

        private void AddTag_Click(object sender, RoutedEventArgs e)
        {
            var list = GetSelectedMany();
            if (list.Count == 0) return;

            var tag = Prompt("輸入要新增的標籤（單一字串）", "新增標籤");
            if (string.IsNullOrWhiteSpace(tag)) return;

            var before = list.Select(it => (it.Path, it.Tags)).ToList();

            foreach (var it in list)
            {
                var tags = (it.Tags ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToList();

                if (!tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
                    tags.Add(tag);

                it.Tags = string.Join(", ", tags);
                _db!.UpdateTagsByPath(new[] { it.Path }, it.Tags);
            }

            // 記錄最近標籤
            if (!_cfg.Views.FavoriteTags.Contains(tag)) _cfg.Views.FavoriteTags.Insert(0, tag);
            if (_cfg.Views.FavoriteTags.Count > 5) _cfg.Views.FavoriteTags.RemoveAt(5);
            ConfigService.Save(_configPath, _cfg);

            PushUndo(new ActionBatch
            {
                Name = $"新增標籤「{tag}」（{list.Count}）",
                Do = db => { foreach (var it in list) db.UpdateTagsByPath(new[] { it.Path }, it.Tags); },
                UndoDo = db => { foreach (var (p, t) in before) db.UpdateTagsByPath(new[] { p }, t ?? ""); }
            });

            AddLog($"已新增標籤「{tag}」到 {list.Count} 筆。");
        }

        private void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            var list = GetSelectedMany();
            if (list.Count == 0) return;

            var tag = Prompt("輸入要移除的標籤", "移除標籤");
            if (string.IsNullOrWhiteSpace(tag)) return;

            var before = list.Select(it => (it.Path, it.Tags)).ToList();

            foreach (var it in list)
            {
                var tags = (it.Tags ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToList();

                tags.RemoveAll(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase));

                it.Tags = string.Join(", ", tags);
                _db!.UpdateTagsByPath(new[] { it.Path }, it.Tags);
            }

            PushUndo(new ActionBatch
            {
                Name = $"移除標籤「{tag}」（{list.Count}）",
                Do = db => { foreach (var it in list) db.UpdateTagsByPath(new[] { it.Path }, it.Tags); },
                UndoDo = db => { foreach (var (p, t) in before) db.UpdateTagsByPath(new[] { p }, t ?? ""); }
            });

            AddLog($"已移除標籤「{tag}」。");
        }

        private async void Reclassify_Click(object sender, RoutedEventArgs e)
        {
            var list = GetSelectedMany();
            if (list.Count == 0) return;

            var before = list.Select(it => (it.Path, it.Category, it.Confidence)).ToList();

            foreach (var it in list)
            {
                var result = await _llm!.ClassifyAsync(it.Filename, it.Summary);
                it.Category = result.Category;
                it.Confidence = result.Confidence;
                it.Reasoning = result.Reason;
                _db!.UpdateCategoryByPath(new[] { it.Path }, it.Category, it.Confidence);
            }

            PushUndo(new ActionBatch
            {
                Name = $"重跑分類（{list.Count}）",
                Do = db => { foreach (var it in list) db.UpdateCategoryByPath(new[] { it.Path }, it.Category, it.Confidence); },
                UndoDo = db => { foreach (var (p, c, conf) in before) db.UpdateCategoryByPath(new[] { p }, c, conf); }
            });

            ReloadCurrentView();
            AddLog("重跑分類完成。");
        }
        #endregion

        #region Preview / Stats / Helpers
        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var count = ListView.SelectedItems.Count;
            SelectionInfo.Text = count > 0 ? $"已選 {count} 筆" : "";
            ShowPreview(GetSelectedOne());
        }

        private void ShowPreview(Item? it)
        {
            PreviewHeader.Text = it == null ? "（選取檔案以預覽）" : it.Filename;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewText.Visibility = Visibility.Collapsed;

            if (it == null || !File.Exists(it.Path)) return;

            var ext = (Path.GetExtension(it.Path) ?? "").ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp")
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(it.Path);
                    bmp.DecodePixelWidth = 320;
                    bmp.EndInit();
                    PreviewImage.Source = bmp;
                    PreviewImage.Visibility = Visibility.Visible;
                }
                catch { /* ignore */ }
            }
            else if (ext is ".txt" or ".md" or ".csv")
            {
                try
                {
                    var text = File.ReadAllText(it.Path, Encoding.UTF8);
                    PreviewText.Text = text.Length > 5000 ? text.Substring(0, 5000) + "\r\n..." : text;
                    PreviewText.Visibility = Visibility.Visible;
                }
                catch { /* ignore */ }
            }
        }

        private void UpdateStats()
        {
            int week = _items.Count(x => DateTimeOffset.FromUnixTimeSeconds(x.CreatedTs) >= DateTimeOffset.Now.AddDays(-7));
            int fav = _items.Count(x => string.Equals(x.Status, "favorite", StringComparison.OrdinalIgnoreCase));
            int auto = _items.Count(x => string.Equals(x.Status, "auto-sorted", StringComparison.OrdinalIgnoreCase));
            StatSummary.Text = $"本週新增：{week}；我的最愛：{fav}；自整理：{auto}";
        }

        private void AddLog(string msg)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
            TxtLog.ScrollToEnd();
        }

        private void InitShortcuts()
        {
            InputBindings.Add(new KeyBinding(new Relay(() => SearchBox.Focus()), new KeyGesture(Key.F, ModifierKeys.Control)));
            InputBindings.Add(new KeyBinding(new Relay(() => { TxtLog.Clear(); }), new KeyGesture(Key.L, ModifierKeys.Control)));
        }
        #endregion

        #region Small utils (Prompt / Undo helper / Relay)
        private static string? Prompt(string message, string title)
        {
            var w = new Window
            {
                Title = title,
                Width = 360,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var panel = new Grid { Margin = new Thickness(12) };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tbMsg = new TextBlock { Text = message, Margin = new Thickness(0, 0, 0, 8) };
            var tbInput = new TextBox { Height = 28, Margin = new Thickness(0, 0, 0, 12) };

            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "確定", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "取消", Width = 80 };

            string? result = null;
            ok.Click += (_, __) => { result = tbInput.Text; w.DialogResult = true; };
            cancel.Click += (_, __) => { result = null; w.DialogResult = false; };

            sp.Children.Add(ok);
            sp.Children.Add(cancel);

            Grid.SetRow(tbMsg, 0);
            Grid.SetRow(tbInput, 1);
            Grid.SetRow(sp, 2);

            panel.Children.Add(tbMsg);
            panel.Children.Add(tbInput);
            panel.Children.Add(sp);

            w.Content = panel;
            w.ShowDialog();
            return result;
        }

        private void PushUndo(ActionBatch batch)
        {
            _undo.Push(batch);
            _redo.Clear();
        }

        private class ActionBatch
        {
            public string Name { get; set; } = "";
            public Action<DbService> Do { get; set; } = _ => { };
            public Action<DbService> UndoDo { get; set; } = _ => { };
            public void Undo(DbService db) => UndoDo(db);
            public void Redo(DbService db) => Do(db);
        }

        private class Relay : ICommand
        {
            private readonly Action _action;
            public Relay(Action a) { _action = a; }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => _action();
            public event EventHandler? CanExecuteChanged;
        }
        #endregion
    }
}
