using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging; // 引入 BitmapImage
using WinForms = System.Windows.Forms;

namespace AI.KB.Assistant.Views
{
    /// <summary>
    /// V20.13.25 (設定視窗資源與圖示修復版)
    /// 1. [Fix XAML Parse Error] 移除 XAML 圖示參照，改為在 Code-behind 中手動設定 Icon。
    /// 2. 移除冗餘的資源合併邏輯。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private static readonly Dictionary<string, string> TokenToDisplay = new(StringComparer.OrdinalIgnoreCase)
        {
            { "year",     "year / 年份" },
            { "month",    "month / 月份" },
            { "project",  "project / 專案" },
            { "category", "category / 類別" },
        };
        private static readonly HashSet<string> ValidTokens =
            new(new[] { "year", "month", "project", "category" }, StringComparer.OrdinalIgnoreCase);

        private AppConfig _tempConfig;

        public SettingsWindow()
        {
            InitializeComponent();

            // [Fix XAML Parse Error] 由於 XAML 解析器在載入 Icon 資源時會拋錯，我們手動在 Code-behind 設定
            try
            {
                // 假設 LOGO 圖片已設為 Resource 且位於根目錄
                this.Icon = new BitmapImage(new Uri("pack://application:,,,/石頭的LOGO_1.jpg", UriKind.Absolute));
            }
            catch (Exception ex)
            {
                // 如果圖示設置失敗，Log 錯誤，但不阻止視窗開啟
                Console.WriteLine($"[SettingsWindow Icon Error] {ex.Message}");
            }

            var current = ConfigService.Cfg;
            _tempConfig = current != null ? current.Clone() : new AppConfig();

            Loaded += SettingsWindow_Loaded;
        }

        private void SettingsWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                RenderFromConfig(_tempConfig);
                EnsureTopPathsNotBlank(_tempConfig);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"載入設定時發生例外：{ex.Message}", "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ========================= Config -> UI =========================
        private void RenderFromConfig(AppConfig cfg)
        {
            // [Safety] Ensure objects are not null
            cfg.App ??= new AppSection();
            cfg.Import ??= new ImportSection();
            cfg.Db ??= new DbSection();
            cfg.Routing ??= new RoutingSection();
            cfg.OpenAI ??= new OpenAISection();

            SetTextByNames(cfg.App.RootDir, "TbRootDir");
            SetTextByNames(cfg.Import.HotFolder, "TbHotFolder");
            SetTextByNames(cfg.Db.DbPath, "TbDbPath");

            string mode = cfg.App.LaunchMode;
            if (FindName("RadioModeSimple") is RadioButton r_simp && FindName("RadioModeDetailed") is RadioButton r_det)
            {
                if (mode == "Simple") r_simp.IsChecked = true; else r_det.IsChecked = true;
            }

            SetTextByNames(string.Join(Environment.NewLine, cfg.App.TreeViewRootPaths ?? new List<string>()), "TbTreeViewRoots");

            SetBool("CbIncludeSubdir", cfg.Import.IncludeSubdir);
            SetBool("CbEnableHotFolder", cfg.Import.EnableHotFolder);

            SetComboByRaw("CbMoveMode", cfg.Import.MoveMode, "copy");
            SetComboByRaw("CbOverwrite", cfg.Import.OverwritePolicy, "KeepBoth");

            SetTextByNames(JoinList(cfg.Routing.BlacklistExts), "TbBlacklistExts");
            SetTextByNames(JoinList(cfg.Routing.BlacklistFolderNames), "TbBlacklistFolders");

            SetBool("CbUseYear", cfg.Routing.UseYear);
            SetBool("CbUseMonth", cfg.Routing.UseMonth);
            SetBool("CbUseProject", cfg.Routing.UseProject);
            SetBool("CbUseCategory", cfg.Routing.UseCategory);

            SetComboByRaw("CbUseType", cfg.Routing.UseType, "rule+llm");

            var order = (cfg.Routing.FolderOrder == null || cfg.Routing.FolderOrder.Count == 0)
                ? RoutingService.DefaultOrder(cfg.Routing.UseCategory)
                : cfg.Routing.FolderOrder.ToList();

            foreach (var t in ValidTokens) if (!order.Contains(t, StringComparer.OrdinalIgnoreCase)) order.Add(t);

            if (FindName("LbFolderOrder") is ListBox lb)
            {
                lb.ItemsSource = order.Select(ToDisplay).ToList();
                if (lb.Items.Count > 0) lb.SelectedIndex = 0;
            }

            SetTextByNames(cfg.Routing.LowConfidenceFolderName, "TbLowConfidence");

            Slider? sld = FindName("SlThreshold") as Slider;
            TextBox? tbTh = FindName("TbThreshold") as TextBox;
            var th = cfg.Routing.Threshold;

            if (sld != null) sld.Value = Math.Clamp(th, 0, 1);
            if (tbTh != null)
            {
                tbTh.Text = th.ToString("F2");
                if (sld != null) sld.ValueChanged += (s, e) => tbTh.Text = e.NewValue.ToString("F2");
            }

            SetPasswordByNames(cfg.OpenAI.ApiKey, "TbApiKey");
            SetTextByNames(cfg.OpenAI.Model, "TbModel");
        }

        private void EnsureTopPathsNotBlank(AppConfig cfg)
        {
            if (string.IsNullOrWhiteSpace(GetTextByNames("TbRootDir"))) SetTextByNames(cfg.App.RootDir, "TbRootDir");
            if (string.IsNullOrWhiteSpace(GetTextByNames("TbHotFolder"))) SetTextByNames(cfg.Import.HotFolder, "TbHotFolder");
            if (string.IsNullOrWhiteSpace(GetTextByNames("TbDbPath"))) SetTextByNames(cfg.Db.DbPath, "TbDbPath");
        }

        private void HarvestToConfig(AppConfig cfg)
        {
            cfg.App.RootDir = GetTextByNames("TbRootDir");
            cfg.Import.HotFolder = GetTextByNames("TbHotFolder");
            cfg.Db.DbPath = GetTextByNames("TbDbPath");

            if (GetBoolByNames("RadioModeSimple")) cfg.App.LaunchMode = "Simple"; else cfg.App.LaunchMode = "Detailed";

            cfg.App.TreeViewRootPaths = SplitPaths(GetTextByNames("TbTreeViewRoots"));

            cfg.Import.IncludeSubdir = GetBoolByNames("CbIncludeSubdir");
            cfg.Import.EnableHotFolder = GetBoolByNames("CbEnableHotFolder");
            cfg.Import.MoveMode = GetComboRaw("CbMoveMode", "copy");
            cfg.Import.OverwritePolicy = GetComboRaw("CbOverwrite", "KeepBoth").Trim();

            cfg.Routing.BlacklistExts = SplitList(GetTextByNames("TbBlacklistExts"));
            cfg.Routing.BlacklistFolderNames = SplitList(GetTextByNames("TbBlacklistFolders"));
            cfg.Import.BlacklistExts = cfg.Routing.BlacklistExts;
            cfg.Import.BlacklistFolderNames = cfg.Routing.BlacklistFolderNames;

            cfg.Routing.UseYear = GetBoolByNames("CbUseYear");
            cfg.Routing.UseMonth = GetBoolByNames("CbUseMonth");
            cfg.Routing.UseProject = GetBoolByNames("CbUseProject");
            cfg.Routing.UseCategory = GetBoolByNames("CbUseCategory");
            cfg.Routing.UseType = GetComboRaw("CbUseType", "rule+llm");

            if (FindName("LbFolderOrder") is ListBox lb)
            {
                var list = lb.Items.Cast<object>().Select(x => FromDisplay(x?.ToString() ?? "")).Where(t => ValidTokens.Contains(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var t in ValidTokens) if (!list.Contains(t, StringComparer.OrdinalIgnoreCase)) list.Add(t);
                cfg.Routing.FolderOrder = list;
            }

            cfg.Routing.LowConfidenceFolderName = GetTextByNames("TbLowConfidence");

            Slider? sld = FindName("SlThreshold") as Slider;
            var thStr = GetTextByNames("TbThreshold");
            if (string.IsNullOrWhiteSpace(thStr) && sld != null) thStr = sld.Value.ToString("F2");

            if (double.TryParse(thStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedTh)) cfg.Routing.Threshold = Math.Clamp(parsedTh, 0, 1);
            else if (sld != null) cfg.Routing.Threshold = sld.Value;

            cfg.OpenAI.ApiKey = GetPasswordByNames("TbApiKey");
            cfg.OpenAI.Model = GetTextByNames("TbModel");
        }

        // ========================= Buttons =========================
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                HarvestToConfig(_tempConfig);
                if (!ConfigService.Save(_tempConfig)) { MessageBox.Show($"無法寫入設定檔：{ConfigService.ConfigPath}", "儲存失敗", MessageBoxButton.OK, MessageBoxImage.Error); return; }
                DialogResult = true; Close();
            }
            catch (Exception ex) { MessageBox.Show($"儲存設定時發生例外：{ex.Message}", "Settings", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ConfigService.Load();
                var current = ConfigService.Cfg;
                _tempConfig = current != null ? current.Clone() : new AppConfig();
                RenderFromConfig(_tempConfig);
                EnsureTopPathsNotBlank(_tempConfig);
            }
            catch (Exception ex) { MessageBox.Show($"重新載入時發生例外：{ex.Message}", "Settings", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        private void BtnBrowseRoot_Click(object sender, RoutedEventArgs e) { using var dlg = new WinForms.FolderBrowserDialog { Description = "選擇 Root 目錄", UseDescriptionForTitle = true, ShowNewFolderButton = true, SelectedPath = GetTextByNames("TbRootDir") }; if (dlg.ShowDialog() == WinForms.DialogResult.OK) SetTextByNames(dlg.SelectedPath, "TbRootDir"); }
        private void BtnBrowseHot_Click(object sender, RoutedEventArgs e) { using var dlg = new WinForms.FolderBrowserDialog { Description = "選擇收件夾 (HotFolder)", UseDescriptionForTitle = true, ShowNewFolderButton = true, SelectedPath = GetTextByNames("TbHotFolder") }; if (dlg.ShowDialog() == WinForms.DialogResult.OK) SetTextByNames(dlg.SelectedPath, "TbHotFolder"); }
        private void BtnBrowseDb_Click(object sender, RoutedEventArgs e) { var dlg = new SaveFileDialog { Title = "選擇或建立 SQLite 資料庫檔案", Filter = "SQLite Database (*.db;*.sqlite)|*.db;*.sqlite|All Files (*.*)|*.*", AddExtension = true, DefaultExt = ".db", FileName = GetTextByNames("TbDbPath") }; if (string.IsNullOrWhiteSpace(dlg.FileName)) dlg.FileName = "ai_kb.db"; if (dlg.ShowDialog(this) == true) SetTextByNames(dlg.FileName, "TbDbPath"); }
        private void BtnAddRootPath_Click(object sender, RoutedEventArgs e) { using var dlg = new WinForms.FolderBrowserDialog { Description = "選擇要加入檔案樹的根目錄", UseDescriptionForTitle = true, ShowNewFolderButton = true }; if (dlg.ShowDialog() == WinForms.DialogResult.OK) { if (FindName("TbTreeViewRoots") is TextBox tb) { var currentPaths = SplitPaths(tb.Text); var newPath = dlg.SelectedPath; if (!currentPaths.Contains(newPath, StringComparer.OrdinalIgnoreCase)) { currentPaths.Add(newPath); tb.Text = string.Join(Environment.NewLine, currentPaths); } } } }

        private void BtnOrderUp_Click(object sender, RoutedEventArgs e) { if (FindName("LbFolderOrder") is not ListBox lb) return; if (lb.SelectedIndex <= 0) return; var i = lb.SelectedIndex; var list = lb.Items.Cast<object>().Select(x => x.ToString() ?? "").ToList(); (list[i - 1], list[i]) = (list[i], list[i - 1]); lb.ItemsSource = list; lb.SelectedIndex = i - 1; }
        private void BtnOrderDown_Click(object sender, RoutedEventArgs e) { if (FindName("LbFolderOrder") is not ListBox lb) return; if (lb.SelectedIndex < 0 || lb.SelectedIndex >= lb.Items.Count - 1) return; var i = lb.SelectedIndex; var list = lb.Items.Cast<object>().Select(x => x.ToString() ?? "").ToList(); (list[i + 1], list[i]) = (list[i], list[i + 1]); lb.ItemsSource = list; lb.SelectedIndex = i + 1; }

        // ========================= Helper: UI getters/setters =========================
        private string GetTextByNames(params string[] names) { foreach (var n in names ?? Array.Empty<string>()) if (FindName(n) is TextBox tb) return tb.Text ?? string.Empty; return string.Empty; }
        private void SetTextByNames(string? value, params string[] names) { var v = value ?? string.Empty; foreach (var n in names ?? Array.Empty<string>()) if (FindName(n) is TextBox tb) tb.Text = v; }
        private string GetPasswordByNames(params string[] names) { foreach (var n in names ?? Array.Empty<string>()) if (FindName(n) is PasswordBox pb) return pb.Password ?? string.Empty; return string.Empty; }
        private void SetPasswordByNames(string? value, params string[] names) { var v = value ?? string.Empty; foreach (var n in names ?? Array.Empty<string>()) if (FindName(n) is PasswordBox pb) pb.Password = v; }
        private bool GetBoolByNames(params string[] names) { foreach (var n in names ?? Array.Empty<string>()) { if (FindName(n) is CheckBox cb) return cb.IsChecked == true; if (FindName(n) is RadioButton rb) return rb.IsChecked == true; } return false; }
        private void SetBool(string name, bool value) { if (FindName(name) is CheckBox cb) cb.IsChecked = value; }
        private string GetComboRaw(string name, string fallback) { if (FindName(name) is ComboBox cb) { if (cb.SelectedValue is string sv && !string.IsNullOrWhiteSpace(sv)) return sv.Trim(); if (cb.SelectedItem is ComboBoxItem cbi) { if (cbi.Tag is string tag && !string.IsNullOrWhiteSpace(tag)) return tag.Trim(); if (cbi.Content is string content && !string.IsNullOrWhiteSpace(content)) return ExtractRawToken(content); } var txt = (cb.Text ?? "").Trim(); if (!string.IsNullOrEmpty(txt)) return ExtractRawToken(txt); } return fallback; }
        private void SetComboByRaw(string name, string? raw, string fallback) { if (FindName(name) is ComboBox cb) { var token = string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim(); cb.SelectedValue = token; if (cb.SelectedItem == null) { foreach (var it in cb.Items) { if (it is ComboBoxItem cbi) { var tag = (cbi.Tag as string)?.Trim(); var txt = (cbi.Content as string)?.Trim(); if (string.Equals(tag, token, StringComparison.OrdinalIgnoreCase) || string.Equals(ExtractRawToken(txt ?? ""), token, StringComparison.OrdinalIgnoreCase)) { cb.SelectedItem = cbi; break; } } } } if (cb.SelectedItem == null && cb.Items.Count > 0) cb.SelectedIndex = 0; } }
        private static string ExtractRawToken(string s) { if (string.IsNullOrWhiteSpace(s)) return string.Empty; var idx = s.IndexOf('/'); if (idx > 0) return s[..idx].Trim(); idx = s.IndexOf(' '); if (idx > 0) return s[..idx].Trim(); return s.Trim(); }
        private static string ToDisplay(string? token) { if (string.IsNullOrWhiteSpace(token)) return string.Empty; token = token.Trim().ToLowerInvariant(); return TokenToDisplay.TryGetValue(token, out var disp) ? disp ?? token : token; }
        private static string FromDisplay(string display) { if (string.IsNullOrWhiteSpace(display)) return string.Empty; var s = display.Trim(); var slash = s.IndexOf('/'); if (slash > 0) s = s[..slash].Trim(); s = s.Split(' ')[0].Trim(); return s.ToLowerInvariant(); }
        private static List<string> SplitList(string? input) { if (string.IsNullOrWhiteSpace(input)) return new List<string>(); return input.Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().TrimStart('.')).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
        private static List<string> SplitPaths(string? input) { if (string.IsNullOrWhiteSpace(input)) return new List<string>(); return input.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
        private static string JoinList(IEnumerable<string>? list) { if (list == null) return string.Empty; return string.Join(", ", list.Where(x => !string.IsNullOrWhiteSpace(x))); }
    }
}