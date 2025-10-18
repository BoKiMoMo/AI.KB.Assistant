using System;
using System.IO;
using System.Windows;
using System.Windows.Forms; // 需參考 WindowsFormsIntegration
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly string _cfgPath;
        private readonly AppConfig _cfg;

        public SettingsWindow()
        {
            InitializeComponent();

            _cfgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            _cfg = ConfigService.TryLoad(_cfgPath);

            // 路徑
            TbDbPath.Text = _cfg.App.DbPath ?? "";
            TbRootDir.Text = _cfg.App.RootDir ?? "";
            TbHotFolder.Text = _cfg.Import.HotFolderPath ?? "";

            // Routing 深度
            CbYear.IsChecked = _cfg.Routing.EnableYear;
            CbMonth.IsChecked = _cfg.Routing.EnableMonth;
            CbProject.IsChecked = _cfg.Routing.EnableProject;
            CbCategory.IsChecked = _cfg.Routing.EnableCategory;
            CbType.IsChecked = _cfg.Routing.EnableType;

            // LLM
            CbEnableAI.IsChecked = _cfg.OpenAI.EnableWhenLowConfidence;
            TbApiKey.Text = _cfg.OpenAI.ApiKey ?? "";
            TbBaseUrl.Text = _cfg.OpenAI.BaseUrl ?? "";
            TbModel.Text = _cfg.OpenAI.Model ?? "gpt-4o-mini";

            // Theme
            var theme = _cfg.Views?.Theme ?? "淺色";
            CmbTheme.SelectedIndex = theme switch
            {
                "深色" => 1,
                "跟隨系統" => 2,
                _ => 0
            };
        }

        private void BrowseFolder(System.Windows.Controls.TextBox target)
        {
            using var dlg = new FolderBrowserDialog();
            dlg.ShowNewFolderButton = true;
            dlg.Description = "選擇資料夾";
            if (Directory.Exists(target.Text)) dlg.SelectedPath = target.Text;

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                target.Text = dlg.SelectedPath;
        }

        private void BrowseRoot_Click(object sender, RoutedEventArgs e) => BrowseFolder(TbRootDir);
        private void BrowseHotFolder_Click(object sender, RoutedEventArgs e) => BrowseFolder(TbHotFolder);

        private void BrowseDb_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new SaveFileDialog
            {
                Title = "選擇或建立 SQLite 資料庫",
                Filter = "SQLite DB (*.db)|*.db|所有檔案 (*.*)|*.*",
                FileName = string.IsNullOrWhiteSpace(TbDbPath.Text) ? "data.db" : Path.GetFileName(TbDbPath.Text),
                AddExtension = true
            };
            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TbDbPath.Text = ofd.FileName;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // 路徑
            _cfg.App.DbPath = TbDbPath.Text?.Trim();
            _cfg.App.RootDir = TbRootDir.Text?.Trim();
            _cfg.Import.HotFolderPath = TbHotFolder.Text?.Trim();

            // Routing 深度
            _cfg.Routing.EnableYear = CbYear.IsChecked == true;
            _cfg.Routing.EnableMonth = CbMonth.IsChecked == true;
            _cfg.Routing.EnableProject = CbProject.IsChecked == true;
            _cfg.Routing.EnableCategory = CbCategory.IsChecked == true;
            _cfg.Routing.EnableType = CbType.IsChecked == true;

            // LLM
            _cfg.OpenAI.EnableWhenLowConfidence = CbEnableAI.IsChecked == true;
            _cfg.OpenAI.ApiKey = TbApiKey.Text?.Trim();
            _cfg.OpenAI.BaseUrl = TbBaseUrl.Text?.Trim();
            _cfg.OpenAI.Model = TbModel.Text?.Trim();

            // Theme
            _cfg.Views ??= new AppConfig.ViewsSection();
            _cfg.Views.Theme = CmbTheme.SelectedIndex switch
            {
                1 => "深色",
                2 => "跟隨系統",
                _ => "淺色"
            };

            try
            {
                ConfigService.Save(_cfgPath, _cfg);
                System.Windows.MessageBox.Show("已儲存設定並寫入 config.json。", "設定", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"儲存失敗：{ex.Message}", "設定", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
