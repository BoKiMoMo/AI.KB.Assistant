using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AI.KB.Assistant.Views
{
    public partial class MainWindow : Window
    {
        private readonly string _cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        private AppConfig _cfg = new();
        private DbService _db = null!;
        private RoutingService _routing = null!;
        private LlmService _llm = null!;
        private IntakeService _intake = null!;

        private string? _currentDir;
        private GridViewColumnHeader? _lastHeader;
        private ListSortDirection _dir = ListSortDirection.Descending;

        public MainWindow()
        {
            InitializeComponent();

            InitServices();
            BuildTopPaths();
            BuildFolderTreeRoots();
            RefreshList(_cfg.Import.HotFolderPath);
        }

        private void InitServices()
        {
            _cfg = ConfigService.TryLoad(_cfgPath);
            ThemeService.Apply(_cfg);

            Directory.CreateDirectory(Path.GetDirectoryName(_cfg.App.DbPath!)!);

            _db = new DbService(_cfg.App.DbPath!);
            _routing = new RoutingService(_cfg);
            _llm = new LlmService(_cfg);
            _intake = new IntakeService(_cfg, _db, _routing, _llm);

            if (FindName("RtThreshold") is Slider sl)
            {
                sl.Value = _cfg.Classification.ConfidenceThreshold;
                if (FindName("RtThresholdValue") is TextBlock tv) tv.Text = sl.Value.ToString("0.00");
                sl.ValueChanged += (_, __) =>
                {
                    if (FindName("RtThresholdValue") is TextBlock tv2)
                        tv2.Text = sl.Value.ToString("0.00");
                };
            }
        }

        private sealed class TopPath { public string Label { get; set; } = ""; public string Path { get; set; } = ""; }

        private void BuildTopPaths()
        {
            var list = new[]
            {
                new TopPath{ Label="ROOT", Path=_cfg.App.RootDir ?? "" },
                new TopPath{ Label="收件夾", Path=_cfg.Import.HotFolderPath ?? "" },
                new TopPath{ Label="桌面", Path=Environment.GetFolderPath(Environment.SpecialFolder.Desktop) },
                new TopPath{ Label="DB", Path=_cfg.App.DbPath ?? "" },
            };
            if (FindName("TopPaths") is ItemsControl ic) ic.ItemsSource = list;
        }

        // 左側樹
        private void BuildFolderTreeRoots()
        {
            if (FindName("TvFolders") is not TreeView tv) return;
            tv.Items.Clear();

            var roots = new[] { _cfg.App.RootDir, _cfg.Import.HotFolderPath, Environment.GetFolderPath(Environment.SpecialFolder.Desktop) }
                .Where(s => !string.IsNullOrWhiteSpace(s) && Directory.Exists(s!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(s => s!)
                .ToList();

            foreach (var root in roots)
            {
                var t = new TreeViewItem { Header = new DirectoryInfo(root).Name, Tag = root };
                t.Items.Add("*");
                t.Expanded += DirNode_Expanded;
                tv.Items.Add(t);
            }
        }

        private void DirNode_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is not TreeViewItem tvi) return;
            if (tvi.Items.Count == 1 && Equals(tvi.Items[0], "*"))
            {
                tvi.Items.Clear();
                var dir = tvi.Tag?.ToString();
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;

                foreach (var d in Directory.EnumerateDirectories(dir))
                {
                    var name = new DirectoryInfo(d).Name;
                    if (_routing.ShouldHideFolder(name)) continue;
                    var child = new TreeViewItem { Header = name, Tag = d };
                    child.Items.Add("*");
                    child.Expanded += DirNode_Expanded;
                    tvi.Items.Add(child);
                }
            }
        }

        private void TvFolders_SelectedItemChanged(object s, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem tvi) RefreshList(tvi.Tag?.ToString());
        }

        // 中清單
        private class FileRow
        {
            public string Filename { get; set; } = "";
            public string Ext { get; set; } = "";
            public string Project { get; set; } = "";
            public string Tags { get; set; } = "";
            public string Path { get; set; } = "";
            public string ProposedPath { get; set; } = "";
            public DateTime CreatedTs { get; set; }
        }

        public void RefreshList(string? dir = null)
        {
            var target = dir ?? _currentDir ?? _cfg.Import.HotFolderPath!;
            _currentDir = target;

            if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
            {
                if (FindName("FileList") is ListView lv0) lv0.ItemsSource = Array.Empty<FileRow>();
                if (FindName("TxtCounter") is TextBlock c0) c0.Text = "0 項";
                return;
            }

            var rows = new List<FileRow>();
            foreach (var f in Directory.EnumerateFiles(target))
            {
                rows.Add(new FileRow
                {
                    Filename = System.IO.Path.GetFileName(f),
                    Ext = System.IO.Path.GetExtension(f).Trim('.').ToLowerInvariant(),
                    Project = new DirectoryInfo(System.IO.Path.GetDirectoryName(f) ?? "").Name,
                    Tags = "",
                    Path = f,
                    CreatedTs = File.GetCreationTime(f),
                    ProposedPath = _routing.PreviewDestPath(f, _cfg.App.ProjectLock, null)
                });
            }

            if (FindName("FileList") is ListView lv) lv.ItemsSource = rows;
            if (FindName("TxtCounter") is TextBlock c) c.Text = $"{rows.Count} 項";
            ApplySort("CreatedTs", ListSortDirection.Descending);
        }

        private void ApplySort(string property, ListSortDirection direction)
        {
            if (FindName("FileList") is not ListView lv) return;
            ICollectionView view = CollectionViewSource.GetDefaultView(lv.ItemsSource);
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(property, direction));
            view.Refresh();
        }

        private void ListHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not GridViewColumnHeader h) return;
            var key = h.Tag?.ToString() ?? "CreatedTs";
            _dir = (_lastHeader == h && _dir == ListSortDirection.Ascending)
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
            _lastHeader = h;
            h.Content = $"{h.Content.ToString()?.Split(' ')[0]} {(_dir == ListSortDirection.Ascending ? "▲" : "▼")}";
            ApplySort(key, _dir);
        }

        private void List_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (FindName("FileList") is not ListView lv || lv.SelectedItem is not FileRow row) return;
            try { ProcessStart(row.Path); } catch { }
        }

        private static void ProcessStart(string target)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + target + "\""); } catch { }
        }

        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentDir != null) RefreshList(_currentDir);
        }

        // 工具列
        private void BtnAddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "All Files|*.*" };
            if (dlg.ShowDialog(this) == true)
            {
                _ = Task.Run(async () =>
                {
                    int added = 0;
                    foreach (var f in dlg.FileNames) { try { await _intake.StageOnlyAsync(f, CancellationToken.None); added++; } catch { } }
                    Dispatcher.Invoke(() => { ShowBanner($"已加入 {added} 檔案至收件清單。"); RefreshList(_currentDir); });
                });
            }
        }

        private void BtnStartClassify_Click(object sender, RoutedEventArgs e)
        {
            _ = Task.Run(async () =>
            {
                int cnt = 0; var dir = _currentDir ?? _cfg.Import.HotFolderPath!;
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                    try { await _intake.ClassifyOnlyAsync(f, CancellationToken.None); cnt++; } catch { }
                Dispatcher.Invoke(() => { ShowBanner($"已完成預分類 {cnt} 筆。"); RefreshList(_currentDir); });
            });
        }

        private void BtnCommit_Click(object sender, RoutedEventArgs e)
        {
            _ = Task.Run(async () =>
            {
                int moved = await _intake.CommitPendingAsync(_cfg.Import.OverwritePolicy, _cfg.Import.MoveMode == MoveMode.Copy, CancellationToken.None);
                Dispatcher.Invoke(() => { ShowBanner($"搬檔完成：{moved} 筆。"); RefreshList(_currentDir); });
            });
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            BuildFolderTreeRoots();
            RefreshList(_currentDir);
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var w = new SettingsWindow(this, _cfg);
            if (w.ShowDialog() == true)
            {
                InitServices();
                BuildTopPaths();
                BuildFolderTreeRoots();
                RefreshList(_currentDir);
                ShowBanner("設定已重新載入。");
            }
        }

        private void BtnOpenInbox_Click(object sender, RoutedEventArgs e)
        {
            var p = _cfg.Import?.HotFolderPath;
            if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                System.Diagnostics.Process.Start("explorer.exe", p);
        }

        private void BtnEdgeLeft_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("LeftPaneColumn") is ColumnDefinition col)
                col.Width = col.Width.Value > 0 ? new GridLength(0) : new GridLength(280);
        }

        private void BtnEdgeRight_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("RightPaneColumn") is ColumnDefinition col)
                col.Width = col.Width.Value > 0 ? new GridLength(0) : new GridLength(320);
        }

        // 樹右鍵
        private string? GetTreeSelectedPath()
        {
            if (FindName("TvFolders") is not TreeView tv) return null;
            return (tv.SelectedItem as TreeViewItem)?.Tag?.ToString();
        }

        private void Tree_OpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            var dir = GetTreeSelectedPath() ?? _currentDir;
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                System.Diagnostics.Process.Start("explorer.exe", dir!);
        }

        private async void Tree_MoveFolderToInbox_Click(object sender, RoutedEventArgs e)
        {
            var root = GetTreeSelectedPath();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

            var opt = _cfg.Import.IncludeSubdir ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            int staged = 0;
            await Task.Run(async () =>
            {
                foreach (var f in Directory.EnumerateFiles(root!, "*", opt))
                    try { await _intake.StageOnlyAsync(f, CancellationToken.None); staged++; } catch { }
            });

            ShowBanner($"已加入 {staged} 檔案到收件清單。");
        }

        private void Tree_LockProject_Click(object sender, RoutedEventArgs e)
        {
            var dir = GetTreeSelectedPath(); if (string.IsNullOrWhiteSpace(dir)) return;
            _cfg.App.ProjectLock = new DirectoryInfo(dir!).Name;
            ShowBanner($"已鎖定專案：{_cfg.App.ProjectLock}");
        }

        private void Tree_UnlockProject_Click(object sender, RoutedEventArgs e)
        {
            _cfg.App.ProjectLock = null; ShowBanner("已解除專案鎖定。");
        }

        private void Tree_Rename_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("重新命名請於檔案總管操作。", "提示");
        }

        // 右欄 AI
        private void BtnGenTags_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("FileList") is not ListView lv || lv.SelectedItem is not FileRow row) return;
            _ = Task.Run(async () =>
            {
                var tags = await _llm.SuggestTagsAsync(row.Path, CancellationToken.None);
                Dispatcher.Invoke(() =>
                {
                    if (FindName("RtMeta") is TextBlock tb)
                        tb.Text = "建議標籤：" + string.Join(", ", tags);
                });
            });
        }

        private void BtnSummarize_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("FileList") is not ListView lv || lv.SelectedItem is not FileRow row) return;
            _ = Task.Run(async () =>
            {
                var text = await _llm.SummarizeAsync(row.Path, CancellationToken.None);
                Dispatcher.Invoke(() =>
                {
                    if (FindName("RtMeta") is TextBlock tb)
                        tb.Text = text;
                });
            });
        }

        private void BtnAnalyzeConfidence_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("FileList") is not ListView lv || lv.SelectedItem is not FileRow row) return;
            _ = Task.Run(async () =>
            {
                var score = await _llm.AnalyzeConfidenceAsync(row.Path, CancellationToken.None);
                Dispatcher.Invoke(() => ShowBanner($"信心分數：{score:0.00}"));
            });
        }

        private void BtnApplyAiTuning_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("RtBlacklist") is TextBox tb)
            {
                _cfg.Import.BlacklistFolderNames = tb.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
                ConfigService.Save(_cfg, _cfgPath);
                BuildFolderTreeRoots();
                ShowBanner("已更新黑名單資料夾。");
            }
        }

        private void BtnReloadSettings_Click(object sender, RoutedEventArgs e)
        {
            InitServices(); BuildTopPaths(); BuildFolderTreeRoots(); RefreshList(_currentDir);
            ShowBanner("設定已重新載入。");
        }

        private void ShowBanner(string text)
        {
            if (FindName("Banner") is Border bd && FindName("BannerText") is TextBlock tb)
            { tb.Text = text; bd.Visibility = Visibility.Visible; }
        }

        // 工具列：搜尋/鎖定專案
        private void BtnSearchProject_Click(object sender, RoutedEventArgs e)
        {
            var tb = FindName("CbLockProject") as ComboBox;
            var key = (tb?.Text ?? "").Trim();
            if (!string.IsNullOrEmpty(key))
            {
                _cfg.App.ProjectLock = key;
                ShowBanner($"已將專案鎖定為：{key}");
                RefreshList(_currentDir);
            }
            else
            {
                ShowBanner("請輸入欲鎖定/搜尋的專案名稱。");
            }
        }

        private void BtnLockProject_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_cfg.App.ProjectLock))
            {
                _cfg.App.ProjectLock = null;
                ShowBanner("已解除專案鎖定。");
            }
            else
            {
                var dir = _currentDir ?? _cfg.Import.HotFolderPath ?? "";
                var name = new DirectoryInfo(string.IsNullOrEmpty(dir) ? Environment.CurrentDirectory : dir).Name;
                _cfg.App.ProjectLock = name;
                ShowBanner($"已鎖定專案：{name}");
            }
            RefreshList(_currentDir);
        }
    }
}
