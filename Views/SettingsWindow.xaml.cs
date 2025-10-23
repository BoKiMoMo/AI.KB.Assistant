using AI.KB.Assistant.Models;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
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

            _cfg.ThemeColors ??= new ThemeColors(); // safety

            LoadConfig();
            // 若還有主題選項 UI，就呼叫這兩個；若沒有，保留不影響
            TryLoadThemeToUi();
            TryUpdateThemePreview();
        }

        // ---------------- App / Import / Routing / LLM / ExtGroups ----------------

        private void LoadConfig()
        {
            // 🧭 App
            TxtDbPath.Text = _cfg.App.DbPath ?? "";
            TxtRootDir.Text = _cfg.App.RootDir ?? "";

            // 📥 Import
            CbAutoOnDrop.IsChecked = _cfg.Import.AutoOnDrop;
            CbIncludeSubdir.IsChecked = _cfg.Import.IncludeSubdirectories;
            TxtHotFolder.Text = _cfg.Import.HotFolderPath ?? "";

            // 搬檔 / 覆蓋（以 index 對應）
            CbMoveMode.SelectedIndex = (int)_cfg.Import.MoveMode;           // 0: Move, 1: Copy
            CbOverwritePolicy.SelectedIndex = (int)_cfg.Import.OverwritePolicy;    // 0: Replace, 1: Rename, 2: Skip

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

            // 🧩 Extension Groups JSON：輸出緊湊格式
            if (_cfg.Routing.ExtensionGroups != null && FindName("TxtExtGroups") is TextBox tb)
            {
                var compact = ToCompactJson(_cfg.Routing.ExtensionGroups);
                tb.Text = compact;
            }
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

            // 🧩 Extension Groups JSON 匯入（容錯）
            if (FindName("TxtExtGroups") is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                try
                {
                    var eg = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string[]>>(
                        tb.Text,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                            AllowTrailingCommas = true
                        }
                    ) ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

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

                    // 將緊湊格式回寫（使用者若貼入鬆散格式，存檔時會轉成緊湊）
                    tb.Text = ToCompactJson(norm);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Extension Groups JSON 解析失敗：{ex.Message}", "格式錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // 💾 Save & 通知主視窗
            AppConfig.Save(_configPath, _cfg);
            _owner?.ReloadConfig();
            DialogResult = true;
            Close();
        }

        // 🔧 檔案對話框
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
                Description = "選取 HotFolder（收件夾）"
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TxtHotFolder.Text = dlg.SelectedPath;
        }

        // ---------------- Theme（若你保留主題色 UI 就會用到；沒有的話不影響） ----------------

        private void TryLoadThemeToUi()
        {
            // 你的主題色 TextBox 名稱若存在，則載入；不存在就略過
            var t = _cfg.ThemeColors;
            SetIfFound("TbBg", t.Background);
            SetIfFound("TbPanel", t.Panel);
            SetIfFound("TbBorder", t.Border);
            SetIfFound("TbText", t.Text);
            SetIfFound("TbTextMuted", t.TextMuted);
            SetIfFound("TbPrimary", t.Primary);
            SetIfFound("TbPrimaryHover", t.PrimaryHover);
            SetIfFound("TbSecondary", t.Secondary);
            SetIfFound("TbBannerInfo", t.BannerInfo);
            SetIfFound("TbBannerWarn", t.BannerWarn);
            SetIfFound("TbBannerError", t.BannerError);
            SetIfFound("TbSuccess", t.Success);
            SetIfFound("TbWarning", t.Warning);
            SetIfFound("TbError", t.Error);
        }

        private void TryUpdateThemePreview()
        {
            // 若預覽的 Rectangle/Border 存在就套色；否則略過
            PaintIfFound("PrevBg", _cfg.ThemeColors.Background);
            PaintIfFound("PrevPanel", _cfg.ThemeColors.Panel);
            PaintIfFound("PrevBorder", _cfg.ThemeColors.Border);
            PaintIfFound("PrevText", _cfg.ThemeColors.Text);
            PaintIfFound("PrevTextMuted", _cfg.ThemeColors.TextMuted);
            PaintIfFound("PrevPrimary", _cfg.ThemeColors.Primary);
            PaintIfFound("PrevPrimaryHover", _cfg.ThemeColors.PrimaryHover);
            PaintIfFound("PrevSecondary", _cfg.ThemeColors.Secondary);
            PaintIfFound("PrevBannerInfo", _cfg.ThemeColors.BannerInfo);
            PaintIfFound("PrevBannerWarn", _cfg.ThemeColors.BannerWarn);
            PaintIfFound("PrevBannerError", _cfg.ThemeColors.BannerError);
            PaintIfFound("PrevSuccess", _cfg.ThemeColors.Success);
            PaintIfFound("PrevWarning", _cfg.ThemeColors.Warning);
            PaintIfFound("PrevError", _cfg.ThemeColors.Error);
        }

        private void SetIfFound(string name, string value)
        {
            if (FindName(name) is TextBox tb) tb.Text = value ?? "";
        }
        private void PaintIfFound(string name, string hex)
        {
            if (FindName(name) is Border b) b.Background = BrushFrom(hex);
        }

        private static SolidColorBrush BrushFrom(string hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return new SolidColorBrush(Colors.Transparent); }
        }

        // ---------------- 工具：Extension Groups 緊湊 JSON ----------------
        private static string ToCompactJson(Dictionary<string, string[]> map)
        {
            // 將陣列輸出為單行 ["a", "b", "c"] 風格
            // 例： "Images": [ "png", "jpg", "jpeg", ... ]
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");
            var i = 0;
            foreach (var kv in map.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (i++ > 0) sb.Append(",\n");
                sb.Append("  \"").Append(kv.Key).Append("\": [ ");
                var j = 0;
                foreach (var v in kv.Value ?? Array.Empty<string>())
                {
                    if (j++ > 0) sb.Append(", ");
                    sb.Append("\"").Append(v).Append("\"");
                }
                sb.Append(" ]");
            }
            sb.Append("\n}");
            return sb.ToString();
        }
    }
}
