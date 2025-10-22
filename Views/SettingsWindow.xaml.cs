using AI.KB.Assistant.Models;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AI.KB.Assistant.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly string _configPath = "config.json";
        private AppConfig _cfg;
        private readonly MainWindow? _owner;

        public SettingsWindow(MainWindow owner, AppConfig cfg)
        {
            InitializeComponent();
            _owner = owner;
            _cfg = cfg ?? AppConfig.Load(_configPath);
            _cfg.ThemeColors ??= new ThemeColors(); // 安全防呆

            LoadConfig();
            LoadThemeToUi();
            UpdateThemePreview();
        }

        // ===================== 一般設定載入 =====================

        private void LoadConfig()
        {
            // 🧭 App
            TxtDbPath.Text = _cfg.App.DbPath ?? "";
            TxtRootDir.Text = _cfg.App.RootDir ?? "";

            // 📥 Import
            CbAutoOnDrop.IsChecked = _cfg.Import.AutoOnDrop;
            CbIncludeSubdir.IsChecked = _cfg.Import.IncludeSubdirectories;
            TxtHotFolder.Text = _cfg.Import.HotFolderPath ?? "";

            // 搬檔 / 覆蓋策略（中文顯示，但 SelectedIndex 仍對應列舉順序）
            CbMoveMode.SelectedIndex = (int)_cfg.Import.MoveMode;                 // 0=Move,1=Copy
            CbOverwritePolicy.SelectedIndex = (int)_cfg.Import.OverwritePolicy;   // 0=Replace,1=Rename,2=Skip

            TxtBlacklistFolders.Text = string.Join(", ", _cfg.Import.BlacklistFolderNames ?? Array.Empty<string>());
            TxtBlacklistExts.Text = string.Join(", ", _cfg.Import.BlacklistExts ?? Array.Empty<string>());

            // 🧩 Routing
            CbUseYear.IsChecked = _cfg.Routing.UseYear;
            CbUseMonth.IsChecked = _cfg.Routing.UseMonth;
            CbUseType.IsChecked = _cfg.Routing.UseType;
            CbUseProject.IsChecked = _cfg.Routing.UseProject;

            // 🧠 LLM
            TxtApiKey.Password = _cfg.OpenAI.ApiKey ?? "";
            TxtModel.Text = _cfg.OpenAI.Model ?? "";
            CbLmLowConf.IsChecked = _cfg.OpenAI.EnableWhenLowConfidence;

            // 🧩 Extension Groups（「陣列單行」預覽）
            if (_cfg.Routing.ExtensionGroups != null && FindName("TxtExtGroups") is TextBox tb)
                tb.Text = BuildExtGroupsPreview(_cfg.Routing.ExtensionGroups);
        }

        private static string[] ParseList(string? text) =>
            (text ?? "")
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            // 🧭 App
            _cfg.App.DbPath = TxtDbPath.Text.Trim();
            _cfg.App.RootDir = TxtRootDir.Text.Trim();

            // 📥 Import
            _cfg.Import.AutoOnDrop = CbAutoOnDrop.IsChecked == true;
            _cfg.Import.IncludeSubdirectories = CbIncludeSubdir.IsChecked == true;
            _cfg.Import.HotFolderPath = TxtHotFolder.Text.Trim();
            _cfg.Import.MoveMode = (MoveMode)CbMoveMode.SelectedIndex;
            _cfg.Import.OverwritePolicy = (OverwritePolicy)CbOverwritePolicy.SelectedIndex;

            _cfg.Import.BlacklistFolderNames = ParseList(TxtBlacklistFolders.Text);
            _cfg.Import.BlacklistExts = ParseList(TxtBlacklistExts.Text);

            // 🧩 Routing
            _cfg.Routing.UseYear = CbUseYear.IsChecked == true;
            _cfg.Routing.UseMonth = CbUseMonth.IsChecked == true;
            _cfg.Routing.UseType = CbUseType.IsChecked == true;
            _cfg.Routing.UseProject = CbUseProject.IsChecked == true;

            // 🧠 LLM
            _cfg.OpenAI.ApiKey = TxtApiKey.Password.Trim();
            _cfg.OpenAI.Model = TxtModel.Text.Trim();
            _cfg.OpenAI.EnableWhenLowConfidence = CbLmLowConf.IsChecked == true;

            // 🧩 Extension Groups（不論是「一般 JSON」或「單行陣列 JSON」，都會嘗試解析）
            if (FindName("TxtExtGroups") is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                try
                {
                    var eg = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
                        tb.Text,
                        new JsonSerializerOptions
                        {
                            ReadCommentHandling = JsonCommentHandling.Skip,
                            AllowTrailingCommas = true
                        }
                    ) ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

                    // 正規化副檔名（去掉點、小寫、去重）
                    var norm = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in eg)
                    {
                        var k = (kv.Key ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(k)) continue;

                        var exts = (kv.Value ?? Array.Empty<string>())
                           .Select(v => (v ?? "").Trim().TrimStart('.').ToLowerInvariant())
                           .Where(v => !string.IsNullOrWhiteSpace(v))
                           .Distinct()
                           .ToArray();
                        norm[k] = exts;
                    }
                    _cfg.Routing.ExtensionGroups = norm;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Extension Groups 解析失敗：{ex.Message}", "格式錯誤",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // 💾 保存
            AppConfig.Save(_configPath, _cfg);
            _owner?.ReloadConfig();
            DialogResult = true;
            Close();
        }

        // ===================== 檔案/資料夾選擇 =====================

        private void BtnPickDbPath_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "SQLite Database (*.db;*.sqlite)|*.db;*.sqlite|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
                TxtDbPath.Text = dlg.FileName;
        }

        private void BtnPickRootDir_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "選取專案根目錄"
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TxtRootDir.Text = dlg.SelectedPath;
        }

        private void BtnPickHotFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "選取 HotFolder 監控資料夾"
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TxtHotFolder.Text = dlg.SelectedPath;
        }

        // ===================== Theme Colors（多組色碼＋預覽） =====================

        private void LoadThemeToUi()
        {
            var t = _cfg.ThemeColors;

            TbBg.Text = t.Background;
            TbPanel.Text = t.Panel;
            TbBorder.Text = t.Border;
            TbText.Text = t.Text;
            TbTextMuted.Text = t.TextMuted;

            TbPrimary.Text = t.Primary;
            TbPrimaryHover.Text = t.PrimaryHover;
            TbSecondary.Text = t.Secondary;

            TbBannerInfo.Text = t.BannerInfo;
            TbBannerWarn.Text = t.BannerWarn;
            TbBannerError.Text = t.BannerError;

            TbSuccess.Text = t.Success;
            TbWarning.Text = t.Warning;
            TbError.Text = t.Error;
        }

        private static SolidColorBrush BrushFrom(string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return new SolidColorBrush(Colors.Transparent); }
        }

        private void UpdateThemePreview()
        {
            PrevBg.Background = BrushFrom(TbBg.Text);
            PrevPanel.Background = BrushFrom(TbPanel.Text);
            PrevBorder.Background = BrushFrom(TbBorder.Text);
            PrevText.Background = BrushFrom(TbText.Text);
            PrevTextMuted.Background = BrushFrom(TbTextMuted.Text);

            PrevPrimary.Background = BrushFrom(TbPrimary.Text);
            PrevPrimaryHover.Background = BrushFrom(TbPrimaryHover.Text);
            PrevSecondary.Background = BrushFrom(TbSecondary.Text);

            PrevBannerInfo.Background = BrushFrom(TbBannerInfo.Text);
            PrevBannerWarn.Background = BrushFrom(TbBannerWarn.Text);
            PrevBannerError.Background = BrushFrom(TbBannerError.Text);

            PrevSuccess.Background = BrushFrom(TbSuccess.Text);
            PrevWarning.Background = BrushFrom(TbWarning.Text);
            PrevError.Background = BrushFrom(TbError.Text);
        }

        private static bool TryPickColor(ref string hex)
        {
            using var dlg = new System.Windows.Forms.ColorDialog();
            try { dlg.Color = System.Drawing.ColorTranslator.FromHtml(hex); } catch { /* ignore */ }
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var c = dlg.Color;
                hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                return true;
            }
            return false;
        }

        // 個別挑色（左列）
        private void PickBg_Click(object sender, RoutedEventArgs e) { var s = TbBg.Text; if (TryPickColor(ref s)) { TbBg.Text = s; UpdateThemePreview(); } }
        private void PickPanel_Click(object sender, RoutedEventArgs e) { var s = TbPanel.Text; if (TryPickColor(ref s)) { TbPanel.Text = s; UpdateThemePreview(); } }
        private void PickBorder_Click(object sender, RoutedEventArgs e) { var s = TbBorder.Text; if (TryPickColor(ref s)) { TbBorder.Text = s; UpdateThemePreview(); } }
        private void PickText_Click(object sender, RoutedEventArgs e) { var s = TbText.Text; if (TryPickColor(ref s)) { TbText.Text = s; UpdateThemePreview(); } }
        private void PickTextMuted_Click(object sender, RoutedEventArgs e) { var s = TbTextMuted.Text; if (TryPickColor(ref s)) { TbTextMuted.Text = s; UpdateThemePreview(); } }
        private void PickPrimary_Click(object sender, RoutedEventArgs e) { var s = TbPrimary.Text; if (TryPickColor(ref s)) { TbPrimary.Text = s; UpdateThemePreview(); } }
        private void PickPrimaryHover_Click(object sender, RoutedEventArgs e) { var s = TbPrimaryHover.Text; if (TryPickColor(ref s)) { TbPrimaryHover.Text = s; UpdateThemePreview(); } }

        // 個別挑色（右列）
        private void PickSecondary_Click(object sender, RoutedEventArgs e) { var s = TbSecondary.Text; if (TryPickColor(ref s)) { TbSecondary.Text = s; UpdateThemePreview(); } }
        private void PickBannerInfo_Click(object sender, RoutedEventArgs e) { var s = TbBannerInfo.Text; if (TryPickColor(ref s)) { TbBannerInfo.Text = s; UpdateThemePreview(); } }
        private void PickBannerWarn_Click(object sender, RoutedEventArgs e) { var s = TbBannerWarn.Text; if (TryPickColor(ref s)) { TbBannerWarn.Text = s; UpdateThemePreview(); } }
        private void PickBannerError_Click(object sender, RoutedEventArgs e) { var s = TbBannerError.Text; if (TryPickColor(ref s)) { TbBannerError.Text = s; UpdateThemePreview(); } }
        private void PickSuccess_Click(object sender, RoutedEventArgs e) { var s = TbSuccess.Text; if (TryPickColor(ref s)) { TbSuccess.Text = s; UpdateThemePreview(); } }
        private void PickWarning_Click(object sender, RoutedEventArgs e) { var s = TbWarning.Text; if (TryPickColor(ref s)) { TbWarning.Text = s; UpdateThemePreview(); } }
        private void PickError_Click(object sender, RoutedEventArgs e) { var s = TbError.Text; if (TryPickColor(ref s)) { TbError.Text = s; UpdateThemePreview(); } }

        private void SaveTheme_Click(object sender, RoutedEventArgs e)
        {
            var t = _cfg.ThemeColors ??= new ThemeColors();

            t.Background = TbBg.Text;
            t.Panel = TbPanel.Text;
            t.Border = TbBorder.Text;
            t.Text = TbText.Text;
            t.TextMuted = TbTextMuted.Text;

            t.Primary = TbPrimary.Text;
            t.PrimaryHover = TbPrimaryHover.Text;
            t.Secondary = TbSecondary.Text;

            t.BannerInfo = TbBannerInfo.Text;
            t.BannerWarn = TbBannerWarn.Text;
            t.BannerError = TbBannerError.Text;

            t.Success = TbSuccess.Text;
            t.Warning = TbWarning.Text;
            t.Error = TbError.Text;

            ApplyThemeToResources(t);
            AppConfig.Save(_configPath, _cfg);
            MessageBox.Show("主題設定已套用並儲存。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ResetTheme_Click(object sender, RoutedEventArgs e)
        {
            _cfg.ThemeColors = new ThemeColors(); // 回預設（對應你目前 Theme.xaml 的淺色主題）
            LoadThemeToUi();
            UpdateThemePreview();
            ApplyThemeToResources(_cfg.ThemeColors);
            AppConfig.Save(_configPath, _cfg);
        }

        private static void ApplyThemeToResources(ThemeColors t)
        {
            var dict = Application.Current?.Resources;
            if (dict == null) return;

            void Set(string key, string hex)
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(hex);
                    if (dict[key] is SolidColorBrush b) b.Color = c;
                    else dict[key] = new SolidColorBrush(c);
                }
                catch { /* ignore invalid */ }
            }

            Set("App.BackgroundBrush", t.Background);
            Set("App.PanelBrush", t.Panel);
            Set("App.BorderBrush", t.Border);
            Set("App.TextBrush", t.Text);
            Set("App.TextMutedBrush", t.TextMuted);

            Set("App.PrimaryBrush", t.Primary);
            Set("App.PrimaryHover", t.PrimaryHover);
            Set("App.SecondaryBrush", t.Secondary);

            Set("App.BannerInfoBrush", t.BannerInfo);
            Set("App.BannerWarnBrush", t.BannerWarn);
            Set("App.BannerErrorBrush", t.BannerError);

            Set("App.SuccessBrush", t.Success);
            Set("App.WarningBrush", t.Warning);
            Set("App.ErrorBrush", t.Error);
        }

        private void ThemeTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateThemePreview();

        // ===================== Extension Groups：陣列單行 =====================

        /// <summary>
        /// 依照「陣列單行」的可讀格式輸出。
        /// 例：
        ///   "Images": [ "png", "jpg", "jpeg", ... ]
        /// 各群組之間以換行分隔；陣列元素以逗號＋空白分隔，不另外斷行。
        /// </summary>
        private static string BuildExtGroupsPreview(Dictionary<string, string[]> eg)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            var groups = eg.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToArray();
            for (int i = 0; i < groups.Length; i++)
            {
                var (key, arr) = (groups[i].Key, groups[i].Value ?? Array.Empty<string>());
                var items = arr.Select(v => $"\"{v}\"");
                sb.Append("  \"").Append(key).Append("\": [ ").Append(string.Join(", ", items)).Append(" ]");
                if (i < groups.Length - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// 將目前 TextBox 內的 JSON（不論原先是否多行陣列）整理成「陣列單行」輸出。
        /// </summary>
        private void BtnCompactExtGroups_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("TxtExtGroups") is not TextBox tb) return;
            try
            {
                var eg = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
                    tb.Text,
                    new JsonSerializerOptions
                    {
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    }
                ) ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

                tb.Text = BuildExtGroupsPreview(eg);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"整理失敗：{ex.Message}", "格式錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
