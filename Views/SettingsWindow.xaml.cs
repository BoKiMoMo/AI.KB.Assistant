// Views/SettingsWindow.xaml.cs
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using System.IO;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly string _configPath;
        private AppConfig _cfg;

        public SettingsWindow(string configPath, AppConfig current)
        {
            InitializeComponent();
            _configPath = configPath;
            _cfg = current ?? new AppConfig();
            BindToUi();
        }

        private void BindToUi()
        {
            TxtRootDir.Text = _cfg.App.RootDir;
            TxtInboxDir.Text = _cfg.App.InboxDir;
            TxtDbPath.Text = _cfg.App.DbPath;

            ChkDryRun.IsChecked = _cfg.App.DryRun;
            CmbMoveMode.SelectedItem = CmbMoveMode.Items.Cast<FrameworkElement>().FirstOrDefault(i => (string)(i as dynamic).Content == _cfg.App.MoveMode);
            CmbOverwrite.SelectedItem = CmbOverwrite.Items.Cast<FrameworkElement>().FirstOrDefault(i => (string)(i as dynamic).Content == _cfg.App.OverwritePolicy);

            CmbClassMode.SelectedItem = CmbClassMode.Items.Cast<FrameworkElement>().FirstOrDefault(i => (string)(i as dynamic).Content == _cfg.Routing.ClassificationMode);
            CmbGranularity.SelectedItem = CmbGranularity.Items.Cast<FrameworkElement>().FirstOrDefault(i => (string)(i as dynamic).Content == _cfg.Routing.TimeGranularity);

            SldThreshold.Value = _cfg.Classification.ConfidenceThreshold;
            LblThreshold.Text = $"{SldThreshold.Value:0.00}";
            TxtAutoFolder.Text = _cfg.Classification.AutoFolderName;

            TxtModel.Text = _cfg.OpenAI.Model;
            PwdApiKey.Password = _cfg.OpenAI.ApiKey;

            TxtRuleSummary.Text =
                $"副檔名類別數：{_cfg.Classification.ExtensionMap.Count}\r\n" +
                $"關鍵字類別數：{_cfg.Classification.KeywordMap.Count}\r\n" +
                $"正則類別數：{_cfg.Classification.RegexMap.Count}\r\n" +
                $"（完整規則可直接編輯 config.json）";

            SldThreshold.ValueChanged += (_, __) => LblThreshold.Text = $"{SldThreshold.Value:0.00}";
        }

        private void Collect()
        {
            _cfg.App.RootDir = TxtRootDir.Text.Trim();
            _cfg.App.InboxDir = TxtInboxDir.Text.Trim();
            _cfg.App.DbPath = TxtDbPath.Text.Trim();
            _cfg.App.DryRun = ChkDryRun.IsChecked == true;
            _cfg.App.MoveMode = ((dynamic)CmbMoveMode.SelectedItem).Content.ToString();
            _cfg.App.OverwritePolicy = ((dynamic)CmbOverwrite.SelectedItem).Content.ToString();

            _cfg.Routing.ClassificationMode = ((dynamic)CmbClassMode.SelectedItem).Content.ToString();
            _cfg.Routing.TimeGranularity = ((dynamic)CmbGranularity.SelectedItem).Content.ToString();

            _cfg.Classification.ConfidenceThreshold = SldThreshold.Value;
            _cfg.Classification.AutoFolderName = TxtAutoFolder.Text.Trim();

            _cfg.OpenAI.Model = TxtModel.Text.Trim();
            _cfg.OpenAI.ApiKey = PwdApiKey.Password;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Collect();
                ConfigService.Save(_configPath, _cfg);
                MessageBox.Show("設定已儲存。", "設定", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"儲存失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // 無 WinForms 的資料夾挑選（用選檔取其目錄）
        private void PickFolderRoot_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Title = "選擇根目錄（任意輸入檔名後按儲存）", Filter = "All (*.*)|*.*" };
            if (dlg.ShowDialog() == true) TxtRootDir.Text = Path.GetDirectoryName(dlg.FileName)!;
        }
        private void PickFolderInbox_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Title = "選擇收件匣（任意輸入檔名後按儲存）", Filter = "All (*.*)|*.*" };
            if (dlg.ShowDialog() == true) TxtInboxDir.Text = Path.GetDirectoryName(dlg.FileName)!;
        }
        private void PickDb_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Title = "選擇/建立資料庫檔", Filter = "SQLite (*.db)|*.db|All (*.*)|*.*" };
            if (dlg.ShowDialog() == true) TxtDbPath.Text = dlg.FileName;
        }
    }
}
