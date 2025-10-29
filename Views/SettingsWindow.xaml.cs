using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly string _cfgPath;
        private AppConfig _cfg;
        private readonly MainWindow? _ownerMain;

        public SettingsWindow(MainWindow owner, AppConfig cfg)
        {
            InitializeComponent();
            _ownerMain = owner;
            _cfg = cfg ?? new AppConfig();
            _cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            Loaded += SettingsWindow_Loaded;
        }

        public SettingsWindow()
        {
            InitializeComponent();
            _cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            _cfg = ConfigService.TryLoad(_cfgPath);
            Loaded += SettingsWindow_Loaded;
        }

        private void SettingsWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            _cfg.App ??= new AppConfig.AppSection();
            _cfg.Import ??= new AppConfig.ImportSection();
            _cfg.Routing ??= new AppConfig.RoutingSection();
            _cfg.Classification ??= new AppConfig.ClassificationSection();
            _cfg.OpenAI ??= new AppConfig.OpenAISection();

            // ===== 基礎 =====
            TxtRootDir.Text = _cfg.App.RootDir ?? "";
            TxtHotFolder.Text = _cfg.Import.HotFolderPath ?? "";
            TxtDbPath.Text = _cfg.App.DbPath ?? "";

            // ===== 匯入（以 enum 名稱顯示）=====
            SetComboByText(CmbMoveMode, _cfg.Import.MoveMode.ToString());
            SetComboByText(CmbOverwritePolicy, _cfg.Import.OverwritePolicy.ToString());
            ChkIncludeSubdir.IsChecked = _cfg.Import.IncludeSubdir;

            TxtBlacklistExts.Text = string.Join(", ",
                (_cfg.Import.BlacklistExts ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)));

            // ===== 路由 =====
            TxtAutoFolderName.Text = _cfg.Routing.AutoFolderName ?? "自整理";
            TxtLowConfidenceFolderName.Text = _cfg.Routing.LowConfidenceFolderName ?? "信心不足";
            ChkUseYear.IsChecked = _cfg.Routing.UseYear;
            ChkUseMonth.IsChecked = _cfg.Routing.UseMonth;
            ChkUseProject.IsChecked = _cfg.Routing.UseProject;
            ChkUseType.IsChecked = _cfg.Routing.UseType;

            TxtBlacklistFolders.Text = string.Join(", ",
                (_cfg.Import.BlacklistFolderNames ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)));

            // ===== AI =====
            SldThreshold.Value = Guard01(_cfg.Classification.ConfidenceThreshold, 0.75);
            TxtThresholdValue.Text = $"{SldThreshold.Value:0.00}";
            TxtModel.Text = _cfg.OpenAI.Model ?? "gpt-4o-mini";

            // ===== 主題（僅重點色，從現有資源讀）=====
            TxtAccentColor.Text = TryGetBrushHex("App.PrimaryBrush") ?? "#3B82F6";
        }

        // ========= 事件 =========

        private void BtnPickRoot_Click(object sender, RoutedEventArgs e)
        {
            var p = PickFolder(TxtRootDir.Text);
            if (!string.IsNullOrWhiteSpace(p)) TxtRootDir.Text = p;
        }

        private void BtnPickHot_Click(object sender, RoutedEventArgs e)
        {
            var p = PickFolder(TxtHotFolder.Text);
            if (!string.IsNullOrWhiteSpace(p)) TxtHotFolder.Text = p;
        }

        private void BtnPickDb_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "選擇或建立 SQLite 檔案",
                Filter = "SQLite (*.db)|*.db|所有檔案 (*.*)|*.*",
                FileName = string.IsNullOrWhiteSpace(TxtDbPath.Text) ? "ai.kb.assistant.db" : TxtDbPath.Text
            };
            if (dlg.ShowDialog(this) == true) TxtDbPath.Text = dlg.FileName;
        }

        private void SldThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtThresholdValue != null)
                TxtThresholdValue.Text = $"{SldThreshold.Value:0.00}";
        }

        private void BtnApplyTheme_Click(object sender, RoutedEventArgs e)
        {
            var accent = string.IsNullOrWhiteSpace(TxtAccentColor.Text) ? "#3B82F6" : TxtAccentColor.Text.Trim();
            ThemeService.ApplyAccent(accent);
            MessageBox.Show(this, "主題已套用（Primary/PrimaryHover）。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            PullUiIntoConfig();
            ConfigService.Save(_cfgPath, _cfg);
            _ownerMain?.ReloadConfig();
            DialogResult = true;
            Close();
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            _cfg = ConfigService.TryLoad(_cfgPath);
            SettingsWindow_Loaded(this, new RoutedEventArgs());
            MessageBox.Show(this, "設定已重新載入。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ========= 私有 =========

        private static void SetComboByText(ComboBox combo, string text)
        {
            if (combo == null) return;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                var it = combo.Items[i] as ComboBoxItem;
                if (string.Equals(it?.Content?.ToString(), text, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            if (combo.Items.Count > 0 && combo.SelectedIndex < 0) combo.SelectedIndex = 0;
        }

        private string PickFolder(string? seed)
        {
            var msg = "請在『檔案總管』複製路徑後貼到輸入框。\n（此按鈕暫以現有文字為主，不開對話框）";
            if (!string.IsNullOrEmpty(seed))
            {
                MessageBox.Show(this, msg, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return seed;
            }
            MessageBox.Show(this, msg, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private static string[] SplitCsv(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
            return text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .Where(s => s.Length > 0)
                       .ToArray();
        }

        private static double Guard01(double v, double @default)
        {
            if (double.IsNaN(v)) return @default;
            if (v < 0) return 0;
            if (v > 1) return 1;
            return v;
        }

        private static string? TryGetBrushHex(string key)
        {
            try
            {
                if (Application.Current.Resources[key] is SolidColorBrush b)
                    return $"#{b.Color.A:X2}{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}";
            }
            catch { }
            return null;
        }

        private void PullUiIntoConfig()
        {
            _cfg.App ??= new AppConfig.AppSection();
            _cfg.Import ??= new AppConfig.ImportSection();
            _cfg.Routing ??= new AppConfig.RoutingSection();
            _cfg.Classification ??= new AppConfig.ClassificationSection();
            _cfg.OpenAI ??= new AppConfig.OpenAISection();

            // ===== 基礎 =====
            _cfg.App.RootDir = (TxtRootDir.Text ?? "").Trim();
            _cfg.Import.HotFolderPath = (TxtHotFolder.Text ?? "").Trim();
            _cfg.App.DbPath = (TxtDbPath.Text ?? "").Trim();

            // ===== 匯入（ComboBox → Enum）=====
            _cfg.Import.MoveMode =
                Enum.TryParse<MoveMode>((CmbMoveMode.SelectedItem as ComboBoxItem)?.Content?.ToString(), true, out var mm)
                    ? mm : MoveMode.Move;

            _cfg.Import.OverwritePolicy =
                Enum.TryParse<OverwritePolicy>((CmbOverwritePolicy.SelectedItem as ComboBoxItem)?.Content?.ToString(), true, out var op)
                    ? op : OverwritePolicy.Rename;

            _cfg.Import.IncludeSubdir = ChkIncludeSubdir.IsChecked == true;

            _cfg.Import.BlacklistExts = SplitCsv(TxtBlacklistExts.Text)
                                        .Select(s => s.TrimStart('.').ToLowerInvariant())
                                        .Distinct(StringComparer.OrdinalIgnoreCase)
                                        .ToArray();

            // ===== 路由 =====
            _cfg.Routing.AutoFolderName = string.IsNullOrWhiteSpace(TxtAutoFolderName.Text) ? "自整理" : TxtAutoFolderName.Text.Trim();
            _cfg.Routing.LowConfidenceFolderName = string.IsNullOrWhiteSpace(TxtLowConfidenceFolderName.Text) ? "信心不足" : TxtLowConfidenceFolderName.Text.Trim();
            _cfg.Routing.UseYear = ChkUseYear.IsChecked == true;
            _cfg.Routing.UseMonth = ChkUseMonth.IsChecked == true;
            _cfg.Routing.UseProject = ChkUseProject.IsChecked == true;
            _cfg.Routing.UseType = ChkUseType.IsChecked == true;

            _cfg.Import.BlacklistFolderNames = SplitCsv(TxtBlacklistFolders.Text);

            // ===== AI =====
            _cfg.Classification.ConfidenceThreshold = Guard01(SldThreshold.Value, 0.75);
            if (!string.IsNullOrWhiteSpace(TxtModel.Text))
                _cfg.OpenAI.Model = TxtModel.Text.Trim();

            // 主題不寫入 _cfg（使用 ThemeService.ApplyAccent 即時套用）
        }
    }
}
