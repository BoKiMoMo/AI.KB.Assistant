using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using AI.KB.Assistant.Models;

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
            LoadConfig();
        }

        private void LoadConfig()
        {
            // 🧭 App
            TxtDbPath.Text = _cfg.App.DbPath ?? "";
            TxtRootDir.Text = _cfg.App.RootDir ?? "";

            // 📥 Import
            CbAutoOnDrop.IsChecked = _cfg.Import.AutoOnDrop;
            CbIncludeSubdir.IsChecked = _cfg.Import.IncludeSubdirectories;
            TxtHotFolder.Text = _cfg.Import.HotFolderPath ?? "";

            // 移動模式、覆蓋策略
            CbMoveMode.SelectedIndex = (int)_cfg.Import.MoveMode;
            CbOverwritePolicy.SelectedIndex = (int)_cfg.Import.OverwritePolicy;

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

            // 🧩 Extension Groups JSON 預覽
            if (_cfg.Routing.ExtensionGroups != null)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(
                    _cfg.Routing.ExtensionGroups,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
                );
                if (FindName("TxtExtGroups") is System.Windows.Controls.TextBox tb)
                    tb.Text = json;
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

            // 🧩 Extension Groups JSON 匯入
            if (FindName("TxtExtGroups") is System.Windows.Controls.TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
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
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Extension Groups JSON 解析失敗：{ex.Message}", "格式錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // 💾 儲存設定
            AppConfig.Save(_configPath, _cfg);
            _owner?.ReloadConfig();
            DialogResult = true;
            Close();
        }

        // 🔧 以下三個事件：修復 XAML Click 綁定缺失
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
    }
}
