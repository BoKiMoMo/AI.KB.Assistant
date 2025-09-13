using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
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
            _cfg = current;

            // 綁定現值
            TxtRootDir.Text = _cfg.App.RootDir;
            TxtInboxDir.Text = _cfg.App.InboxDir;
            TxtDbPath.Text = _cfg.App.DbPath;

            PwdApiKey.Password = _cfg.OpenAI.ApiKey ?? "";
            TxtModel.Text = _cfg.OpenAI.Model ?? "gpt-4o-mini";

            ChkUseLLM.IsChecked = _cfg.Classification.UseLLM;
            TxtThreshold.Text = _cfg.Classification.ConfidenceThreshold.ToString("0.00");
            TxtMaxTags.Text = _cfg.Classification.MaxTags.ToString();
            ChkChatSearch.IsChecked = _cfg.Classification.EnableChatSearch;
        }

        private void BrowseRoot_Click(object sender, RoutedEventArgs e) => TxtRootDir.Text = PickFolderLikeFileDialog(TxtRootDir.Text);
        private void BrowseInbox_Click(object sender, RoutedEventArgs e) => TxtInboxDir.Text = PickFolderLikeFileDialog(TxtInboxDir.Text);

        private string PickFolderLikeFileDialog(string? initial)
        {
            var dlg = new SaveFileDialog
            {
                Title = "選擇資料夾（任意輸入檔名後按儲存即可）",
                FileName = "這裡代表資料夾",
                InitialDirectory = Directory.Exists(initial ?? "") ? initial : null,
                Filter = "All (*.*)|*.*"
            };
            return (dlg.ShowDialog() == true) ? Path.GetDirectoryName(dlg.FileName)! : (initial ?? "");
        }

        private void BrowseDb_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "選擇 / 建立 SQLite 檔案",
                Filter = "SQLite (*.db)|*.db|All (*.*)|*.*",
                FileName = string.IsNullOrWhiteSpace(TxtDbPath.Text) ? "kb.db" : Path.GetFileName(TxtDbPath.Text)
            };
            if (dlg.ShowDialog() == true) TxtDbPath.Text = dlg.FileName;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _cfg.App.RootDir = (TxtRootDir.Text ?? "").Trim();
                _cfg.App.InboxDir = (TxtInboxDir.Text ?? "").Trim();
                _cfg.App.DbPath = (TxtDbPath.Text ?? "").Trim();

                _cfg.OpenAI.ApiKey = PwdApiKey.Password ?? "";
                _cfg.OpenAI.Model = (TxtModel.Text ?? "gpt-4o-mini").Trim();

                _cfg.Classification.UseLLM = ChkUseLLM.IsChecked == true;
                if (double.TryParse(TxtThreshold.Text, out var th))
                    _cfg.Classification.ConfidenceThreshold = Math.Clamp(th, 0, 1);
                if (int.TryParse(TxtMaxTags.Text, out var mt))
                    _cfg.Classification.MaxTags = Math.Max(1, Math.Min(10, mt));
                _cfg.Classification.EnableChatSearch = ChkChatSearch.IsChecked == true;

                ConfigService.Save(_configPath, _cfg);
                MessageBox.Show("設定已儲存。", "完成");
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("儲存失敗：" + ex.Message, "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
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
            _cfg = current;

            // 綁定現值
            TxtRootDir.Text = _cfg.App.RootDir;
            TxtInboxDir.Text = _cfg.App.InboxDir;
            TxtDbPath.Text = _cfg.App.DbPath;

            PwdApiKey.Password = _cfg.OpenAI.ApiKey ?? "";
            TxtModel.Text = _cfg.OpenAI.Model ?? "gpt-4o-mini";

            ChkUseLLM.IsChecked = _cfg.Classification.UseLLM;
            TxtThreshold.Text = _cfg.Classification.ConfidenceThreshold.ToString("0.00");
            TxtMaxTags.Text = _cfg.Classification.MaxTags.ToString();
            ChkChatSearch.IsChecked = _cfg.Classification.EnableChatSearch;
        }

        private void BrowseRoot_Click(object sender, RoutedEventArgs e) => TxtRootDir.Text = PickFolderLikeFileDialog(TxtRootDir.Text);
        private void BrowseInbox_Click(object sender, RoutedEventArgs e) => TxtInboxDir.Text = PickFolderLikeFileDialog(TxtInboxDir.Text);

        private string PickFolderLikeFileDialog(string? initial)
        {
            var dlg = new SaveFileDialog
            {
                Title = "選擇資料夾（任意輸入檔名後按儲存即可）",
                FileName = "這裡代表資料夾",
                InitialDirectory = Directory.Exists(initial ?? "") ? initial : null,
                Filter = "All (*.*)|*.*"
            };
            return (dlg.ShowDialog() == true) ? Path.GetDirectoryName(dlg.FileName)! : (initial ?? "");
        }

        private void BrowseDb_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "選擇 / 建立 SQLite 檔案",
                Filter = "SQLite (*.db)|*.db|All (*.*)|*.*",
                FileName = string.IsNullOrWhiteSpace(TxtDbPath.Text) ? "kb.db" : Path.GetFileName(TxtDbPath.Text)
            };
            if (dlg.ShowDialog() == true) TxtDbPath.Text = dlg.FileName;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _cfg.App.RootDir = (TxtRootDir.Text ?? "").Trim();
                _cfg.App.InboxDir = (TxtInboxDir.Text ?? "").Trim();
                _cfg.App.DbPath = (TxtDbPath.Text ?? "").Trim();

                _cfg.OpenAI.ApiKey = PwdApiKey.Password ?? "";
                _cfg.OpenAI.Model = (TxtModel.Text ?? "gpt-4o-mini").Trim();

                _cfg.Classification.UseLLM = ChkUseLLM.IsChecked == true;
                if (double.TryParse(TxtThreshold.Text, out var th))
                    _cfg.Classification.ConfidenceThreshold = Math.Clamp(th, 0, 1);
                if (int.TryParse(TxtMaxTags.Text, out var mt))
                    _cfg.Classification.MaxTags = Math.Max(1, Math.Min(10, mt));
                _cfg.Classification.EnableChatSearch = ChkChatSearch.IsChecked == true;

                ConfigService.Save(_configPath, _cfg);
                MessageBox.Show("設定已儲存。", "完成");
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("儲存失敗：" + ex.Message, "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
