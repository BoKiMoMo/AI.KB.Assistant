// Views/MainWindow.xaml.cs
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Views
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<Item> FileList { get; } = new();

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

        // ========= 三顆「開啟」按鈕 =========
        private void BtnOpenHot_Click(object sender, RoutedEventArgs e)
            => OpenInExplorer(AppConfig.Current?.Import?.HotFolder);

        private void BtnOpenRoot_Click(object sender, RoutedEventArgs e)
            => OpenInExplorer(AppConfig.Current?.Routing?.RootDir);

        private void BtnOpenDb_Click(object sender, RoutedEventArgs e)
        {
            var p = AppConfig.Current?.Db?.DbPath;
            if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
                TryStart(p); // 直接開啟 DB 檔
            else
                OpenInExplorer(Path.GetDirectoryName(p ?? string.Empty)); // 找不到就開資料夾
        }

        // ========= 以下是 XAML 綁定到的事件（每個只宣告一次） =========
        private void TvFolders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) { }
        private void CmStageToInbox_Click(object sender, RoutedEventArgs e) { }
        private void CmClassify_Click(object sender, RoutedEventArgs e) { }
        private void CmCommit_Click(object sender, RoutedEventArgs e) { }

        private void List_DoubleClick(object sender, MouseButtonEventArgs e) { }
        private void ListHeader_Click(object sender, RoutedEventArgs e) { }

        private void BtnAddFiles_Click(object sender, RoutedEventArgs e) { }
        private void BtnStartClassify_Click(object sender, RoutedEventArgs e) { }
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
        private void BtnCommit_Click(object sender, RoutedEventArgs e) { }
    }
}
