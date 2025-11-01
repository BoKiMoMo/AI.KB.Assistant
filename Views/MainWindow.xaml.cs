// Views/MainWindow.xaml.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views
{
    public partial class MainWindow : Window
    {
        // ===== UI Row =====
        private sealed class UiRow
        {
            public Item Item { get; }
            public UiRow(Item it)
            {
                Item = it;
                FileName = Path.GetFileName(it.Path);
                Ext = (Path.GetExtension(it.Path) ?? "").Trim('.').ToLowerInvariant();
                Project = it.Project ?? "";
                Tags = it.Tags == null ? "" : string.Join(",", it.Tags);
                SourcePath = it.Path;
                DestPath = it.ProposedPath ?? "";
                CreatedAt = it.Timestamp ?? it.UpdatedAt;
                Status = string.IsNullOrWhiteSpace(it.Status) ? "intaked" : it.Status!;
            }

            public string FileName { get; }
            public string Ext { get; }
            public string Project { get; set; }
            public string Tags { get; set; }
            public string SourcePath { get; }
            public string DestPath { get; set; }
            public DateTime CreatedAt { get; }
            public string Status { get; set; }
        }

        private readonly ObservableCollection<UiRow> _rows = new();
        private ListCollectionView? _view;

        // TreeView lazy-load tag
        private sealed class FolderNode
        {
            public string Name { get; set; } = "";
            public string FullPath { get; set; } = "";
            public override string ToString() => Name;
        }

        // 服務
        private T? Get<T>(string key) where T : class => Application.Current?.Resources[key] as T;
        private DbService? Db => Get<DbService>("Db");
        private IntakeService? Intake => Get<IntakeService>("Intake");
        private RoutingService? Router => Get<RoutingService>("Router");
        private LlmService? Llm => Get<LlmService>("Llm");

        // 路徑欄寬暫存
        private double _srcWidth = 300;
        private double _dstWidth = 320;

        // 排序狀態
        private string _sortKey = "CreatedAt";
        private ListSortDirection _sortDir = ListSortDirection.Descending;

        // Converters expose
        public static readonly IValueConverter StatusToLabelConverterInstance = new StatusToLabelConverter();
        public static readonly IMultiValueConverter StatusToBrushConverterInstance = new StatusToBrushConverter();

        public MainWindow()
        {
            InitializeComponent();

            // 綁清單
            MainList.ItemsSource = _rows;
            _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_rows);
            ApplySort(_sortKey, _sortDir);

            // Left tree: 事件以 AddHandler 綁（避免 XAML Expanded 屬性造成 MC3072）
            TvFolders.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(TvFolders_Expanded));

            // 設定異動
            ConfigService.ConfigChanged += (_, cfg) =>
            {
                try { Router?.ApplyConfig(cfg); Llm?.UpdateConfig(cfg); } catch { }
                _ = RefreshFromDbAsync();
                LoadFolderRoot(); // 也重載側邊樹
            };

            Loaded += async (_, __) =>
            {
                LoadFolderRoot();
                if (FindName("RtThresholdValue") is TextBlock t) t.Text = $"{(FindName("RtThreshold") as Slider)?.Value:0.00}";
                await RefreshFromDbAsync();
            };
        }

        // ===== DB → UI =====
        private async Task RefreshFromDbAsync()
        {
            try
            {
                TxtCounterSafe("讀取中…");
                _rows.Clear();

                if (Db == null) { TxtCounterSafe("DB 尚未初始化"); return; }

                var items = await Db.QueryAllAsync();
                foreach (var it in items)
                {
                    if (string.IsNullOrWhiteSpace(it.ProposedPath) && Router != null)
                        it.ProposedPath = Router.PreviewDestPath(it.Path);

                    _rows.Add(new UiRow(it));
                }

                ApplySort(_sortKey, _sortDir);
                TxtCounterSafe($"共 {_rows.Count} 筆");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "重新整理失敗");
                TxtCounterSafe("讀取失敗");
            }
        }

        private void TxtCounterSafe(string text)
        {
            if (FindName("TxtCounter") is TextBlock t) t.Text = text;
        }

        // ===== Toolbar =====
        private async void BtnAddFiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Intake == null || Router == null) { MessageBox.Show("服務尚未初始化（Intake / Router）。"); return; }

                var dlg = new OpenFileDialog { Title = "選擇要加入的檔案", Multiselect = true, CheckFileExists = true };
                if (dlg.ShowDialog(this) != true) return;

                var added = await Intake.IntakeFilesAsync(dlg.FileNames);
                foreach (var it in added.Where(a => a != null))
                {
                    it.ProposedPath = Router.PreviewDestPath(it.Path);
                    _rows.Insert(0, new UiRow(it));
                }

                ApplySort(_sortKey, _sortDir);
                TxtCounterSafe($"共 {_rows.Count} 筆");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "加入檔案失敗"); }
        }

        private async void BtnCommit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Router == null || Db == null) { MessageBox.Show("服務尚未初始化（Router / Db）。"); return; }

                var selected = GetSelectedUiRows();
                if (selected.Length == 0) { MessageBox.Show("請先在清單中選取要提交的項目。"); return; }

                int ok = 0;
                foreach (var row in selected)
                {
                    var it = row.Item;
                    if (string.IsNullOrWhiteSpace(it.ProposedPath))
                        it.ProposedPath = Router.PreviewDestPath(it.Path);

                    var final = Router.Commit(it);
                    if (!string.IsNullOrWhiteSpace(final))
                    {
                        it.Status = "committed";
                        it.ProposedPath = final;
                        row.Status = "committed";
                        row.DestPath = final;
                        ok++;
                    }
                }

                if (ok > 0) await Db.UpdateItemsAsync(selected.Select(r => r.Item).ToArray());
                CollectionViewSource.GetDefaultView(_rows)?.Refresh();
                MessageBox.Show($"完成提交：{ok} / {selected.Length}");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "提交失敗"); }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await RefreshFromDbAsync();

        private void BtnOpenHot_Click(object sender, RoutedEventArgs e) => OpenInExplorer(AppConfig.Current?.Import?.HotFolderPath ?? AppConfig.Current?.Import?.HotFolder);
        private void BtnOpenRoot_Click(object sender, RoutedEventArgs e) => OpenInExplorer(AppConfig.Current?.Routing?.RootDir ?? AppConfig.Current?.App?.RootDir);
        private void BtnOpenDb_Click(object sender, RoutedEventArgs e)
        {
            var p = AppConfig.Current?.Db?.DbPath ?? AppConfig.Current?.Db?.Path;
            if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) TryStart(p);
            else OpenInExplorer(Path.GetDirectoryName(p ?? string.Empty));
        }
        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            try { new SettingsWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "開啟設定失敗"); }
        }

        // ===== 排序 =====
        private void ListHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not GridViewColumnHeader h) return;
            var text = (h.Content as string)?.Trim() ?? "";
            if (string.IsNullOrEmpty(text)) return;

            string key = text switch
            {
                "檔名" => "FileName",
                "副檔名" => "Ext",
                "狀態" => "Status",
                "專案" => "Project",
                "標籤" => "Tags",
                "路徑" => "SourcePath",
                "預計路徑" => "DestPath",
                "建立時間" => "CreatedAt",
                _ => "CreatedAt"
            };

            _sortDir = (_sortKey == key && _sortDir == ListSortDirection.Ascending)
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            _sortKey = key;
            ApplySort(_sortKey, _sortDir);
        }

        private void ApplySort(string key, ListSortDirection dir)
        {
            if (_view == null) return;

            IComparer cmp = key switch
            {
                "FileName" => new PropComparer<UiRow>(r => r.FileName, dir),
                "Ext" => new CategoryComparer(dir, Router),
                "Status" => new StatusComparer(dir),
                "Project" => new PropComparer<UiRow>(r => r.Project, dir),
                "Tags" => new PropComparer<UiRow>(r => r.Tags, dir),
                "SourcePath" => new PropComparer<UiRow>(r => r.SourcePath, dir),
                "DestPath" => new PropComparer<UiRow>(r => r.DestPath, dir),
                "CreatedAt" => new DateComparer(dir),
                _ => new DateComparer(dir)
            };

            _view.CustomSort = cmp;
            _view.Refresh();
        }

        // ===== 右鍵功能（中清單） =====
        private void CmOpenFile_Click(object sender, RoutedEventArgs e)
        {
            var row = GetSelectedUiRows().FirstOrDefault(); if (row == null) return;
            try
            {
                if (File.Exists(row.SourcePath))
                    Process.Start(new ProcessStartInfo { FileName = row.SourcePath, UseShellExecute = true });
                else MessageBox.Show("找不到檔案。");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "開啟檔案失敗"); }
        }
        private void CmRevealInExplorer_Click(object sender, RoutedEventArgs e)
        {
            var row = GetSelectedUiRows().FirstOrDefault(); if (row == null) return;
            OpenInExplorer(row.SourcePath);
        }
        private void CmCopySourcePath_Click(object sender, RoutedEventArgs e)
        {
            var txt = string.Join(Environment.NewLine, GetSelectedUiRows().Select(r => r.SourcePath));
            if (!string.IsNullOrWhiteSpace(txt)) Clipboard.SetText(txt);
        }
        private void CmCopyDestPath_Click(object sender, RoutedEventArgs e)
        {
            var txt = string.Join(Environment.NewLine, GetSelectedUiRows().Select(r => r.DestPath));
            if (!string.IsNullOrWhiteSpace(txt)) Clipboard.SetText(txt);
        }
        private async void CmAddTags_Click(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedUiRows();
            if (rows.Length == 0) { MessageBox.Show("請先選取資料列"); return; }

            var existingTags = _rows.SelectMany(r => (r.Item.Tags ?? new List<string>())).Distinct().OrderBy(s => s).ToList();

            // 簡易多選視窗
            var win = new Window
            {
                Title = "添加標籤",
                Width = 420,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = (Brush)FindResource("App.PanelBrush")
            };
            var root = new DockPanel { Margin = new Thickness(12) };
            win.Content = root;

            var list = new ListBox { SelectionMode = SelectionMode.Multiple, Height = 360 };
            foreach (var t in existingTags) list.Items.Add(t);

            var tb = new TextBox { Margin = new Thickness(0, 8, 0, 0), ToolTip = "輸入新標籤後按 Enter 新增" };
            tb.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter && !string.IsNullOrWhiteSpace(tb.Text))
                {
                    var nt = tb.Text.Trim();
                    if (!list.Items.Contains(nt)) list.Items.Add(nt);
                    tb.Clear();
                }
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "確定", Width = 80, Margin = new Thickness(8, 0, 0, 0) };
            var cancel = new Button { Content = "取消", Width = 80 };
            ok.Click += (_, __) => win.DialogResult = true;
            cancel.Click += (_, __) => win.DialogResult = false;

            DockPanel.SetDock(sp, Dock.Bottom);
            DockPanel.SetDock(tb, Dock.Bottom);
            root.Children.Add(sp);
            root.Children.Add(tb);
            root.Children.Add(list);
            sp.Children.Add(cancel);
            sp.Children.Add(ok);

            if (win.ShowDialog() == true)
            {
                var picked = list.SelectedItems.Cast<string>().ToList();
                if (picked.Count == 0) { MessageBox.Show("未選取標籤"); return; }

                foreach (var r in rows)
                {
                    var set = (r.Item.Tags ?? new List<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in picked) set.Add(p);
                    r.Item.Tags = set.ToList();
                    r.Tags = string.Join(",", set);
                }

                try
                {
                    if (Db != null) await Db.UpdateItemsAsync(rows.Select(x => x.Item).ToArray());
                    CollectionViewSource.GetDefaultView(_rows)?.Refresh();
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "更新標籤失敗"); }
            }
        }
        private void CmStageToInbox_Click(object sender, RoutedEventArgs e) { /* TODO: V7.4 */ }
        private void CmClassify_Click(object sender, RoutedEventArgs e) { /* TODO: V7.4 */ }
        private void CmCommit_Click(object sender, RoutedEventArgs e) => BtnCommit_Click(sender, e);
        private void CmDeleteRecord_Click(object sender, RoutedEventArgs e) { /* TODO: V7.4 */ }

        // ===== 右側資訊欄 =====
        private void MainList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = GetSelectedUiRows().FirstOrDefault();
            if (row == null)
            {
                SetRtDetail("", "", "", "", "", "");
                RtSuggestedProject.ItemsSource = null;
                return;
            }

            try
            {
                string size, created, modified;
                if (File.Exists(row.SourcePath))
                {
                    var fi = new FileInfo(row.SourcePath);
                    size = $"{fi.Length:n0} bytes";
                    created = fi.CreationTime.ToString("yyyy-MM-dd HH:mm");
                    modified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                }
                else
                {
                    size = "-";
                    created = row.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                    modified = "-";
                }

                SetRtDetail(row.FileName, row.Ext, size, created, modified, StatusToLabel(row.Status));

                // 建議專案
                var candidates = new List<string>();
                candidates.AddRange(ExtractFolders(row.SourcePath));
                candidates.AddRange(ExtractFolders(row.DestPath));
                candidates.AddRange(_rows.Select(r => r.Project).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct());
                var unique = candidates.Where(s => !string.IsNullOrWhiteSpace(s))
                                       .Distinct(StringComparer.OrdinalIgnoreCase)
                                       .Take(20)
                                       .ToList();
                RtSuggestedProject.ItemsSource = unique;
                if (unique.Count > 0) RtSuggestedProject.SelectedIndex = 0;
            }
            catch { }
        }

        private void SetRtDetail(string name, string ext, string size, string created, string modified, string status)
        {
            if (FindName("RtName") is TextBlock a) a.Text = name;
            if (FindName("RtExt") is TextBlock b) b.Text = ext;
            if (FindName("RtSize") is TextBlock c) c.Text = size;
            if (FindName("RtCreated") is TextBlock d) d.Text = created;
            if (FindName("RtModified") is TextBlock e) e.Text = modified;
            if (FindName("RtStatus") is TextBlock f) f.Text = status;
        }

        private static IEnumerable<string> ExtractFolders(string? path)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(path))
                return result;

            try
            {
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(dir))
                    return result;

                var parts = dir
                    .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Reverse()
                    .Take(3);

                result.AddRange(parts);
            }
            catch { }
            return result;
        }

        private async void BtnApplySuggestedProject_Click(object sender, RoutedEventArgs e)
        {
            var row = GetSelectedUiRows().FirstOrDefault();
            if (row == null) return;

            var proj = (RtSuggestedProject.SelectedItem as string) ?? RtSuggestedProject.Text?.Trim();
            if (string.IsNullOrWhiteSpace(proj)) return;

            row.Project = proj;
            row.Item.Project = proj;
            try
            {
                if (Db != null) await Db.UpdateItemsAsync(new[] { row.Item });
                CollectionViewSource.GetDefaultView(_rows)?.Refresh();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "更新專案失敗"); }
        }

        private void RtThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
            => (FindName("RtThresholdValue") as TextBlock)!.Text = $"{e.NewValue:0.00}";

        // ===== 左側：樹狀 + 麵包屑 =====
        private void LoadFolderRoot()
        {
            try
            {
                TvFolders.Items.Clear();

                // 1) ROOT
                var root = AppConfig.Current?.Routing?.RootDir;
                if (string.IsNullOrWhiteSpace(root))
                    root = AppConfig.Current?.App?.RootDir;

                if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                {
                    var n = MakeNode(root);
                    var tvi = MakeTvi(n);
                    tvi.Header = $"ROOT：{n.Name}";
                    TvFolders.Items.Add(tvi);
                }

                // 2) 桌面（依勾選）
                if (ChkShowDesktop.IsChecked == true)
                {
                    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    if (!string.IsNullOrWhiteSpace(desktop) && Directory.Exists(desktop))
                        TvFolders.Items.Add(MakeTvi(new FolderNode { Name = "桌面", FullPath = desktop }));
                }

                // 3) 磁碟（依勾選）
                if (ChkShowDrives.IsChecked == true)
                {
                    foreach (var d in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed && x.IsReady))
                    {
                        var name = d.Name.TrimEnd(Path.DirectorySeparatorChar);
                        TvFolders.Items.Add(MakeTvi(new FolderNode { Name = name, FullPath = name + Path.DirectorySeparatorChar }));
                    }
                }

                // 4) 收件夾（HotFolder）
                var hot = AppConfig.Current?.Import?.HotFolderPath ?? AppConfig.Current?.Import?.HotFolder;
                if (!string.IsNullOrWhiteSpace(hot) && Directory.Exists(hot))
                {
                    var tvi = MakeTvi(new FolderNode { Name = "收件夾", FullPath = hot });
                    TvFolders.Items.Add(tvi);
                }

                // 選擇第一個，以便麵包屑顯示
                if (TvFolders.Items.Count > 0 && TvFolders.Items[0] is TreeViewItem first)
                    first.IsSelected = true;
            }
            catch { /* ignore */ }
        }

        private static FolderNode MakeNode(string path)
        {
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(name)) name = trimmed;
            return new FolderNode { Name = name, FullPath = trimmed };
        }

        private TreeViewItem MakeTvi(FolderNode node)
        {
            var tvi = new TreeViewItem { Header = node.Name, Tag = node };
            try
            {
                if (Directory.Exists(node.FullPath) &&
                    Directory.EnumerateDirectories(node.FullPath).Any())
                {
                    tvi.Items.Add(new TreeViewItem { Header = "…" }); // dummy
                }
            }
            catch { }
            return tvi;
        }

        private void TvFolders_Expanded(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not TreeViewItem tvi) return;
            if (tvi.Items.Count == 1 && tvi.Items[0] is TreeViewItem dummy && (string)dummy.Header == "…")
            {
                tvi.Items.Clear();
                if (tvi.Tag is not FolderNode node) return;
                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(node.FullPath))
                    {
                        var child = MakeTvi(MakeNode(sub));
                        tvi.Items.Add(child);
                    }
                }
                catch { }
            }
        }

        private void TvFolders_SelectedItemChanged(object? sender, RoutedPropertyChangedEventArgs<object> e)
        {
            try
            {
                if (TvFolders.SelectedItem is not TreeViewItem tvi || tvi.Tag is not FolderNode node) return;

                // 麵包屑：由根到子
                var stack = new List<FolderNode>();
                var cur = tvi;
                while (cur != null)
                {
                    if (cur.Tag is FolderNode fn) stack.Add(fn);
                    cur = cur.Parent as TreeViewItem;
                }
                stack.Reverse();
                Breadcrumb.ItemsSource = stack;
            }
            catch { }
        }

        private void TreeToggles_Changed(object sender, RoutedEventArgs e) => LoadFolderRoot();

        private void Breadcrumb_Click(object sender, RoutedEventArgs e)
        {
            // TODO：定位到左樹節點（必要時再補）
        }

        // ====== 缺少的事件：提供安全 No-Op 或簡單行為 ======
        private void BtnStartClassify_Click(object sender, RoutedEventArgs e)
        {
            // TODO V7.4: 跑 RoutingService 預測並填入 ProposedPath
            _ = RefreshFromDbAsync();
        }

        private void BtnEdgeLeft_Click(object sender, RoutedEventArgs e)
        {
            if (LeftPaneColumn.Width.Value > 0) LeftPaneColumn.Width = new GridLength(0);
            else LeftPaneColumn.Width = new GridLength(300);
        }

        private void BtnEdgeRight_Click(object sender, RoutedEventArgs e)
        {
            if (RightPaneColumn.Width.Value > 0) RightPaneColumn.Width = new GridLength(0);
            else RightPaneColumn.Width = new GridLength(360);
        }

        private void BtnSearchProject_Click(object sender, RoutedEventArgs e) { }
        private void BtnLockProject_Click(object sender, RoutedEventArgs e) { MessageBox.Show("專案鎖定：尚未實作（V7.4）"); }
        private void TvFilterBox_TextChanged(object sender, TextChangedEventArgs e) { }

        private void Tree_MoveFolderToInbox_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("整份資料夾加入收件夾：尚未實作（V7.4）");
        }

        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private void List_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            var row = GetSelectedUiRows().FirstOrDefault();
            if (row == null) return;
            try
            {
                if (File.Exists(row.SourcePath))
                    Process.Start(new ProcessStartInfo { FileName = row.SourcePath, UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "開啟檔案失敗"); }
        }

        private void BtnGenTags_Click(object sender, RoutedEventArgs e) { MessageBox.Show("產生建議：V7.5 接 AI 後啟用"); }
        private void BtnSummarize_Click(object sender, RoutedEventArgs e) { MessageBox.Show("摘要：V7.5 接 AI 後啟用"); }
        private void BtnAnalyzeConfidence_Click(object sender, RoutedEventArgs e) { MessageBox.Show("信心分析：V7.5 接 AI 後啟用"); }

        private void BtnApplyAiTuning_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfg = ConfigService.Cfg;

                if (FindName("RtThreshold") is Slider s)
                    cfg.Routing.Threshold = s.Value;

                if (FindName("RtBlacklist") is TextBox tb)
                {
                    var list = (tb.Text ?? "")
                        .Split(new[] { ',', ';', '；', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    cfg.Routing.BlacklistFolderNames = list;
                }

                ConfigService.Save();
                MessageBox.Show("已套用。");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "套用失敗"); }
        }

        private void BtnReloadSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ConfigService.Load();
                MessageBox.Show("設定已重新載入。");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "重新載入失敗"); }
        }

        // ===== Helpers =====
        private UiRow[] GetSelectedUiRows() => MainList.SelectedItems.Cast<UiRow>().ToArray();

        private static void OpenInExplorer(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) { MessageBox.Show("路徑為空。"); return; }
            if (File.Exists(path)) TryStart("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path)) TryStart("explorer.exe", $"\"{path}\"");
            else MessageBox.Show($"找不到路徑：{path}");
        }
        private static void TryStart(string fileName, string? args = null)
        {
            try { Process.Start(new ProcessStartInfo { FileName = fileName, Arguments = args ?? "", UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "啟動失敗"); }
        }

        private static string StatusToLabel(string? s)
        {
            var v = (s ?? "").ToLowerInvariant();
            return v switch
            {
                "committed" => "已提交",
                "error" => "錯誤",
                "" or null => "未處理",
                "intaked" => "未處理",
                _ when v.StartsWith("stage") => "暫存",
                _ => v
            };
        }

        // ===== Converters & Comparers =====
        private sealed class StatusToLabelConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => StatusToLabel(value as string);
            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
                => Binding.DoNothing;
        }

        private sealed class StatusToBrushConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                var status = (values[0] as string)?.ToLowerInvariant() ?? "";
                Brush pick(string key) => key switch
                {
                    "commit" => new SolidColorBrush(Color.FromRgb(0x34, 0xA8, 0x53)),
                    "stage" => new SolidColorBrush(Color.FromRgb(0xF4, 0xB4, 0x00)),
                    "error" => new SolidColorBrush(Color.FromRgb(0xEA, 0x43, 0x35)),
                    _ => new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6)),
                };
                return status switch
                {
                    "committed" => pick("commit"),
                    "error" => pick("error"),
                    "" or null => pick("unset"),
                    "intaked" => pick("unset"),
                    _ when status.StartsWith("stage") => pick("stage"),
                    _ => pick("unset")
                };
            }
            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
                => throw new NotSupportedException();
        }

        private sealed class PropComparer<T> : IComparer
        {
            private readonly Func<T, string> _selector;
            private readonly ListSortDirection _dir;
            public PropComparer(Func<T, string> selector, ListSortDirection dir) { _selector = selector; _dir = dir; }
            public int Compare(object? x, object? y)
            {
                var a = x is T tx ? _selector(tx) : string.Empty;
                var b = y is T ty ? _selector(ty) : string.Empty;
                var r = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                return _dir == ListSortDirection.Ascending ? r : -r;
            }
        }

        private sealed class DateComparer : IComparer
        {
            private readonly ListSortDirection _dir;
            public DateComparer(ListSortDirection dir) { _dir = dir; }
            public int Compare(object? x, object? y)
            {
                var a = x is UiRow rx ? rx.CreatedAt : DateTime.MinValue;
                var b = y is UiRow ry ? ry.CreatedAt : DateTime.MinValue;
                var r = a.CompareTo(b);
                return _dir == ListSortDirection.Ascending ? r : -r;
            }
        }

        private sealed class CategoryComparer : IComparer
        {
            private readonly ListSortDirection _dir;
            private readonly RoutingService? _router;
            public CategoryComparer(ListSortDirection dir, RoutingService? router) { _dir = dir; _router = router; }
            public int Compare(object? x, object? y)
            {
                string cat(UiRow? r)
                {
                    if (r == null) return "";
                    var ext = "." + (r.Ext ?? "");
                    try { return _router?.MapExtensionToCategory(ext) ?? ext; }
                    catch { return ext; }
                }
                var cx = cat(x as UiRow);
                var cy = cat(y as UiRow);
                var rlt = string.Compare(cx, cy, StringComparison.OrdinalIgnoreCase);
                if (rlt == 0)
                {
                    var nx = (x as UiRow)?.FileName ?? "";
                    var ny = (y as UiRow)?.FileName ?? "";
                    rlt = string.Compare(nx, ny, StringComparison.OrdinalIgnoreCase);
                }
                return _dir == ListSortDirection.Ascending ? rlt : -rlt;
            }
        }

        private sealed class StatusComparer : IComparer
        {
            private readonly ListSortDirection _dir;
            public StatusComparer(ListSortDirection dir) => _dir = dir;
            private static int Weight(string? s)
            {
                var v = (s ?? "").ToLowerInvariant();
                return v switch
                {
                    "error" => 0,
                    "" or null => 1,
                    "intaked" => 1,
                    _ when v.StartsWith("stage") => 2,
                    "committed" => 3,
                    _ => 1
                };
            }
            public int Compare(object? x, object? y)
            {
                var a = Weight((x as UiRow)?.Status);
                var b = Weight((y as UiRow)?.Status);
                var r = a.CompareTo(b);
                return _dir == ListSortDirection.Ascending ? r : -r;
            }
        }
    }
}
