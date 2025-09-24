using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using Ookii.Dialogs.Wpf;

namespace AI.KB.Assistant.Views
{
    public partial class MainWindow : Window
    {
        // ==============  Services  ==============
        private readonly DbService _db;
        private readonly RoutingService _router;
        private readonly ConfigService _config;

        // ==============  View State  ==============
        private readonly ObservableCollection<Item> _allItems = new();
        private readonly ObservableCollection<Item> _viewItems = new();
        private ICollectionView _view;

        // 左側路徑樹（你的 PathNode.cs 已存在；若不同可換成匿名樹）
        private readonly ObservableCollection<PathNode> _pathRoots = new();

        // 篩選旗標
        private bool _filterQueue;     // 待處理
        private bool _filterFavorite;  // 我的最愛
        private bool _filterRunning;   // 執行中

        public MainWindow()
        {
            InitializeComponent();

            _db = new DbService();
            _router = new RoutingService();
            _config = new ConfigService();

            // 中間清單繫結
            FileList.ItemsSource = _viewItems;
            _view = CollectionViewSource.GetDefaultView(_viewItems);

            // 路徑樹繫結
            PathTree.ItemsSource = _pathRoots;

            // 載入資料
            Loaded += async (_, __) => await InitializeAsync();
        }

        #region 初始載入
        private async Task InitializeAsync()
        {
            try
            {
                await EnsureDbAsync();
                await RefreshAllAsync();
                await BuildPathTreeAsync();
                Log("就緒。");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "初始化失敗：\n" + ex, "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task EnsureDbAsync()
        {
            try
            {
                await _db.EnsureTablesAsync();
            }
            catch
            {
                // 舊版 DbService 沒有 async 的話，嘗試非 async 版本
                _db.EnsureTables();
            }
        }

        private async Task RefreshAllAsync()
        {
            _allItems.Clear();
            var list = await SafeGetAllAsync();

            foreach (var it in list)
                _allItems.Add(it);

            ApplyFilterAndSearch();
            UpdateStat();
        }

        private async Task<List<Item>> SafeGetAllAsync()
        {
            try
            {
                var list = await _db.GetAllItemsAsync();
                return list ?? new List<Item>();
            }
            catch
            {
                // 如果你是同步版本
                try
                {
                    var list = _db.GetAllItems();
                    return list ?? new List<Item>();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "讀取資料失敗：\n" + ex.Message, "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    return new List<Item>();
                }
            }
        }

        private async Task BuildPathTreeAsync()
        {
            _pathRoots.Clear();

            // 簡單以資料的 Path 做樹（到目錄層）
            var groups = _allItems
                .Where(i => !string.IsNullOrWhiteSpace(i.Path))
                .GroupBy(i => Path.GetDirectoryName(i.Path) ?? "");

            // 以 root 路徑群組
            foreach (var g in groups
                         .GroupBy(x => GetTopFolder(x.Key))
                         .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                var root = new PathNode { Name = g.Key, FullPath = g.Key };
                foreach (var sub in g.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    root.Children.Add(new PathNode
                    {
                        Name = sub.Key,
                        FullPath = sub.Key
                    });
                }
                _pathRoots.Add(root);
            }
        }

        private static string GetTopFolder(string full)
        {
            if (string.IsNullOrWhiteSpace(full)) return "(未知)";
            var p = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var idx = p.IndexOf(Path.DirectorySeparatorChar);
            if (idx <= 0) return p;
            return p[..idx];
        }
        #endregion

        #region UI：搜尋 / 篩選
        private void ApplyFilterAndSearch()
        {
            var kw = (SearchBox.Text ?? "").Trim();

            IEnumerable<Item> q = _allItems;

            if (!string.IsNullOrWhiteSpace(kw))
            {
                var parts = kw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                q = q.Where(it => parts.All(k =>
                    (it.Filename ?? "").IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (it.Category ?? "").IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (it.Tags ?? "").IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (it.Path ?? "").IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            if (_filterQueue) q = q.Where(it => string.Equals(it.Status, "Queue", StringComparison.OrdinalIgnoreCase));
            if (_filterFavorite) q = q.Where(it => IsFavorite(it));
            if (_filterRunning) q = q.Where(it => string.Equals(it.Status, "Running", StringComparison.OrdinalIgnoreCase));

            // 路徑鎖定（若左樹有勾選）
            var lockedPaths = CollectCheckedPaths();
            if (lockedPaths.Count > 0)
            {
                q = q.Where(it => !string.IsNullOrEmpty(it.Path) &&
                                  lockedPaths.Any(p => it.Path!.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
            }

            _viewItems.Clear();
            foreach (var it in q.OrderByDescending(i => i.CreatedTs))
                _viewItems.Add(it);

            UpdateStat();
        }

        private void UpdateStat()
        {
            StatSummary.Text = $"共 {_allItems.Count} 筆，顯示 {_viewItems.Count} 筆";
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e) => ApplyFilterAndSearch();

        private void BtnQueue_Checked(object sender, RoutedEventArgs e)
        {
            _filterQueue = true;
            ApplyFilterAndSearch();
        }

        private void BtnQueue_Unchecked(object sender, RoutedEventArgs e)
        {
            _filterQueue = false;
            ApplyFilterAndSearch();
        }

        private void BtnFavorite_Checked(object sender, RoutedEventArgs e)
        {
            _filterFavorite = true;
            ApplyFilterAndSearch();
        }

        private void BtnFavorite_Unchecked(object sender, RoutedEventArgs e)
        {
            _filterFavorite = false;
            ApplyFilterAndSearch();
        }

        private void BtnRunning_Checked(object sender, RoutedEventArgs e)
        {
            _filterRunning = true;
            ApplyFilterAndSearch();
        }

        private void BtnRunning_Unchecked(object sender, RoutedEventArgs e)
        {
            _filterRunning = false;
            ApplyFilterAndSearch();
        }
        #endregion

        #region 路徑樹
        private void PathTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            ApplyFilterAndSearch();
        }

        private void PathNode_Checked(object sender, RoutedEventArgs e) => ApplyFilterAndSearch();

        private void PathNode_Unchecked(object sender, RoutedEventArgs e) => ApplyFilterAndSearch();

        private List<string> CollectCheckedPaths()
        {
            var result = new List<string>();
            foreach (var root in _pathRoots)
                Collect(root, result);
            return result;

            void Collect(PathNode n, List<string> acc)
            {
                if (n.IsChecked && !string.IsNullOrEmpty(n.FullPath))
                    acc.Add(n.FullPath!);
                foreach (var c in n.Children)
                    Collect(c, acc);
            }
        }
        #endregion

        #region 清單：選取、右鍵、搬檔
        private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectionInfo();
            UpdatePreview();
        }

        private void UpdateSelectionInfo()
        {
            var sel = FileList.SelectedItems.Cast<Item>().ToList();
            if (sel.Count == 0)
            {
                SelectionInfo.Text = "未選取";
                return;
            }

            var cats = sel.Select(i => i.Category ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            SelectionInfo.Text = $"選取 {sel.Count} 筆；分類：{string.Join(" / ", cats)}";
        }

        private async void BtnExecuteMove_Click(object sender, RoutedEventArgs e)
        {
            // 範例：選資料夾，模擬搬檔（你可以在此呼叫 RoutingService & 寫檔）
            var dialog = new VistaFolderBrowserDialog { Description = "選擇要搬到的資料夾" };
            if (dialog.ShowDialog(this) != true) return;

            var target = dialog.SelectedPath;
            var sel = FileList.SelectedItems.Cast<Item>().ToList();
            if (sel.Count == 0) return;

            int moved = 0;
            foreach (var it in sel)
            {
                try
                {
                    if (File.Exists(it.Path))
                    {
                        var dest = Path.Combine(target, Path.GetFileName(it.Path));
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        File.Copy(it.Path!, dest, overwrite: true);
                        moved++;
                    }
                }
                catch (Exception ex)
                {
                    Log($"搬檔失敗：{it.Path} => {ex.Message}");
                }
            }

            Log($"搬檔完成：{moved}/{sel.Count}");
            await RefreshAllAsync();
        }

        private async void MarkFavorite_Click(object sender, RoutedEventArgs e)
        {
            var sel = FileList.SelectedItems.Cast<Item>().ToList();
            if (sel.Count == 0) return;

            foreach (var it in sel)
                TrySetFavorite(it, true);

            await PushTagsToDbAsync(sel);
            ApplyFilterAndSearch();
        }

        private async void UnmarkFavorite_Click(object sender, RoutedEventArgs e)
        {
            var sel = FileList.SelectedItems.Cast<Item>().ToList();
            if (sel.Count == 0) return;

            foreach (var it in sel)
                TrySetFavorite(it, false);

            await PushTagsToDbAsync(sel);
            ApplyFilterAndSearch();
        }

        private async void AddTag_Click(object sender, RoutedEventArgs e)
        {
            var sel = FileList.SelectedItems.Cast<Item>().ToList();
            if (sel.Count == 0) return;

            var tag = Prompt("新增標籤（多個以逗號分隔）", "new-tag");
            if (string.IsNullOrWhiteSpace(tag)) return;

            var add = SplitTags(tag);
            foreach (var it in sel)
            {
                var now = SplitTags(it.Tags);
                foreach (var t in add) now.Add(t);
                it.Tags = string.Join(", ", now.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            }

            await PushTagsToDbAsync(sel);
            ApplyFilterAndSearch();
        }

        private async void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            var sel = FileList.SelectedItems.Cast<Item>().ToList();
            if (sel.Count == 0) return;

            var tag = Prompt("要移除哪個標籤？", "");
            if (string.IsNullOrWhiteSpace(tag)) return;

            foreach (var it in sel)
            {
                var now = SplitTags(it.Tags);
                now.Remove(tag.Trim());
                it.Tags = string.Join(", ", now.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            }

            await PushTagsToDbAsync(sel);
            ApplyFilterAndSearch();
        }

        private async void Reclassify_Click(object sender, RoutedEventArgs e)
        {
            var sel = FileList.SelectedItems.Cast<Item>().ToList();
            if (sel.Count == 0) return;

            var cat = Prompt("設定新的『業務分類』", sel.First().Category ?? "");
            if (string.IsNullOrWhiteSpace(cat)) return;

            foreach (var it in sel) it.Category = cat.Trim();

            // 🔧 這裡如果你有 UpdateCategoryByPath(…)，可以一併寫回
            await SafeUpdateCategoryAsync(sel, cat.Trim(), confidence: 0.9, reason: "manual");
            ApplyFilterAndSearch();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.SelectedItem is not Item it || string.IsNullOrWhiteSpace(it.Path)) return;

            try
            {
                var folder = Path.GetDirectoryName(it.Path);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    Process.Start("explorer.exe", folder);
            }
            catch (Exception ex)
            {
                Log("開啟資料夾失敗：" + ex.Message);
            }
        }

        private void Undo_Click(object sender, RoutedEventArgs e) => Log("（範例）復原");
        private void Redo_Click(object sender, RoutedEventArgs e) => Log("（範例）重做");
        #endregion

        #region 預覽
        private void UpdatePreview()
        {
            var sel = FileList.SelectedItems.Cast<Item>().ToList();
            if (sel.Count == 1)
            {
                var it = sel[0];
                PreviewHeader.Text = it.Filename ?? "";

                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewText.Visibility = Visibility.Collapsed;

                if (!string.IsNullOrWhiteSpace(it.Path) && File.Exists(it.Path))
                {
                    var ext = (Path.GetExtension(it.Path) ?? "").ToLowerInvariant();
                    if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp")
                    {
                        try
                        {
                            var img = new System.Windows.Media.Imaging.BitmapImage();
                            img.BeginInit();
                            img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            img.UriSource = new Uri(it.Path);
                            img.EndInit();
                            PreviewImage.Source = img;
                            PreviewImage.Visibility = Visibility.Visible;
                            return;
                        }
                        catch { /* ignore */ }
                    }

                    if (ext is ".txt" or ".log" or ".md")
                    {
                        try
                        {
                            var text = File.ReadAllText(it.Path, Encoding.UTF8);
                            PreviewText.Text = text.Length > 50_000 ? text[..50_000] + "\n…（略）" : text;
                            PreviewText.Visibility = Visibility.Visible;
                            return;
                        }
                        catch { /* ignore */ }
                    }
                }
            }
            else
            {
                PreviewHeader.Text = sel.Count == 0 ? "（未選取）" : $"已選取 {sel.Count} 筆";
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewText.Visibility = Visibility.Collapsed;
            }
        }
        #endregion

        #region 工具列：自整理 / 使用說明 / 設定
        private async void BtnAutoClean_Click(object sender, RoutedEventArgs e)
        {
            // 範例：將空白標籤、重複標籤清掉
            int changed = 0;
            foreach (var it in _allItems)
            {
                var set = SplitTags(it.Tags);
                var newTags = string.Join(", ", set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                if (!string.Equals(newTags, it.Tags, StringComparison.Ordinal))
                {
                    it.Tags = newTags;
                    await SafeUpdateTagsAsync(it.Path, newTags);
                    changed++;
                }
            }
            Log($"自整理完成：{changed} 筆標籤被修正");
            ApplyFilterAndSearch();
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            var win = new HelpWindow();
            win.Owner = this;
            win.ShowDialog();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            // 若 SettingsWindow 需要參數，把 _config.Current 或你的 AppConfig 傳進去
            var win = new SettingsWindow();
            win.Owner = this;
            win.ShowDialog();
        }
        #endregion

        #region 小工具
        private static bool IsFavorite(Item it)
            => SplitTags(it.Tags).Contains("favorite", StringComparer.OrdinalIgnoreCase);

        private static HashSet<string> SplitTags(string? tags)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(tags)) return set;

            foreach (var s in tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(s)) set.Add(s);
            }
            return set;
        }

        private static void TrySetFavorite(Item it, bool on)
        {
            var set = SplitTags(it.Tags);
            if (on) set.Add("favorite");
            else set.Remove("favorite");
            it.Tags = string.Join(", ", set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }

        private async Task PushTagsToDbAsync(List<Item> items)
        {
            foreach (var it in items)
                await SafeUpdateTagsAsync(it.Path, it.Tags ?? "");
        }

        private async Task SafeUpdateTagsAsync(string? path, string tags)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                await _db.UpdateTagsByPath(path, tags);
            }
            catch
            {
                // 若是同步版
                try { _db.UpdateTagsByPathSync(path, tags); } catch { /* 忽略 */ }
            }
        }

        private async Task SafeUpdateCategoryAsync(List<Item> items, string category, double confidence, string reason)
        {
            // 如果你的 DbService 有批次版本（IEnumerable<string> paths,...）可集中呼叫
            var paths = items.Select(i => i.Path).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (paths.Count == 0) return;

            try
            {
                await _db.UpdateCategoryByPath(paths, category, confidence, reason);
            }
            catch
            {
                // 沒有批次的話，逐一嘗試同步/非同步
                foreach (var p in paths)
                {
                    try { await _db.UpdateCategoryByPath(p!, category, confidence, reason); }
                    catch
                    {
                        try { _db.UpdateCategoryByPathSync(p!, category, confidence, reason); }
                        catch { /* 忽略 */ }
                    }
                }
            }
        }

        private string Prompt(string title, string? seed = null)
        {
            var dlg = new InputDialog(title, seed ?? "");
            if (dlg.ShowDialog(this) == true)
                return dlg.Value ?? "";
            return "";
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e) => TxtLog.Clear();

        private void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            TxtLog.AppendText(line + Environment.NewLine);
            TxtLog.ScrollToEnd();
        }
        #endregion
    }

    /// <summary>簡易輸入對話框（避免引用 WinForms）</summary>
    internal sealed class InputDialog : Window
    {
        private readonly TextBox _box;
        public string? Value => _box.Text;

        public InputDialog(string title, string seed)
        {
            Title = title;
            Width = 420;
            Height = 160;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            _box = new TextBox { Text = seed, MinWidth = 360, Height = 26 };
            Grid.SetRow(_box, 1);
            grid.Children.Add(_box);

            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "確定", Width = 80, Margin = new Thickness(0, 12, 8, 0), IsDefault = true };
            var cancel = new Button { Content = "取消", Width = 80, Margin = new Thickness(0, 12, 0, 0), IsCancel = true };
            ok.Click += (_, __) => { DialogResult = true; Close(); };
            panel.Children.Add(ok);
            panel.Children.Add(cancel);
            Grid.SetRow(panel, 2);
            grid.Children.Add(panel);

            Content = grid;
        }
    }
}
