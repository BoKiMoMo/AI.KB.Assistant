using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms; // 只用別名，避免與 WPF 控制項衝突
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly string _cfgPath;
        private AppConfig _cfg;

        public SettingsWindow()
        {
            InitializeComponent();
            _cfgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            _cfg = ConfigService.TryLoad(_cfgPath);
            Loaded += (_, __) => LoadToUI();
        }

        private void LoadToUI()
        {
            // DB / Root
            TxtDbPath.Text = _cfg.App.DbPath ?? "";
            TxtRoot.Text = _cfg.App.RootDir ?? "";

            // Hot
            CbEnableHot.IsChecked = _cfg.Import.EnableHotFolder;
            TxtHotPath.Text = _cfg.Import.HotFolderPath ?? "";
            CbIncludeSubdir.IsChecked = _cfg.Import.IncludeSubdirectories;
            CbAutoOnDrop.IsChecked = _cfg.Import.AutoClassifyOnDrop;
            SelectByTag(CmbMoveMode, _cfg.Import.MoveMode.ToString());          // enum -> string(Tag)
            SelectByTag(CmbOverwrite, _cfg.Import.OverwritePolicy.ToString());   // enum -> string(Tag)

            // Routing
            CbYear.IsChecked = _cfg.Routing.EnableYear;
            CbMonth.IsChecked = _cfg.Routing.EnableMonth;
            CbProject.IsChecked = _cfg.Routing.EnableProject;
            CbType.IsChecked = _cfg.Routing.EnableType;
            TxtAutoFolder.Text = _cfg.Routing.AutoFolderName ?? "自整理";

            // LLM
            CbLlmLowConf.IsChecked = _cfg.OpenAI.EnableWhenLowConfidence;
            TxtApiKey.Password = _cfg.OpenAI.ApiKey ?? "";
            TxtBaseUrl.Text = _cfg.OpenAI.BaseUrl ?? "";
            TxtModel.Text = _cfg.OpenAI.Model ?? "gpt-4o-mini";
        }

        private void SaveFromUI()
        {
            // DB / Root
            _cfg.App.DbPath = TxtDbPath.Text?.Trim() ?? _cfg.App.DbPath;
            _cfg.App.RootDir = TxtRoot.Text?.Trim() ?? _cfg.App.RootDir;

            // Hot（字串 Tag -> enum）
            _cfg.Import.EnableHotFolder = CbEnableHot.IsChecked == true;
            _cfg.Import.HotFolderPath = TxtHotPath.Text?.Trim() ?? "";
            _cfg.Import.IncludeSubdirectories = CbIncludeSubdir.IsChecked == true;
            _cfg.Import.AutoClassifyOnDrop = CbAutoOnDrop.IsChecked == true;
            _cfg.Import.MoveMode = SelectedEnumTag<MoveMode>(CmbMoveMode, MoveMode.Move);
            _cfg.Import.OverwritePolicy = SelectedEnumTag<OverwritePolicy>(CmbOverwrite, OverwritePolicy.Rename);

            // Routing
            _cfg.Routing.EnableYear = CbYear.IsChecked == true;
            _cfg.Routing.EnableMonth = CbMonth.IsChecked == true;
            _cfg.Routing.EnableProject = CbProject.IsChecked == true;
            _cfg.Routing.EnableType = CbType.IsChecked == true;
            _cfg.Routing.AutoFolderName = string.IsNullOrWhiteSpace(TxtAutoFolder.Text) ? "自整理" : TxtAutoFolder.Text.Trim();

            // LLM
            _cfg.OpenAI.EnableWhenLowConfidence = CbLlmLowConf.IsChecked == true;
            _cfg.OpenAI.ApiKey = TxtApiKey.Password?.Trim() ?? "";
            _cfg.OpenAI.BaseUrl = TxtBaseUrl.Text?.Trim() ?? "";
            _cfg.OpenAI.Model = string.IsNullOrWhiteSpace(TxtModel.Text) ? "gpt-4o-mini" : TxtModel.Text.Trim();
        }

        private static void SelectByTag(ComboBox box, string tag)
        {
            var it = box.Items.OfType<ComboBoxItem>()
                              .FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.OrdinalIgnoreCase));
            box.SelectedItem = it ?? box.Items.Cast<object>().FirstOrDefault();
        }

        private static T SelectedEnumTag<T>(ComboBox box, T fallback) where T : struct, Enum
        {
            if (box.SelectedItem is ComboBoxItem it && it.Tag is string s
                && Enum.TryParse<T>(s, ignoreCase: true, out var val))
                return val;
            return fallback;
        }

        private void BtnBrowseDb_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new Forms.OpenFileDialog
            {
                Title = "選擇資料庫檔案",
                Filter = "SQLite 資料庫 (*.db;*.sqlite)|*.db;*.sqlite|所有檔案 (*.*)|*.*",
                CheckFileExists = false
            };
            if (dlg.ShowDialog() == Forms.DialogResult.OK)
                TxtDbPath.Text = dlg.FileName;
        }

        private void BtnBrowseRoot_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new Forms.FolderBrowserDialog
            {
                Description = "選擇根目錄 (Root)",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            if (dlg.ShowDialog() == Forms.DialogResult.OK)
                TxtRoot.Text = dlg.SelectedPath;
        }

        private void BtnBrowseHot_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new Forms.FolderBrowserDialog
            {
                Description = "選擇收件夾 (Hot Folder)",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            if (dlg.ShowDialog() == Forms.DialogResult.OK)
                TxtHotPath.Text = dlg.SelectedPath;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveFromUI();
                ConfigService.Save(_cfgPath, _cfg);
                DialogResult = true; // 立即關閉
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "儲存失敗：" + ex.Message, "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
