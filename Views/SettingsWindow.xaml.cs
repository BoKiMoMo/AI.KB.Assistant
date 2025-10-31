using System;
using System.Windows;
using Microsoft.Win32;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views   // ← 這裡很重要：要和 XAML 的 x:Class 完全一致
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();

            // 初始渲染目前設定（若沒有對應名稱的 TextBox，不會拋錯）
            SetText("RootDir", ConfigService.Cfg.App.RootDir ?? "");
            SetText("HotFolder", ConfigService.Cfg.Import.HotFolderPath ?? "");
            SetText("DbPath", ConfigService.Cfg.Db.Path ?? "");
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            ConfigService.Load();
            SetText("RootDir", ConfigService.Cfg.App.RootDir ?? "");
            SetText("HotFolder", ConfigService.Cfg.Import.HotFolderPath ?? "");
            SetText("DbPath", ConfigService.Cfg.Db.Path ?? "");
            MessageBox.Show("設定已重新載入。", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // 若你有把 TextBox 值回寫到 Cfg，請在此處理（範例註解）：
            // ConfigService.Cfg.App.RootDir           = GetText("RootDir");
            // ConfigService.Cfg.Import.HotFolderPath  = GetText("HotFolder");
            // ConfigService.Cfg.Db.Path               = GetText("DbPath");

            ConfigService.Save();
            MessageBox.Show("設定已儲存。", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnBrowseRoot_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            var r = dlg.ShowDialog();
            if (r == System.Windows.Forms.DialogResult.OK)
            {
                ConfigService.Cfg.App.RootDir = dlg.SelectedPath;
                SetText("RootDir", dlg.SelectedPath);
            }
        }

        private void BtnBrowseHot_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            var r = dlg.ShowDialog();
            if (r == System.Windows.Forms.DialogResult.OK)
            {
                ConfigService.Cfg.Import.HotFolderPath = dlg.SelectedPath;
                SetText("HotFolder", dlg.SelectedPath);
            }
        }

        private void BtnBrowseDb_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "選擇資料庫檔案",
                Filter = "SQLite Database (*.db;*.sqlite)|*.db;*.sqlite|All files (*.*)|*.*",
                CheckFileExists = false
            };
            if (dlg.ShowDialog() == true)
            {
                ConfigService.Cfg.Db.Path = dlg.FileName;
                SetText("DbPath", dlg.FileName);
            }
        }

        /// <summary>
        /// 安全地把文字寫到具名 TextBox（找不到同名元素時不會拋例外）。
        /// </summary>
        private void SetText(string name, string value)
        {
            var tb = FindName(name) as System.Windows.Controls.TextBox;
            if (tb != null) tb.Text = value ?? string.Empty;
        }

        private string GetText(string name)
        {
            var tb = FindName(name) as System.Windows.Controls.TextBox;
            return tb?.Text ?? string.Empty;
        }
    }
}
