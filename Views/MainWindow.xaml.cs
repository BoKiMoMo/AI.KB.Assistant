using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views
{
    public partial class MainWindow : Window
    {
        // 供 XAML 綁定用的主清單（錯誤清單大量提到 FileList）
        public ObservableCollection<Item> FileList { get; } = new();

        public MainWindow()
        {
            InitializeComponent();

            // 讓 ListView 的 ItemsSource 有東西可綁
            try
            {
                // 如果 XAML 叫 FileList 元件名，這裡也同步一下
                var list = this.FindName("FileList") as ListView;
                if (list != null) list.ItemsSource = FileList;
            }
            catch { /* 容錯 */ }
        }

        // === 目前錯誤列表提到的三個事件（先提供最小功能：打開資料夾 / 檔案路徑） ===

        private void BtnOpenHot_Click(object sender, RoutedEventArgs e)
        {
            var p = ConfigService.Cfg?.Import?.HotFolderPath;
            OpenInExplorer(p);
        }

        private void BtnOpenRoot_Click(object sender, RoutedEventArgs e)
        {
            var p = ConfigService.Cfg?.Routing?.RootDir;
            OpenInExplorer(p);
        }

        private void BtnOpenDb_Click(object sender, RoutedEventArgs e)
        {
            var p = ConfigService.DbPath;
            if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
            {
                // 直接叫系統既定程式打開 DB 檔
                TryStart(p);
            }
            else
            {
                // 若檔案不存在則打開其所在資料夾
                OpenInExplorer(Path.GetDirectoryName(p));
            }
        }

        // ====== 常見 UI 事件存根（避免之後又因名稱存在但尚未實作而卡編譯） ======

        private void BtnAddFiles_Click(object sender, RoutedEventArgs e) { }
        private void BtnStartClassify_Click(object sender, RoutedEventArgs e) { }
        private void BtnCommit_Click(object sender, RoutedEventArgs e) { }
        private void BtnRefresh_Click(object sender, RoutedEventArgs e) { }
        private void BtnOpenInbox_Click(object sender, RoutedEventArgs e) { }
        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e) { }
        private void BtnEdgeLeft_Click(object sender, RoutedEventArgs e) { }
        private void BtnEdgeRight_Click(object sender, RoutedEventArgs e) { }
        private void BtnGenTags_Click(object sender, RoutedEventArgs e) { }
        private void BtnSummarize_Click(object sender, RoutedEventArgs e) { }
        private void BtnAnalyzeConfidence_Click(object sender, RoutedEventArgs e) { }
        private void BtnSearchProject_Click(object sender, RoutedEventArgs e) { }
        private void BtnLockProject_Click(object sender, RoutedEventArgs e) { }
        private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
        private void List_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { }
        private void ListHeader_Click(object sender, RoutedEventArgs e) { }
        private void TvFilterBox_TextChanged(object sender, TextChangedEventArgs e) { }
        private void Tree_OpenInExplorer_Click(object sender, RoutedEventArgs e) { }
        private void Tree_MoveFolderToInbox_Click(object sender, RoutedEventArgs e) { }
        private void Tree_LockProject_Click(object sender, RoutedEventArgs e) { }
        private void Tree_UnlockProject_Click(object sender, RoutedEventArgs e) { }
        private void Tree_Rename_Click(object sender, RoutedEventArgs e) { }
        private void CmOpenFile_Click(object sender, RoutedEventArgs e) { }
        private void CmRevealInExplorer_Click(object sender, RoutedEventArgs e) { }
        private void CmCopySourcePath_Click(object sender, RoutedEventArgs e) { }
        private void CmCopyDestPath_Click(object sender, RoutedEventArgs e) { }
        private void CmDeleteRecord_Click(object sender, RoutedEventArgs e) { }
        private void BtnApplyAiTuning_Click(object sender, RoutedEventArgs e) { }
        private void BtnReloadSettings_Click(object sender, RoutedEventArgs e) { }

        // ====== 小工具 ======

        private static void OpenInExplorer(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("路徑為空。");
                return;
            }

            if (File.Exists(path))
            {
                // 直接選取檔案
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

        private static void TryStart(string fileName, string args = null)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args ?? "",
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "啟動失敗");
            }
        }

        /// <summary>相對 / 展開等處理</summary>
        private static string GetFullPath(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "";
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(p));
        }
    }
}
