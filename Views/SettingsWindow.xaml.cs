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

        public SettingsWindow(string configPath, AppConfig cfg)
        {
            InitializeComponent();

            _configPath = configPath ?? "config.json";
            _cfg = cfg ?? new AppConfig();

            // 載入 UI
            LoadToUI();
        }

        #region Load / Save

        private void LoadToUI()
        {
            // App
            TxtRootDir.Text = _cfg.App?.RootDir ?? string.Empty;
            TxtInboxDir.Text = _cfg.App?.InboxDir ?? string.Empty;
            TxtDbPath.Text = _cfg.App?.DbPath ?? string.Empty;

            ChkDryRun.IsChecked = _cfg.App?.DryRun ?? false;
            ChkOverwrite.IsChecked = _cfg.App?.Overwrite ?? false;

            SelectComboByValue(CmbMoveMode, _cfg.App?.MoveMode ?? "copy");

            // OpenAI
            TxtApiKey.Password = _cfg.OpenAI?.ApiKey ?? string.Empty;

            // Classification
            SelectComboByValue(CmbClassificationMode, _cfg.Classification?.ClassificationMode ?? "category");

            // Routing
            SelectComboByValue(CmbTimeGranularity, _cfg.Routing?.TimeGranularity ?? "month");
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _cfg.App ??= new AppSection();
                _cfg.OpenAI ??= new OpenAISection();
                _cfg.Routing ??= new RoutingSection();
                _cfg.Classification ??= new ClassificationSection();

                // App
                _cfg.App.RootDir = (TxtRootDir.Text ?? string.Empty).Trim();
                _cfg.App.InboxDir = (TxtInboxDir.Text ?? string.Empty).Trim();
                _cfg.App.DbPath = (TxtDbPath.Text ?? string.Empty).Trim();
                _cfg.App.DryRun = ChkDryRun.IsChecked == true;
                _cfg.App.Overwrite = ChkOverwrite.IsChecked == true;
                _cfg.App.MoveMode = GetComboText(CmbMoveMode, "copy");

                // OpenAI
                _cfg.OpenAI.ApiKey = TxtApiKey.Password ?? string.Empty;

                // Classification / Routing
                _cfg.Classification.ClassificationMode = GetComboText(CmbClassificationMode, "category");
                _cfg.Routing.TimeGranularity = GetComboText(CmbTimeGranularity, "month");

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

        #endregion

        #region Browse Buttons

        private void BrowseRoot_Click(object sender, RoutedEventArgs e)
        {
            var picked = PickFolderLikeFileDialog(TxtRootDir.Text);
            if (!string.IsNullOrWhiteSpace(picked))
                TxtRootDir.Text = picked!;
        }

        private void BrowseInbox_Click(object sender, RoutedEventArgs e)
        {
            var picked = PickFolderLikeFileDialog(TxtInboxDir.Text);
            if (!string.IsNullOrWhiteSpace(picked))
                TxtInboxDir.Text = picked!;
        }

        private void BrowseDb_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "選擇資料庫檔 (.db)",
                Filter = "SQLite Database (*.db)|*.db|所有檔案 (*.*)|*.*",
                CheckFileExists = false
            };
            if (dlg.ShowDialog() == true)
                TxtDbPath.Text = dlg.FileName;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// WPF 原生沒有選資料夾的對話框：借用 OpenFileDialog 來取得「所在資料夾」。
        /// 作法：使用者在目標資料夾任意輸入一個檔名 → 取其資料夾路徑。
        /// </summary>
        private static string? PickFolderLikeFileDialog(string? initialFolder)
        {
            var dlg = new SaveFileDialog
            {
                Title = "選取資料夾（隨便輸入檔名後按儲存）",
                FileName = "擷取這個資料夾",
                Filter = "任何檔案 (*.*)|*.*",
                OverwritePrompt = false
            };

            try
            {
                if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
                    dlg.InitialDirectory = initialFolder;
            }
            catch { /* ignore invalid paths */ }

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var dir = Path.GetDirectoryName(dlg.FileName);
                    if (!string.IsNullOrWhiteSpace(dir)) return dir;
                }
                catch { /* ignore */ }
            }
            return null;
        }

        private static string GetComboText(ComboBox combo, string fallback)
        {
            if (combo?.SelectedItem is ComboBoxItem cbi)
                return cbi.Content?.ToString() ?? fallback;

            // 若使用者沒選，就以第一個項目或 fallback
            if (combo?.Items.Count > 0 && combo.Items[0] is ComboBoxItem first)
                return first.Content?.ToString() ?? fallback;

            return fallback;
        }

        private static void SelectComboByValue(ComboBox combo, string? value)
        {
            if (combo == null) return;
            if (string.IsNullOrWhiteSpace(value))
            {
                combo.SelectedIndex = 0;
                return;
            }

            value = value.Trim();
            foreach (var item in combo.Items)
            {
                if (item is ComboBoxItem cbi &&
                    string.Equals(cbi.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    cbi.IsSelected = true;
                    combo.SelectedItem = cbi;
                    return;
                }
            }
            // 沒命中就選第一個
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        #endregion
    }
}
