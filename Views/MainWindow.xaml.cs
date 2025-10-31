// Views/MainWindow.xaml.cs
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32; // OpenFileDialog
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<Item> FileList { get; } = new();

        // 方便取用在 App.xaml.cs 註冊的全域服務
        private T? Get<T>(string key) where T : class
            => Application.Current?.Resources[key] as T;

        private DbService? Db => Get<DbService>("Db");
        private IntakeService? Intake => Get<IntakeService>("Intake");
        private RoutingService? Router => Get<RoutingService>("Router");
        private LlmService? Llm => Get<LlmService>("Llm");

        public MainWindow()
        {
            InitializeComponent();

            // 若 XAML 的檔案清單 ListView 名稱為 FileList，先綁定 ItemsSource 避免 Null 綁定
            try
            {
                if (FindName("FileList") is ListView list)
                    list.ItemsSource = FileList;
            }
            catch { /* ignore */ }

            // 監聽設定變更（V7.1 新增）
            ConfigService.ConfigChanged += (_, cfg) =>
            {
                try
                {
                    Router?.ApplyConfig(cfg);
                    Llm?.UpdateConfig(cfg);
                    // TODO: 若 HotFolderService 之後上線，可在此重新綁監控
                }
                catch { /* ignore */ }
                _ = RefreshFromDbAsync(); // 更新清單（保守作法）
            };

            // 載入時把 DB 內容讀進清單
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshFromDbAsync();
        }

        // ========= 共用小工具 =========
        private static void OpenInExplorer(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("路徑為空。");
                return;
            }

            if (File.Exists(path))
            {
                TryStart("explorer.exe", $"/select,\"{path}\"");
            }
            else if (Directory.Exists(path))
            {
                TryStart("explorer.exe", $"\"{path}\"");
            }
            else
            {
                MessageBox.Show($"找不到路徑：{path}");
            }
        }

        private static void TryStart(string fileName, string? args = null)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args ?? string.Empty,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "啟動失敗");
            }
        }

        private async Task RefreshFromDbAsync()
        {
            try
            {
                if (Db == null) return;
                var all = await Db.QueryAllAsync();
                FileList.Clear();
                foreach (var it in all)
                    FileList.Add(it);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "重新整理失敗");
            }
        }

        private static Item[] GetSelectedItems(Window win)
        {
            if (win == null) return Array.Empty<Item>();
            if (win.FindName("FileList") is ListView list)
                return list.SelectedItems.Cast<Item>().ToArray();
            return Array.Empty<Item>();
        }

        // ========= 三顆「開啟」按鈕 =========
        private void BtnOpenHot_Click(object sender, RoutedEventArgs e)
            => OpenInExplorer(AppConfig.Current?.Import?.HotFolder);

        private void BtnOpenRoot_Click(object sender, RoutedEventArgs e)
            => OpenInExplorer(AppConfig.Current?.Routing?.RootDir ?? AppConfig.Current?.App?.RootDir);

        private void BtnOpenDb_Click(object sender, RoutedEventArgs e)
        {
            var p = AppConfig.Current?.Db?.DbPath;
            if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
                TryStart(p); // 直接開啟 DB 檔
            else
                OpenInExplorer(Path.GetDirectoryName(p ?? string.Empty)); // 找不到就開資料夾
        }

        // ========= 以下是 XAML 綁定到的事件（保留名稱；只補安全最小實作） =========
        private void TvFolders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) { }
        private void CmStageToInbox_Click(object sender, RoutedEventArgs e) { }
        private void CmClassify_Click(object sender, RoutedEventArgs e) { }
        private void CmCommit_Click(object sender, RoutedEventArgs e) { }

        private void List_DoubleClick(object sender, MouseButtonEventArgs e) { }
        private void ListHeader_Click(object sender, RoutedEventArgs e) { }

        // === 新增檔案（多選）→ Intake → 預覽目的路徑 ===
        private async void BtnAddFiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Intake == null || Router == null)
                {
                    MessageBox.Show("服務尚未初始化（Intake / Router）。");
                    return;
                }

                var dlg = new OpenFileDialog
                {
                    Title = "選擇要加入的檔案",
                    Multiselect = true,
                    CheckFileExists = true
                };
                if (dlg.ShowDialog(this) != true) return;

                var added = await Intake.IntakeFilesAsync(dlg.FileNames);
                // 預覽目的路徑
                foreach (var it in added.Where(a => a != null))
                {
                    it.ProposedPath = Router.PreviewDestPath(it.Path);
                    FileList.Insert(0, it);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "加入檔案失敗");
            }
        }

        private void BtnStartClassify_Click(object sender, RoutedEventArgs e) { }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshFromDbAsync();
        }

        private void BtnOpenInbox_Click(object sender, RoutedEventArgs e)
            => OpenInExplorer(AppConfig.Current?.Import?.HotFolder);

        // === 新增：開啟設定視窗（使用者儲存後會自動廣播；這裡不用手動刷新）
        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new SettingsWindow();
                win.Owner = this;
                win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                win.ShowDialog();
                // 儲存時已廣播；若只是關閉不儲存，就不會影響目前狀態
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "開啟設定失敗");
            }
        }

        // === 新增：重新載入設定（從磁碟 Load，並觸發廣播）
        private void BtnReloadSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ConfigService.Load(); // 內部會觸發 ConfigChanged
                MessageBox.Show("設定已重新載入。", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "重新載入設定失敗");
            }
        }

        private void BtnEdgeLeft_Click(object sender, RoutedEventArgs e) { }
        private void BtnEdgeRight_Click(object sender, RoutedEventArgs e) { }

        private void BtnGenTags_Click(object sender, RoutedEventArgs e) { }
        private void BtnSummarize_Click(object sender, RoutedEventArgs e) { }
        private void BtnAnalyzeConfidence_Click(object sender, RoutedEventArgs e) { }
        private void BtnSearchProject_Click(object sender, RoutedEventArgs e) { }
        private void BtnLockProject_Click(object sender, RoutedEventArgs e) { }

        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
        private void TvFilterBox_TextChanged(object sender, TextChangedEventArgs e) { }

        private void Tree_OpenInExplorer_Click(object sender, RoutedEventArgs e) { }
        private void Tree_MoveFolderToInbox_Click(object sender, RoutedEventArgs e) { }
        private void Tree_LockProject_Click(object sender, RoutedEventArgs e) { }
        private void Tree_UnlockProject_Click(object sender, RoutedEventArgs e) { }
        private void Tree_Rename_Click(object sender, RoutedEventArgs e) { }

        private void BtnApplyAiTuning_Click(object sender, RoutedEventArgs e) { }
        private void CmOpenFile_Click(object sender, RoutedEventArgs e) { }
        private void CmRevealInExplorer_Click(object sender, RoutedEventArgs e) { }
        private void CmCopySourcePath_Click(object sender, RoutedEventArgs e) { }
        private void CmCopyDestPath_Click(object sender, RoutedEventArgs e) { }
        private void CmDeleteRecord_Click(object sender, RoutedEventArgs e) { }

        // === 實際搬檔（提交） ===
        private async void BtnCommit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Router == null || Db == null)
                {
                    MessageBox.Show("服務尚未初始化（Router / Db）。");
                    return;
                }

                var selected = GetSelectedItems(this);
                if (selected.Length == 0)
                {
                    MessageBox.Show("請先在清單中選取要提交的項目。");
                    return;
                }

                int ok = 0;
                foreach (var it in selected)
                {
                    // 沒有預覽就先算
                    if (string.IsNullOrWhiteSpace(it.ProposedPath))
                        it.ProposedPath = Router.PreviewDestPath(it.Path);

                    var final = Router.Commit(it);
                    if (!string.IsNullOrWhiteSpace(final))
                    {
                        it.Status = "committed";
                        it.ProposedPath = final;
                        ok++;
                    }
                }

                if (ok > 0)
                    await Db.UpdateItemsAsync(selected);

                MessageBox.Show($"完成提交：{ok} / {selected.Length}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "提交失敗");
            }
        }
    }
}
