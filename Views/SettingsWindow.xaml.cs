using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views
{
    public partial class SettingsWindow : Window
    {
        // === 新增：層級英↔中對照與合法 token 集合 ===
        private static readonly Dictionary<string, string> TokenToDisplay = new(StringComparer.OrdinalIgnoreCase)
        {
            { "year",     "year / 年份" },
            { "month",    "month / 月份" },
            { "project",  "project / 專案" },
            { "category", "category / 類別" },
        };
        private static readonly HashSet<string> ValidTokens =
            new(new[] { "year", "month", "project", "category" }, StringComparer.OrdinalIgnoreCase);

        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += SettingsWindow_Loaded;
        }

        private void SettingsWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (ConfigService.Cfg == null)
                    ConfigService.Load();

                RenderFromConfig();
                EnsureTopPathsNotBlank();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"載入設定時發生例外：{ex.Message}", "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ========================= Config -> UI =========================
        private void RenderFromConfig()
        {
            var cfg = ConfigService.Cfg ?? new AppConfig();

            // 路徑
            SetTextByNames(cfg.App?.RootDir, "TbRootDir");
            SetTextByNames(cfg.Import?.HotFolder, "TbHotFolder");
            SetTextByNames(cfg.Db?.DbPath, "TbDbPath");

            // V7.34 UI 串接：載入啟動模式
            string mode = cfg.App?.LaunchMode ?? "Detailed";
            if (FindName("RadioModeSimple") is RadioButton r_simp && FindName("RadioModeDetailed") is RadioButton r_det)
            {
                if (mode == "Simple")
                    r_simp.IsChecked = true;
                else
                    r_det.IsChecked = true;
            }

            // 匯入/監控
            SetBool("CbIncludeSubdir", cfg.Import?.IncludeSubdir == true);
            SetBool("CbEnableHotFolder", cfg.Import?.EnableHotFolder == true);

            SetComboByRaw("CbMoveMode", cfg.Import?.MoveMode, "copy");                       // copy/move/link
            SetComboByRaw("CbOverwrite", cfg.Import?.OverwritePolicy.ToString(), "KeepBoth"); // KeepBoth/Rename/Replace

            // 黑名單
            SetTextByNames(JoinList(cfg.Routing?.BlacklistExts), "TbBlacklistExts");
            SetTextByNames(JoinList(cfg.Routing?.BlacklistFolderNames), "TbBlacklistFolders");

            // 路徑規則
            SetBool("CbUseYear", cfg.Routing?.UseYear == true);
            SetBool("CbUseMonth", cfg.Routing?.UseMonth == true);
            SetBool("CbUseProject", cfg.Routing?.UseProject == true);
            SetBool("CbUseCategory", cfg.Routing?.UseCategory == true); // 保留

            SetComboByRaw("CbUseType", cfg.Routing?.UseType, "rule+llm");

            // 層級順序（若為空用預設） → 轉成「英+中」顯示
            var order = (cfg.Routing?.FolderOrder == null || cfg.Routing.FolderOrder.Count == 0)
                ? RoutingService.DefaultOrder(cfg.Routing?.UseCategory == true)
                : cfg.Routing!.FolderOrder!.ToList();

            // 新增：確保 4 個 token 都存在並以「英+中」顯示
            foreach (var t in ValidTokens)
                if (!order.Contains(t, StringComparer.OrdinalIgnoreCase)) order.Add(t);

            if (FindName("LbFolderOrder") is ListBox lb)
            {
                lb.ItemsSource = order.Select(ToDisplay).ToList();  // 顯示英+中
                if (lb.Items.Count > 0) lb.SelectedIndex = 0;
            }

            // 信心不足資料夾 & 閾值
            SetTextByNames(cfg.Routing?.LowConfidenceFolderName, "TbLowConfidence");

            var th = cfg.Routing?.Threshold ?? 0.0;
            if (FindName("SlThreshold") is Slider sld) sld.Value = Math.Clamp(th, 0, 1);
            SetTextByNames((FindName("SlThreshold") is Slider s ? s.Value : th).ToString("F2"), "TbThreshold");

            // AI 模組
            SetPasswordByNames(cfg.OpenAI?.ApiKey, "TbApiKey");
            SetTextByNames(cfg.OpenAI?.Model, "TbModel");
        }

        /// <summary>UI 三大路徑欄位若為空，補上目前設定值。</summary>
        private void EnsureTopPathsNotBlank()
        {
            var cfg = ConfigService.Cfg ?? new AppConfig();
            if (string.IsNullOrWhiteSpace(GetTextByNames("TbRootDir")))
                SetTextByNames(cfg.App?.RootDir, "TbRootDir");
            if (string.IsNullOrWhiteSpace(GetTextByNames("TbHotFolder")))
                SetTextByNames(cfg.Import?.HotFolder, "TbHotFolder");
            if (string.IsNullOrWhiteSpace(GetTextByNames("TbDbPath")))
                SetTextByNames(cfg.Db?.DbPath, "TbDbPath");
        }

        // ========================= UI -> Config =========================
        private void HarvestToConfig(AppConfig cfg)
        {
            if (cfg == null) return;

            // 以頂層型別初始化
            cfg.App ??= new AppSection();
            cfg.Import ??= new ImportSection();
            cfg.Db ??= new DbSection();
            cfg.Routing ??= new RoutingSection();
            cfg.OpenAI ??= new OpenAISection();

            // 基本路徑
            cfg.App.RootDir = GetTextByNames("TbRootDir");
            cfg.Import.HotFolder = GetTextByNames("TbHotFolder");
            cfg.Db.DbPath = GetTextByNames("TbDbPath");

            // V7.34 UI 串接：儲存啟動模式
            if (GetBoolByNames("RadioModeSimple")) // (Helper 函式 GetBoolByNames 也適用於 RadioButton)
                cfg.App.LaunchMode = "Simple";
            else
                cfg.App.LaunchMode = "Detailed";

            // 匯入設定
            cfg.Import.IncludeSubdir = GetBoolByNames("CbIncludeSubdir");
            cfg.Import.EnableHotFolder = GetBoolByNames("CbEnableHotFolder");
            cfg.Import.MoveMode = GetComboRaw("CbMoveMode", "copy");

            // 覆寫策略（容錯對應 enum 名稱）
            var overwrite = GetComboRaw("CbOverwrite", "KeepBoth").Trim();
            if (!Enum.TryParse(overwrite, true, out OverwritePolicy policy))
            {
                var wantReplace = new[] { "Replace", "Overwrite" };
                var wantRename = new[] { "Rename", "AutoRename", "RenameNew" };
                var wantKeep = new[] { "KeepBoth", "Keep" };

                if (wantReplace.Contains(overwrite, StringComparer.OrdinalIgnoreCase))
                    policy = ResolveEnumByPreferredNames<OverwritePolicy>(wantReplace, DefaultEnumValue<OverwritePolicy>(wantKeep));
                else if (wantRename.Contains(overwrite, StringComparer.OrdinalIgnoreCase))
                    policy = ResolveEnumByPreferredNames<OverwritePolicy>(wantRename, DefaultEnumValue<OverwritePolicy>(wantKeep));
                else
                    policy = ResolveEnumByPreferredNames<OverwritePolicy>(wantKeep, DefaultEnumValue<OverwritePolicy>(wantKeep));
            }
            cfg.Import.OverwritePolicy = policy;

            // 黑名單
            cfg.Routing.BlacklistExts = SplitList(GetTextByNames("TbBlacklistExts"));
            cfg.Routing.BlacklistFolderNames = SplitList(GetTextByNames("TbBlacklistFolders"));

            // 路徑規則
            cfg.Routing.UseYear = GetBoolByNames("CbUseYear");
            cfg.Routing.UseMonth = GetBoolByNames("CbUseMonth");
            cfg.Routing.UseProject = GetBoolByNames("CbUseProject");
            cfg.Routing.UseCategory = GetBoolByNames("CbUseCategory"); // 保留
            cfg.Routing.UseType = GetComboRaw("CbUseType", "rule+llm");

            // FolderOrder：把「英+中」還原成英文 token，且僅收合法 token
            if (FindName("LbFolderOrder") is ListBox lb)
            {
                var list = lb.Items.Cast<object>()
                    .Select(x => FromDisplay(x?.ToString() ?? ""))     // NEW：還原 token
                    .Where(t => ValidTokens.Contains(t))               // NEW：僅收合法
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // 若 UI 沒選，仍確保四個 token 都存在（順序以目前為主）
                foreach (var t in ValidTokens)
                    if (!list.Contains(t, StringComparer.OrdinalIgnoreCase)) list.Add(t);

                cfg.Routing.FolderOrder = list;
            }

            // 信心不足資料夾 & 閾值
            cfg.Routing.LowConfidenceFolderName = GetTextByNames("TbLowConfidence");

            var thStr = GetTextByNames("TbThreshold");
            if (string.IsNullOrWhiteSpace(thStr) && FindName("SlThreshold") is Slider sld)
                thStr = sld.Value.ToString("F2");
            if (double.TryParse(thStr, out var th))
                cfg.Routing.Threshold = Math.Clamp(th, 0, 1);

            // AI 模組
            cfg.OpenAI.ApiKey = GetPasswordByNames("TbApiKey");
            cfg.OpenAI.Model = GetTextByNames("TbModel");
        }

        // ========================= Buttons =========================
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfg = ConfigService.Cfg ?? new AppConfig();
                HarvestToConfig(cfg);

                if (!ConfigService.Save())
                {
                    MessageBox.Show($"無法寫入設定檔：{ConfigService.ConfigPath}", "儲存失敗", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 成功後重新載入一次，確保畫面同步顯示
                ConfigService.Load();
                RenderFromConfig();
                EnsureTopPathsNotBlank();

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"儲存設定時發生例外：{ex.Message}", "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ConfigService.Load();
                RenderFromConfig();
                EnsureTopPathsNotBlank();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重新載入時發生例外：{ex.Message}", "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnBrowseRoot_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.FolderBrowserDialog
            {
                Description = "選擇 Root 目錄",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                SetTextByNames(dlg.SelectedPath, "TbRootDir");
        }

        private void BtnBrowseHot_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.FolderBrowserDialog
            {
                Description = "選擇收件夾 (HotFolder)",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                SetTextByNames(dlg.SelectedPath, "TbHotFolder");
        }



        private void BtnBrowseDb_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "選擇或建立 SQLite 資料庫檔案",
                Filter = "SQLite Database (*.db;*.sqlite)|*.db;*.sqlite|All Files (*.*)|*.*",
                AddExtension = true,
                DefaultExt = ".db",
                FileName = "ai_kb.db"
            };
            if (dlg.ShowDialog(this) == true)
                SetTextByNames(dlg.FileName, "TbDbPath");
        }

        // ======== 排序按鈕 ========
        private void BtnOrderUp_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("LbFolderOrder") is not ListBox lb) return;
            if (lb.SelectedIndex <= 0) return;
            var i = lb.SelectedIndex;
            var list = lb.Items.Cast<object>().Select(x => x.ToString() ?? "").ToList();
            (list[i - 1], list[i]) = (list[i], list[i - 1]);
            lb.ItemsSource = list;
            lb.SelectedIndex = i - 1;
        }

        private void BtnOrderDown_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("LbFolderOrder") is not ListBox lb) return;
            if (lb.SelectedIndex < 0 || lb.SelectedIndex >= lb.Items.Count - 1) return;
            var i = lb.SelectedIndex;
            var list = lb.Items.Cast<object>().Select(x => x.ToString() ?? "").ToList();
            (list[i + 1], list[i]) = (list[i], list[i + 1]);
            lb.ItemsSource = list;
            lb.SelectedIndex = i + 1;
        }

        // ========================= Helper: UI getters/setters =========================
        private string GetTextByNames(params string[] names)
        {
            foreach (var n in names ?? Array.Empty<string>())
                if (FindName(n) is TextBox tb)
                    return tb.Text ?? string.Empty;
            return string.Empty;
        }

        private void SetTextByNames(string? value, params string[] names)
        {
            var v = value ?? string.Empty;
            foreach (var n in names ?? Array.Empty<string>())
                if (FindName(n) is TextBox tb)
                    tb.Text = v;
        }

        private string GetPasswordByNames(params string[] names)
        {
            foreach (var n in names ?? Array.Empty<string>())
                if (FindName(n) is PasswordBox pb)
                    return pb.Password ?? string.Empty;
            return string.Empty;
        }

        private void SetPasswordByNames(string? value, params string[] names)
        {
            var v = value ?? string.Empty;
            foreach (var n in names ?? Array.Empty<string>())
                if (FindName(n) is PasswordBox pb)
                    pb.Password = v;
        }

        private bool GetBoolByNames(params string[] names)
        {
            foreach (var n in names ?? Array.Empty<string>())
            {
                if (FindName(n) is CheckBox cb)
                    return cb.IsChecked == true;
                // V7.34 UI 串接：擴充 helper
                if (FindName(n) is RadioButton rb)
                    return rb.IsChecked == true;
            }
            return false;
        }

        private void SetBool(string name, bool value)
        {
            if (FindName(name) is CheckBox cb)
                cb.IsChecked = value;
        }

        /// <summary>讀取 ComboBox 的值：優先 SelectedValue（Tag），再看 SelectedItem 的 Tag/Content，最後退回 Text。</summary>
        private string GetComboRaw(string name, string fallback)
        {
            if (FindName(name) is ComboBox cb)
            {
                if (cb.SelectedValue is string sv && !string.IsNullOrWhiteSpace(sv)) return sv.Trim();
                if (cb.SelectedItem is ComboBoxItem cbi)
                {
                    if (cbi.Tag is string tag && !string.IsNullOrWhiteSpace(tag)) return tag.Trim();
                    if (cbi.Content is string content && !string.IsNullOrWhiteSpace(content)) return ExtractRawToken(content);
                }
                var txt = (cb.Text ?? "").Trim();
                if (!string.IsNullOrEmpty(txt)) return ExtractRawToken(txt);
            }
            return fallback;
        }

        /// <summary>設定 ComboBox 的選中值：優先用 SelectedValue（Tag），再對比 Tag/Content；最後選第一個避免空白。</summary>
        private void SetComboByRaw(string name, string? raw, string fallback)
        {
            if (FindName(name) is ComboBox cb)
            {
                var token = string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
                cb.SelectedValue = token;
                if (cb.SelectedItem == null)
                {
                    foreach (var it in cb.Items)
                    {
                        if (it is ComboBoxItem cbi)
                        {
                            var tag = (cbi.Tag as string)?.Trim();
                            var txt = (cbi.Content as string)?.Trim();
                            if (string.Equals(tag, token, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(ExtractRawToken(txt ?? ""), token, StringComparison.OrdinalIgnoreCase))
                            {
                                cb.SelectedItem = cbi;
                                break;
                            }
                        }
                    }
                }
                if (cb.SelectedItem == null && cb.Items.Count > 0)
                    cb.SelectedIndex = 0;
            }
        }

        /// <summary>從「顯示字串」取出機器可讀 token，例如 "move / 搬移檔案" -> "move"</summary>
        private static string ExtractRawToken(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var idx = s.IndexOf('/');
            if (idx > 0) return s[..idx].Trim();
            idx = s.IndexOf(' ');
            if (idx > 0) return s[..idx].Trim();
            return s.Trim();
        }

        // === 新增：顯示↔token 轉換 ===
        private static string ToDisplay(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return string.Empty;

            token = token.Trim().ToLowerInvariant();
            return TokenToDisplay.TryGetValue(token, out var disp)
                ? disp ?? token
                : token;
        }

        private static string FromDisplay(string display)
        {
            if (string.IsNullOrWhiteSpace(display)) return string.Empty;
            var s = display.Trim();
            var slash = s.IndexOf('/');
            if (slash > 0) s = s[..slash].Trim();
            s = s.Split(' ')[0].Trim();
            return s.ToLowerInvariant();
        }

        private static List<string> SplitList(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new List<string>();
            return input
                .Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().TrimStart('.'))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string JoinList(IEnumerable<string>? list)
        {
            if (list == null) return string.Empty;
            return string.Join(", ", list.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        // ========================= Enum mapping helpers =========================
        private static TEnum ResolveEnumByPreferredNames<TEnum>(IEnumerable<string> preferredNames, TEnum fallback)
            where TEnum : struct, Enum
        {
            var names = Enum.GetNames(typeof(TEnum));
            foreach (var p in preferredNames ?? Array.Empty<string>())
            {
                var hit = names.FirstOrDefault(n => string.Equals(n, p, StringComparison.OrdinalIgnoreCase));
                if (hit != null && Enum.TryParse(hit, true, out TEnum val))
                    return val;
            }
            return fallback;
        }

        private static TEnum DefaultEnumValue<TEnum>(IEnumerable<string>? prefer = null)
            where TEnum : struct, Enum
        {
            var values = Enum.GetValues(typeof(TEnum)).Cast<TEnum>().ToArray();
            if (prefer != null)
            {
                var names = Enum.GetNames(typeof(TEnum));
                foreach (var p in prefer)
                {
                    var idx = Array.FindIndex(names, n => string.Equals(n, p, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) return values[idx];
                }
            }
            return values.First();
        }
    }
}