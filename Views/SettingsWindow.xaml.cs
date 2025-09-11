using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly string _configPath;
        private AppConfig _cfg;

        public SettingsWindow(string configPath, AppConfig? cfg = null)
        {
            InitializeComponent();
            _configPath = configPath;
            _cfg = cfg ?? ConfigService.Load(_configPath);

            // App
            TxtRootDir.Text = _cfg.App?.RootDir ?? "";
            TxtInboxDir.Text = _cfg.App?.InboxDir ?? "";
            TxtDbPath.Text = _cfg.App?.DbPath ?? "";
            ChkDryRun.IsChecked = _cfg.App?.DryRun ?? false;
            ChkOverwrite.IsChecked = _cfg.App?.Overwrite ?? false;
            SelectComboByValue(CmbMoveMode, _cfg.App?.MoveMode ?? "copy");

            // OpenAI
            TxtApiKey.Password = _cfg.OpenAI?.ApiKey ?? "";

            // Classification
            SelectComboByValue(CmbClassificationMode, _cfg.Classification?.ClassificationMode ?? "category");
            SelectComboByValue(CmbTimeGranularity, _cfg.Classification?.TimeGranularity ?? "month");
        }

        #region Browse
        private void BrowseRoot_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "選擇根目錄",
                FileName = "在此輸入任意檔名",
                Filter = "所有檔案|*.*"
            };
            if (dlg.ShowDialog() == true)
                TxtRootDir.Text = Path.GetDirectoryName(dlg.FileName) ?? "";
        }

        private void BrowseInbox_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "選擇收件匣資料夾",
                FileName = "在此輸入任意檔名",
                Filter = "所有檔案|*.*"
            };
            if (dlg.ShowDialog() == true)
                TxtInboxDir.Text = Path.GetDirectoryName(dlg.FileName) ?? "";
        }

        private void BrowseDb_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "選擇或建立資料庫檔 (*.db)",
                FileName = "assistant.db",
                Filter = "SQLite Database (*.db)|*.db|所有檔案 (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
                TxtDbPath.Text = dlg.FileName;
        }
        #endregion

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _cfg.App ??= new AppSection();
                _cfg.OpenAI ??= new OpenAISection();
                _cfg.Classification ??= new ClassificationSection();

                // App
                _cfg.App.RootDir = TxtRootDir.Text.Trim();
                _cfg.App.InboxDir = TxtInboxDir.Text.Trim();
                _cfg.App.DbPath = TxtDbPath.Text.Trim();
                _cfg.App.DryRun = ChkDryRun.IsChecked == true;
                _cfg.App.Overwrite = ChkOverwrite.IsChecked == true;
                _cfg.App.MoveMode = GetComboText(CmbMoveMode, "copy");

                // OpenAI
                _cfg.OpenAI.ApiKey = TxtApiKey.Password?.Trim() ?? "";

                // Classification
                _cfg.Classification.ClassificationMode = GetComboText(CmbClassificationMode, "category");
                _cfg.Classification.TimeGranularity = GetComboText(CmbTimeGranularity, "month");

                ConfigService.Save(_configPath, _cfg);

                MessageBox.Show("設定已儲存！", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"儲存失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        #region Helpers
        private static void SelectComboByValue(ComboBox combo, string? value)
        {
            if (combo == null || string.IsNullOrEmpty(value)) return;
            foreach (var item in combo.Items)
            {
                if (item is ComboBoxItem ci &&
                    string.Equals(ci.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = ci;
                    break;
                }
            }
        }

        private static string GetComboText(ComboBox combo, string fallback)
        {
            if (combo?.SelectedItem is ComboBoxItem ci)
                return ci.Content?.ToString() ?? fallback;
            return fallback;
        }
        #endregion
    }
}
