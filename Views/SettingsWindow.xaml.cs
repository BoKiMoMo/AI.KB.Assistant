using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AI.KB.Assistant.Services;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            RenderFromConfig();
        }

        // ================== 儲存（把 UI 寫進 ConfigService.Cfg，然後 Save() 廣播） ==================
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfg = ConfigService.Cfg;

                // ---- 路徑 ----
                cfg.App.RootDir = ReadText("RootDir", "txtRootDir", "TbRootDir", "TextRootDir") ?? cfg.App.RootDir ?? "";
                var hot = ReadText("HotFolder", "txtHotFolder", "TbHotFolder", "TextHotFolder");
                if (!string.IsNullOrWhiteSpace(hot)) cfg.Import.HotFolderPath = hot!;
                var db = ReadText("DbPath", "txtDbPath", "TbDbPath", "TextDbPath");
                if (!string.IsNullOrWhiteSpace(db)) { cfg.Db.Path = db!; cfg.Db.DbPath = cfg.Db.Path; }

                // ---- Import（匯入策略）----
                cfg.Import.IncludeSubdir = ReadBool(cfg.Import.IncludeSubdir, "IncludeSubdir", "ChkIncludeSubdir", "IncludeSubdirectories");
                cfg.Import.EnableHotFolder = ReadBool(cfg.Import.EnableHotFolder, "EnableHotFolder", "ChkEnableHotFolder", "HotFolderEnable");
                cfg.Import.MoveMode = ReadComboOrText(cfg.Import.MoveMode, "MoveMode", "CmbMoveMode", "CbMoveMode"); // "copy" | "move"
                var opText = ReadComboOrText(cfg.Import.OverwritePolicy.ToString(), "OverwritePolicy", "CmbOverwrite", "CbOverwritePolicy");
                try { cfg.Import.OverwritePolicy = (OverwritePolicy)Enum.Parse(typeof(OverwritePolicy), opText, true); } catch { /* ignore */ }

                // ---- Routing（路徑邏輯）----
                cfg.Routing.UseProject = ReadBool(cfg.Routing.UseProject, "UseProject", "ChkUseProject");
                cfg.Routing.UseYear = ReadBool(cfg.Routing.UseYear, "UseYear", "ChkUseYear");
                cfg.Routing.UseMonth = ReadBool(cfg.Routing.UseMonth, "UseMonth", "ChkUseMonth");
                cfg.Routing.UseType = ReadComboOrText(cfg.Routing.UseType, "UseType", "CmbUseType", "CbUseType"); // "rule"|"llm"|"rule+llm"
                cfg.Routing.AutoFolderName = ReadText("AutoFolderName", "txtAutoFolder", "TbAutoFolder") ?? cfg.Routing.AutoFolderName ?? "_auto";
                cfg.Routing.LowConfidenceFolderName = ReadText("LowConfidenceFolderName", "txtLowConfFolder", "TbLowConfFolder") ?? cfg.Routing.LowConfidenceFolderName ?? "_low_conf";
                cfg.Routing.Threshold = ReadDouble(cfg.Routing.Threshold, "Threshold", "TxtThreshold", "TbThreshold");

                // 黑名單（逗號、分號、換行皆可）
                var blExt = ReadText("BlacklistExts", "txtBlacklistExts", "TbBlacklistExts", "TxtBlacklistExt");
                if (blExt != null)
                    cfg.Routing.BlacklistExts = blExt.Split(new[] { ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                                                     .Select(s => s.Trim().TrimStart('.').ToLowerInvariant())
                                                     .Distinct().ToList();

                var blFolder = ReadText("BlacklistFolders", "txtBlacklistFolders", "TbBlacklistFolders", "TxtBlacklistFolderNames");
                if (blFolder != null)
                    cfg.Routing.BlacklistFolderNames = blFolder.Split(new[] { ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                                                               .Select(s => s.Trim())
                                                               .Distinct().ToList();

                // ---- OpenAI（AI 參數）----
                var apiKey = ReadText("OpenAIApiKey", "txtOpenAIApiKey", "TbOpenAIApiKey", "ApiKey", "OpenAI_ApiKey");
                if (!string.IsNullOrWhiteSpace(apiKey)) cfg.OpenAI.ApiKey = apiKey!;
                var model = ReadText("OpenAIModel", "txtOpenAIModel", "TbOpenAIModel", "Model", "OpenAI_Model");
                if (!string.IsNullOrWhiteSpace(model)) cfg.OpenAI.Model = model!;

                // 寫回磁碟並廣播
                ConfigService.Save();

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"儲存設定時發生錯誤：{ex.Message}", "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ================== 重新載入（讀檔→套 UI，內含廣播） ==================
        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ConfigService.Load(); // Save/Load 內部皆會觸發 ConfigChanged
                RenderFromConfig();
                MessageBox.Show("設定已重新載入。", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重新載入設定失敗：{ex.Message}");
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        // ================== 瀏覽：RootDir ==================
        private void BtnBrowseRoot_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SetText("RootDir", "txtRootDir", dlg.SelectedPath);
                ConfigService.Cfg.App.RootDir = dlg.SelectedPath;
            }
        }

        // ================== 瀏覽：HotFolder ==================
        private void BtnBrowseHot_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SetText("HotFolder", "txtHotFolder", dlg.SelectedPath);
                ConfigService.Cfg.Import.HotFolderPath = dlg.SelectedPath;
            }
        }

        // ================== 瀏覽：DbPath ==================
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
                SetText("DbPath", "txtDbPath", dlg.FileName);
                ConfigService.Cfg.Db.Path = dlg.FileName;
                ConfigService.Cfg.Db.DbPath = ConfigService.Cfg.Db.Path;
            }
        }

        // ================== 套 UI（把 Cfg 值反映到畫面） ==================
        private void RenderFromConfig()
        {
            var cfg = ConfigService.Cfg;

            // 路徑
            SetText("RootDir", "txtRootDir", cfg.App.RootDir ?? "");
            SetText("HotFolder", "txtHotFolder", cfg.Import.HotFolderPath ?? "");
            SetText("DbPath", "txtDbPath", cfg.Db.Path ?? cfg.Db.DbPath ?? "");

            // Routing
            SetBool("UseProject", "ChkUseProject", cfg.Routing.UseProject);
            SetBool("UseYear", "ChkUseYear", cfg.Routing.UseYear);
            SetBool("UseMonth", "ChkUseMonth", cfg.Routing.UseMonth);
            SetComboOrText("UseType", "CmbUseType", "CbUseType", cfg.Routing.UseType ?? "rule+llm");
            SetText("AutoFolderName", "txtAutoFolder", "TbAutoFolder", cfg.Routing.AutoFolderName ?? "_auto");
            SetText("LowConfidenceFolderName", "txtLowConfFolder", "TbLowConfFolder", cfg.Routing.LowConfidenceFolderName ?? "_low_conf");
            SetText("Threshold", "TxtThreshold", "TbThreshold", cfg.Routing.Threshold.ToString());

            if (cfg.Routing.BlacklistExts != null && cfg.Routing.BlacklistExts.Any())
                SetText("BlacklistExts", "txtBlacklistExts", "TbBlacklistExts", "TxtBlacklistExt", string.Join(", ", cfg.Routing.BlacklistExts));
            if (cfg.Routing.BlacklistFolderNames != null && cfg.Routing.BlacklistFolderNames.Any())
                SetText("BlacklistFolders", "txtBlacklistFolders", "TbBlacklistFolders", "TxtBlacklistFolderNames", string.Join(", ", cfg.Routing.BlacklistFolderNames));

            // Import
            SetBool("IncludeSubdir", "ChkIncludeSubdir", "IncludeSubdirectories", cfg.Import.IncludeSubdir);
            SetBool("EnableHotFolder", "ChkEnableHotFolder", "HotFolderEnable", cfg.Import.EnableHotFolder);
            SetComboOrText("MoveMode", "CmbMoveMode", "CbMoveMode", cfg.Import.MoveMode ?? "copy");
            SetComboOrText("OverwritePolicy", "CmbOverwrite", "CbOverwritePolicy", cfg.Import.OverwritePolicy.ToString());

            // OpenAI
            SetText("OpenAIApiKey", "txtOpenAIApiKey", "TbOpenAIApiKey", "ApiKey", "OpenAI_ApiKey", cfg.OpenAI.ApiKey ?? "");
            SetText("OpenAIModel", "txtOpenAIModel", "TbOpenAIModel", "Model", "OpenAI_Model", cfg.OpenAI.Model ?? "gpt-4o-mini");
        }

        // ================== 讀取 Helper ==================
        private string? ReadText(params string[] names)
        {
            foreach (var n in names)
                if (FindName(n) is TextBox tb)
                    return tb.Text?.Trim();
            return null;
        }

        private bool ReadBool(bool fallback, params string[] names)
        {
            foreach (var n in names)
                if (FindName(n) is CheckBox cb)
                    return cb.IsChecked == true;
            return fallback;
        }

        private double ReadDouble(double fallback, params string[] names)
        {
            var s = ReadText(names);
            if (double.TryParse(s, out var v)) return v;
            return fallback;
        }

        private string ReadComboOrText(string fallback, params string[] names)
        {
            foreach (var n in names)
            {
                var el = FindName(n);
                if (el is ComboBox cb)
                {
                    var val = (cb.SelectedValue ?? cb.Text ?? cb.SelectedItem)?.ToString();
                    if (!string.IsNullOrWhiteSpace(val)) return val!;
                }
                if (el is TextBox tb)
                {
                    var val = tb.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(val)) return val!;
                }
            }
            return fallback ?? "";
        }

        // ================== 設值 Helper（新增 2 名稱版本，解 CS7036） ==================
        private void SetText(string name, string value)
        {
            if (FindName(name) is TextBox tb) tb.Text = value ?? string.Empty;
        }
        private void SetText(string n1, string n2, string value)
        {
            if (FindName(n1) is TextBox tb1) tb1.Text = value ?? string.Empty;
            else if (FindName(n2) is TextBox tb2) tb2.Text = value ?? string.Empty;
        }
        private void SetText(string n1, string n2, string n3, string value)
        {
            if (FindName(n1) is TextBox tb1) tb1.Text = value ?? string.Empty;
            else if (FindName(n2) is TextBox tb2) tb2.Text = value ?? string.Empty;
            else if (FindName(n3) is TextBox tb3) tb3.Text = value ?? string.Empty;
        }
        private void SetText(string n1, string n2, string n3, string n4, string n5, string value)
        {
            if (FindName(n1) is TextBox tb1) tb1.Text = value ?? string.Empty;
            else if (FindName(n2) is TextBox tb2) tb2.Text = value ?? string.Empty;
            else if (FindName(n3) is TextBox tb3) tb3.Text = value ?? string.Empty;
            else if (FindName(n4) is TextBox tb4) tb4.Text = value ?? string.Empty;
            else if (FindName(n5) is TextBox tb5) tb5.Text = value ?? string.Empty;
        }

        private void SetBool(string name, bool value)
        {
            if (FindName(name) is CheckBox cb) cb.IsChecked = value;
        }
        private void SetBool(string n1, string n2, bool value)
        {
            if (FindName(n1) is CheckBox cb1) cb1.IsChecked = value;
            else if (FindName(n2) is CheckBox cb2) cb2.IsChecked = value;
        }
        private void SetBool(string n1, string n2, string n3, bool value)
        {
            if (FindName(n1) is CheckBox cb1) cb1.IsChecked = value;
            else if (FindName(n2) is CheckBox cb2) cb2.IsChecked = value;
            else if (FindName(n3) is CheckBox cb3) cb3.IsChecked = value;
        }

        private void SetComboOrText(string n1, string n2, string n3, string value)
        {
            var el = FindName(n1);
            if (el is ComboBox c1) { c1.Text = value; c1.SelectedValue = value; return; }
            el = FindName(n2);
            if (el is ComboBox c2) { c2.Text = value; c2.SelectedValue = value; return; }
            el = FindName(n3);
            if (el is ComboBox c3) { c3.Text = value; c3.SelectedValue = value; return; }
            // fallback to TextBox
            if (FindName(n1) is TextBox tb1) tb1.Text = value;
            else if (FindName(n2) is TextBox tb2) tb2.Text = value;
            else if (FindName(n3) is TextBox tb3) tb3.Text = value;
        }
    }
}
