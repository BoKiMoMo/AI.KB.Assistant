using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views
{
    public partial class MainWindow : Window
    {
        // ---- Services & Config ----
        private readonly AppConfig _cfg;
        private readonly DbService _db;
        private readonly RoutingService _routing;
        private readonly LlmService _llm;

        // ---- UI / State ----
        private bool _isLeftCollapsed = false;
        private bool _isRightCollapsed = false;
        private string? _sortKey = null;
        private bool _sortAsc = true;
        private string? _lockedProject = null;   // 專案鎖定（避免直接綁在 _cfg 以免結構不符）

        public MainWindow()
        {
            InitializeComponent();

            // 初始化 Config / Services（依你現有 Service 簽章）
            _cfg = AppConfig.Load();                         // 你的 AppConfig 型別已存在
            _db = new DbService(_cfg.Db?.Path);              // DbService(string? dbPath)  :contentReference[oaicite:3]{index=3}
            _routing = new RoutingService(_cfg);             // RoutingService(AppConfig cfg)  :contentReference[oaicite:4]{index=4}
            _llm = new LlmService(_cfg);                     // LlmService(AppConfig cfg)  :contentReference[oaicite:5]{index=5}

            BuildTopPaths();
            LoadFolderTree();
            LoadInboxList();

            Log("應用程式已啟動。");
        }

        // ==========================
        // Top 路徑籤 / 樹 / 清單
        // ==========================

        private void BuildTopPaths()
        {
            TopPaths.ItemsSource = new[]
            {
                new { Label = "Root",     Path = _cfg.App?.RootDir ?? "" },
                new { Label = "HotFolder",Path = _cfg.Import?.HotFolder ?? "" },
                new { Label = "Desktop",  Path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) },
                new { Label = "Database", Path = _cfg.Db?.Path ?? "" },
            };
        }

        private void LoadFolderTree()
        {
            var root = _cfg.App?.RootDir;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                Log("[警告] Root 目錄未設定或不存在。");
                TvFolders.ItemsSource = null;
                return;
            }

            var rootNode = BuildNode(new DirectoryInfo(root));
            TvFolders.ItemsSource = new List<FolderNode> { rootNode };
        }

        private FolderNode BuildNode(DirectoryInfo dir)
        {
            var n = new FolderNode { Name = dir.Name, FullPath = dir.FullName };
            try
            {
                foreach (var sub in dir.EnumerateDirectories())
                {
                    if (sub.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                    n.Children.Add(BuildNode(sub));
                }
            }
            catch { /* 忽略存取例外 */ }
            return n;
        }

        private void LoadInboxList()
        {
            var items = _db.QueryByStatus("inbox").ToList();
            FileList.ItemsSource = items;
            TxtCounter.Text = $"收件匣：{items.Count} 項";
        }

        // ==========================
        // 工具列（左/右收合、刷新、加入檔案、預分類、確認搬檔）
        // ==========================

        private void BtnEdgeLeft_Click(object sender, RoutedEventArgs e)
        {
            _isLeftCollapsed = !_isLeftCollapsed;
            LeftPaneColumn.Width = _isLeftCollapsed ? new GridLength(0) : new GridLength(280);
        }

        private void BtnEdgeRight_Click(object sender, RoutedEventArgs e)
        {
            _isRightCollapsed = !_isRightCollapsed;
            RightPaneColumn.Width = _isRightCollapsed ? new GridLength(0) : new GridLength(320);
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadFolderTree();
            ReloadCurrentTab();
            Log("已重新整理。");
        }

        private async void BtnAddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "All Files|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;

            var list = new List<Item>();
            foreach (var f in dlg.FileNames)
            {
                var it = new Item(f)
                {
                    Status = "inbox",
                    Project = _lockedProject ?? GuessProjectFromPath(f),
                    Tags = ""
                };
                list.Add(it);
            }

            _db.UpsertRange(list);
            await Task.Yield();
            ReloadCurrentTab();
            ShowBanner($"已加入 {list.Count} 檔案至收件匣。");
        }

        private void BtnStartClassify_Click(object sender, RoutedEventArgs e)
        {
            // 使用 PreviewDestPath 產生 ProposedPath
            var query = FileList.ItemsSource as IEnumerable<Item>;
            if (query == null) return;

            foreach (var it in query)
            {
                it.ProposedPath = _routing.PreviewDestPath(it.SourcePath, _lockedProject);
            }
            FileList.Items.Refresh();
            ShowBanner("預分類完成（僅計算路徑，不搬檔）。");
        }

        private async void BtnCommit_Click(object sender, RoutedEventArgs e)
        {
            // 根據 ProposedPath 進行實際搬檔
            var query = FileList.ItemsSource as IEnumerable<Item>;
            if (query == null) return;

            int ok = 0, fail = 0;
            foreach (var it in query)
            {
                try
                {
                    var target = it.ProposedPath;
                    if (string.IsNullOrWhiteSpace(target))
                    {
                        // 沒有預覽路徑就現算
                        target = _routing.PreviewDestPath(it.SourcePath, _lockedProject);
                        it.ProposedPath = target;
                    }

                    var dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    // 若來源不存在就略過
                    if (!File.Exists(it.SourcePath)) { fail++; continue; }

                    // 目標若已存在，簡單改名避免覆蓋
                    var final = EnsureNoOverwrite(target);
                    File.Move(it.SourcePath, final);
                    it.Status = "done";
                    it.Path = final;
                    ok++;
                    _db.Upsert(it);
                }
                catch (Exception ex)
                {
                    fail++;
                    Log($"[搬檔失敗] {it.SourcePath} => {it.ProposedPath}：{ex.Message}");
                }
            }
            await Task.Yield();
            ReloadCurrentTab();
            ShowBanner($"搬檔完成：成功 {ok}，失敗 {fail}。");
        }

        private string EnsureNoOverwrite(string destPath)
        {
            if (!File.Exists(destPath)) return destPath;
            var dir = Path.GetDirectoryName(destPath) ?? "";
            var name = Path.GetFileNameWithoutExtension(destPath);
            var ext = Path.GetExtension(destPath);
            int i = 1;
            string alt;
            do
            {
                alt = Path.Combine(dir, $"{name} ({i++}){ext}");
            } while (File.Exists(alt));
            return alt;
        }

        // ==========================
        // 專案鎖定 / 搜尋
        // ==========================

        private void BtnSearchProject_Click(object sender, RoutedEventArgs e)
        {
            var kw = (CbLockProject.Text ?? "").Trim();
            var all = _db.QueryAll();
            var filtered = string.IsNullOrEmpty(kw) ? all : all.Where(x => string.Equals(x.Project ?? "", kw, StringComparison.OrdinalIgnoreCase));
            FileList.ItemsSource = filtered.ToList();
            TxtCounter.Text = $"搜尋結果：{(FileList.ItemsSource as IList<Item>)?.Count ?? 0} 項";
        }

        private void BtnLockProject_Click(object sender, RoutedEventArgs e)
        {
            _lockedProject = string.IsNullOrWhiteSpace(CbLockProject.Text) ? null : CbLockProject.Text.Trim();
            ShowBanner(_lockedProject == null ? "已解除專案鎖定" : $"已鎖定專案：{_lockedProject}");
        }

        private void TvFilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 簡單做中清單 Filter（包含名稱/路徑/標籤）
            var kw = (TvFilterBox.Text ?? "").Trim();
            var data = _db.QueryAll();
            if (!string.IsNullOrEmpty(kw))
            {
                data = data.Where(x =>
                    (x.Filename ?? "").Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                    (x.Path ?? "").Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                    (x.Tags ?? "").Contains(kw, StringComparison.OrdinalIgnoreCase));
            }
            FileList.ItemsSource = data.ToList();
            TxtCounter.Text = $"篩選：{(FileList.ItemsSource as IList<Item>)?.Count ?? 0} 項";
        }

        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MainTabs.SelectedItem is not TabItem ti) return;
            var tag = (ti.Tag as string) ?? "home";

            IEnumerable<Item> src = _db.QueryAll();
            src = tag switch
            {
                "home" => _db.QueryByStatus("inbox"),
                "fav" => src.Where(x => (x.Tags ?? "").Split(',').Any(t => t.Trim().Equals("favorite", StringComparison.OrdinalIgnoreCase))),
                "processing" => _db.QueryByStatus("processing"),
                "backlog" => _db.QueryByStatus("backlog"),
                "blacklist" => _db.QueryByStatus("blacklist"),
                "autosort-staging" => _db.QueryByStatus("autosort"),
                _ => src
            };

            FileList.ItemsSource = src.ToList();
            TxtCounter.Text = $"{ti.Header}：{(FileList.ItemsSource as IList<Item>)?.Count ?? 0} 項";
        }

        private void ReloadCurrentTab()
        {
            // 觸發一次 SelectionChanged 邏輯
            var keep = MainTabs.SelectedIndex;
            MainTabs.SelectedIndex = 0;
            MainTabs.SelectedIndex = keep < 0 ? 0 : keep;
        }

        // ==========================
        // 左邊樹：選取 / 加入收件匣 / 其它
        // ==========================

        private void TvFolders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is not FolderNode n) return;
            try
            {
                var files = Directory.EnumerateFiles(n.FullPath, "*.*", SearchOption.TopDirectoryOnly)
                                     .Select(f => new Item(f) { Status = "inbox", Project = GuessProjectFromPath(f) })
                                     .ToList();
                FileList.ItemsSource = files;
                TxtCounter.Text = $"{n.Name}：{files.Count} 項（預覽）";
            }
            catch (Exception ex)
            {
                Log($"[錯誤] 無法讀取 {n.FullPath}：{ex.Message}");
            }
        }

        private void Tree_MoveFolderToInbox_Click(object sender, RoutedEventArgs e)
        {
            if (TvFolders.SelectedItem is not FolderNode n) return;
            var files = Directory.EnumerateFiles(n.FullPath, "*.*", SearchOption.AllDirectories).ToList();
            var items = files.Select(f => new Item(f) { Status = "inbox", Project = GuessProjectFromPath(f) });
            _db.UpsertRange(items);
            ReloadCurrentTab();
            ShowBanner($"已加入 {files.Count} 檔案至收件匣。");
        }

        private void Tree_OpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (TvFolders.SelectedItem is not FolderNode n) return;
            TryOpenFolder(n.FullPath);
        }
        private void Tree_LockProject_Click(object sender, RoutedEventArgs e)
        {
            if (TvFolders.SelectedItem is not FolderNode n) return;
            _lockedProject = new DirectoryInfo(n.FullPath).Name;
            CbLockProject.Text = _lockedProject;
            ShowBanner($"已鎖定專案：{_lockedProject}");
        }
        private void Tree_UnlockProject_Click(object sender, RoutedEventArgs e)
        {
            _lockedProject = null;
            ShowBanner("已解除專案鎖定。");
        }
        private void Tree_Rename_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("重新命名資料夾：之後補完整 UI。", "TODO");
        }

        // ==========================
        // 中清單：雙擊 / 排序
        // ==========================

        private void List_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FileList.SelectedItem is not Item it) return;
            CmOpenFile_Click(sender, e);
        }

        private void ListHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not GridViewColumnHeader h) return;
            var key = (h.Content as string) ?? h.Tag as string ?? "";
            if (string.IsNullOrEmpty(key)) return;

            var data = FileList.ItemsSource as IEnumerable<Item>;
            if (data == null) return;

            if (_sortKey == key) _sortAsc = !_sortAsc; else { _sortKey = key; _sortAsc = true; }

            Func<Item, object?> selector = key switch
            {
                "檔名" or "Filename" => i => i.Filename,
                "副檔名" or "Ext" => i => i.Ext,
                "專案" or "Project" => i => i.Project,
                "標籤" or "Tags" => i => i.Tags,
                "路徑" or "Path" => i => i.Path ?? i.SourcePath,
                "預計路徑" or "ProposedPath" => i => i.ProposedPath,
                "CreatedTs" => i => i.CreatedTs ?? i.CreatedAt, // 兼容不同屬性名
                _ => i => i.Filename
            };

            data = _sortAsc ? data.OrderBy(selector) : data.OrderByDescending(selector);
            FileList.ItemsSource = data.ToList();
        }

        // ==========================
        // 右側：AI 三鍵 / 調校與重載
        // ==========================

        private async void BtnGenTags_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.SelectedItem is not Item it) return;
            ShowBanner("AI 標籤產生中…");
            var text = it.Filename ?? Path.GetFileName(it.SourcePath);
            var tags = await _llm.SuggestTagsAsync(text);  // LlmService.SuggestTagsAsync(string)  :contentReference[oaicite:6]{index=6}
            it.Tags = string.Join(',', tags);
            _db.Upsert(it);
            FileList.Items.Refresh();
            HideBanner();
            Log($"已產生標籤：{it.Filename} => {it.Tags}");
        }

        private async void BtnSummarize_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.SelectedItem is not Item it) return;
            ShowBanner("AI 摘要中…");
            var text = it.Filename ?? Path.GetFileName(it.SourcePath);
            var summary = await _llm.SummarizeAsync(text); // LlmService.SummarizeAsync(string)  :contentReference[oaicite:7]{index=7}
            RtMeta.Text = summary;
            HideBanner();
        }

        private async void BtnAnalyzeConfidence_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.SelectedItem is not Item it) return;
            ShowBanner("AI 信心分析中…");
            var text = it.Filename ?? Path.GetFileName(it.SourcePath);
            var conf = await _llm.AnalyzeConfidenceAsync(text); // LlmService.AnalyzeConfidenceAsync(string)  :contentReference[oaicite:8]{index=8}
            RtMeta.Text = $"信心分數：{conf:P}";
            HideBanner();
        }

        private void BtnApplyAiTuning_Click(object sender, RoutedEventArgs e)
        {
            // 目前右欄只有 Slider/黑名單文字框，直接覆寫 _cfg 之後更新 Service
            double th = RtThreshold.Value;
            _cfg.Routing ??= new();
            _cfg.Routing.Threshold = th;
            _cfg.Save();
            _routing.ApplyConfig(_cfg);
            _llm.UpdateConfig(_cfg);

            ShowBanner("已套用 AI 參數。");
        }

        private void BtnReloadSettings_Click(object sender, RoutedEventArgs e)
        {
            var latest = AppConfig.Load();
            // 更新現有服務的設定
            _routing.ApplyConfig(latest);
            _llm.UpdateConfig(latest);

            // UI 同步
            RtThreshold.Value = latest.Routing?.Threshold ?? RtThreshold.Value;
            RtBlacklist.Text = string.Join(", ", latest.Routing?.BlackFolders ?? Array.Empty<string>());

            ShowBanner("已重新載入設定。");
        }

        // ==========================
        // 中清單 ContextMenu（先做穩定版）
        // ==========================

        private void CmOpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.SelectedItem is not Item it) return;
            if (File.Exists(it.SourcePath))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(it.SourcePath) { UseShellExecute = true });
        }

        private void CmRevealInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.SelectedItem is not Item it) return;
            if (File.Exists(it.SourcePath))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{it.SourcePath}\"");
            else if (!string.IsNullOrEmpty(it.Path) && File.Exists(it.Path))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{it.Path}\"");
        }

        private void CmCopySourcePath_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.SelectedItem is not Item it) return;
            Clipboard.SetText(it.SourcePath ?? "");
            ShowBanner("已複製原始路徑。");
        }

        private void CmCopyDestPath_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.SelectedItem is not Item it) return;
            var p = string.IsNullOrWhiteSpace(it.ProposedPath)
                ? _routing.PreviewDestPath(it.SourcePath, _lockedProject)
                : it.ProposedPath;
            Clipboard.SetText(p ?? "");
            ShowBanner("已複製預計路徑。");
        }

        private void CmStageToInbox_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.SelectedItem is not Item it) return;
            it.Status = "inbox";
            _db.Upsert(it);
            ReloadCurrentTab();
        }

        private void CmClassify_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.SelectedItem is not Item it) return;
            it.ProposedPath = _routing.PreviewDestPath(it.SourcePath, _lockedProject);
            FileList.Items.Refresh();
        }

        private void CmCommit_Click(object sender, RoutedEventArgs e)
        {
            // 單筆搬檔
            if (FileList.SelectedItem is not Item it) return;
            it.ProposedPath ??= _routing.PreviewDestPath(it.SourcePath, _lockedProject);
            try
            {
                var dir = Path.GetDirectoryName(it.ProposedPath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var final = EnsureNoOverwrite(it.ProposedPath);
                if (File.Exists(it.SourcePath))
                    File.Move(it.SourcePath, final);
                it.Path = final;
                it.Status = "done";
                _db.Upsert(it);
                ReloadCurrentTab();
            }
            catch (Exception ex)
            {
                Log($"[搬檔失敗] {it.SourcePath} => {it.ProposedPath}：{ex.Message}");
            }
        }

        private void CmDeleteRecord_Click(object sender, RoutedEventArgs e)
        {
            if (FileList.SelectedItem is not Item it) return;
            _db.RemoveById(it.Id);
            ReloadCurrentTab();
        }

        // ==========================
        // 小工具 / 共用
        // ==========================

        private void ShowBanner(string msg)
        {
            BannerText.Text = msg;
            Banner.Visibility = Visibility.Visible;
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(2000);
                Banner.Visibility = Visibility.Collapsed;
            }, DispatcherPriority.Background);
        }

        private void HideBanner() => Banner.Visibility = Visibility.Collapsed;

        private void Log(string message)
        {
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            LogBox.ScrollToEnd();
        }

        private void TryOpenFolder(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                MessageBox.Show($"找不到路徑：{path}", "提醒", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            System.Diagnostics.Process.Start("explorer.exe", path);
        }

        private static string GuessProjectFromPath(string fullPath)
        {
            try
            {
                var dir = Path.GetDirectoryName(fullPath);
                return string.IsNullOrWhiteSpace(dir) ? "未分類" : new DirectoryInfo(dir).Name;
            }
            catch { return "未分類"; }
        }
    }

    // 樹用節點
    public class FolderNode
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public List<FolderNode> Children { get; set; } = new();
    }
}
