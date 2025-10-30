using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.IO;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views
{
    public partial class SettingsWindow : Window
    {
        private AppConfig _cfg;
        private string _cfgPath = "";

        public SettingsWindow()
        {
            InitializeComponent();

            _cfg = ConfigService.TryLoad(out _cfgPath) ?? AppConfig.Default();
            LoadUiFromConfig();
        }

        // ======== UI <-> Config 映射 ========

        private void LoadUiFromConfig()
        {
            // Routing
            SetText("TbRootDir", _cfg.Routing.RootDir);
            SetText("TbDbPath", _cfg.Db?.Path);
            SetChecked("CbUseYear", _cfg.Routing.UseYear);
            SetChecked("CbUseMonth", _cfg.Routing.UseMonth);
            SetChecked("CbUseProject", _cfg.Routing.UseProject);
            SetChecked("CbUseType", _cfg.Routing.UseType);

            // Import
            SetChecked("CbIncludeSubdir", _cfg.Import.IncludeSubdir);
            SetChecked("CbEnableHotFolder", _cfg.Import.EnableHotFolder);
            SetText("TbHotFolder", _cfg.App?.HotFolder);
            SetText("TbBlacklistExts", string.Join(",", _cfg.Import.BlacklistExts));
            SetText("TbBlacklistFolders", string.Join(",", _cfg.Import.BlacklistFolderNames));

            // AI
            SetDouble("SlThreshold", _cfg.Routing.Threshold);
            SetText("TbAutoFolder", _cfg.Routing.AutoFolderName);
            SetText("TbLowConfidence", _cfg.Routing.LowConfidenceFolderName);
            SetText("TbApiKey", _cfg.OpenAI.ApiKey);
            SetText("TbModel", _cfg.OpenAI.Model);
        }

        private void SaveUiToConfig()
        {
            // Routing
            _cfg.Routing.RootDir = GetText("TbRootDir");
            _cfg.Db.Path = GetText("TbDbPath");
            _cfg.Routing.UseYear = IsChecked("CbUseYear", true);
            _cfg.Routing.UseMonth = IsChecked("CbUseMonth", true);
            _cfg.Routing.UseProject = IsChecked("CbUseProject", true);
            _cfg.Routing.UseType = IsChecked("CbUseType", false);

            // Import
            _cfg.Import.IncludeSubdir = IsChecked("CbIncludeSubdir", true);
            _cfg.Import.EnableHotFolder = IsChecked("CbEnableHotFolder", true);
            _cfg.Import.HotFolderPath = GetText("TbHotFolder");
            _cfg.Import.BlacklistExts = SplitCsv(GetText("TbBlacklistExts"));
            _cfg.Import.BlacklistFolderNames = SplitCsv(GetText("TbBlacklistFolders"));

            // AI
            _cfg.Routing.Threshold = GetDouble("SlThreshold") ?? 0.75;
            _cfg.Routing.AutoFolderName = GetText("TbAutoFolder");
            _cfg.Routing.LowConfidenceFolderName = GetText("TbLowConfidence");
            _cfg.OpenAI.ApiKey = GetText("TbApiKey");
            _cfg.OpenAI.Model = GetText("TbModel");
        }

        // ======== 按鈕事件 ========

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveUiToConfig();
                ConfigService.Save(_cfg, _cfgPath);
                MessageBox.Show("設定已儲存。", "AI.KB Assistant", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"儲存失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            _cfg = ConfigService.TryLoad(out _cfgPath);
            LoadUiFromConfig();
            MessageBox.Show("已重新載入設定。", "AI.KB Assistant", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnBrowseRoot_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                SetText("TbRootDir", dlg.SelectedPath);
        }

        private void BtnBrowseHot_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                SetText("TbHotFolder", dlg.SelectedPath);
        }

        private void BtnBrowseDb_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "SQLite (*.db)|*.db|All Files|*.*" };
            if (dlg.ShowDialog() == true)
                SetText("TbDbPath", dlg.FileName);
        }

        // ======== 工具區 ========

        private static string GetText(string name)
        {
            if (Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault() is not SettingsWindow w)
                return "";
            return (w.FindName(name) as TextBox)?.Text ?? "";
        }

        private static void SetText(string name, string value)
        {
            if (Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault() is not SettingsWindow w)
                return;
            if (w.FindName(name) is TextBox tb)
                tb.Text = value ?? "";
        }

        private static void SetChecked(string name, bool value)
        {
            if (Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault() is not SettingsWindow w)
                return;
            if (w.FindName(name) is CheckBox cb)
                cb.IsChecked = value;
        }

        private static bool IsChecked(string name, bool fallback)
        {
            if (Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault() is not SettingsWindow w)
                return fallback;
            if (w.FindName(name) is CheckBox cb)
                return cb.IsChecked == true;
            return fallback;
        }

        private static void SetDouble(string name, double value)
        {
            if (Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault() is not SettingsWindow w)
                return;
            if (w.FindName(name) is Slider s)
                s.Value = value;
        }

        private static double? GetDouble(string name)
        {
            if (Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault() is not SettingsWindow w)
                return null;
            if (w.FindName(name) is Slider s)
                return s.Value;
            return null;
        }

        private static string[] SplitCsv(string? txt)
        {
            if (string.IsNullOrWhiteSpace(txt)) return Array.Empty<string>();
            return txt.Split(new[] { ',', '，', ';', '；', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToArray();
        }
    }
}
