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
            _cfg = current ?? new AppConfig();
            BindToUi();
        }

        private void BindToUi()
        {
            TxtRootDir.Text = _cfg.App?.RootDir ?? "";
            TxtInboxDir.Text = _cfg.App?.InboxDir ?? "";
            TxtDbPath.Text = _cfg.App?.DbPath ?? "";
            TxtProject.Text = _cfg.App?.ProjectName ?? "DefaultProject";

            // mode
            var mode = (_cfg.Classification?.ClassificationMode ?? "category").ToLowerInvariant();
            CmbMode.SelectedIndex = mode switch { "date" => 1, "project" => 2, _ => 0 };

            // granularity
            var gran = (_cfg.Classification?.TimeGranularity ?? "month").ToLowerInvariant();
            CmbGran.SelectedIndex = gran switch { "year" => 0, "day" => 2, _ => 1 };

            ChkDryRun.IsChecked = _cfg.App?.DryRun ?? true;
            ChkOverwrite.IsChecked = _cfg.App?.Overwrite ?? false;

            // move/copy
            var move = (_cfg.App?.MoveMode ?? "copy").ToLowerInvariant();
            CmbMoveMode.SelectedIndex = move == "move" ? 1 : 0;
        }

        private void CollectFromUi()
        {
            _cfg.App ??= new AppSection();
            _cfg.Classification ??= new ClassificationSection();

            _cfg.App.RootDir = TxtRootDir.Text.Trim();
            _cfg.App.InboxDir = TxtInboxDir.Text.Trim();
            _cfg.App.DbPath = TxtDbPath.Text.Trim();
            _cfg.App.ProjectName = TxtProject.Text.Trim();

            _cfg.App.DryRun = ChkDryRun.IsChecked == true;
            _cfg.App.Overwrite = ChkOverwrite.IsChecked == true;
            _cfg.App.MoveMode = (CmbMoveMode.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "copy";

            _cfg.Classification.ClassificationMode =
                (CmbMode.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "category";

            _cfg.Classification.TimeGranularity =
                (CmbGran.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "month";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            CollectFromUi();
            ConfigService.Save(_configPath, _cfg);
            MessageBox.Show("設定已儲存！", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // 不用 WinForms，這招可取資料夾路徑（在欲選資料夾輸入隨意檔名後按儲存）
        private void BrowseRoot_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Title = "選擇根目錄", FileName = "在此資料夾按儲存即可", Filter = "任何檔案|*.*" };
            if (dlg.ShowDialog() == true) TxtRootDir.Text = Path.GetDirectoryName(dlg.FileName) ?? "";
        }
        private void BrowseInbox_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Title = "選擇收件匣資料夾", FileName = "在此資料夾按儲存即可", Filter = "任何檔案|*.*" };
            if (dlg.ShowDialog() == true) TxtInboxDir.Text = Path.GetDirectoryName(dlg.FileName) ?? "";
        }
        private void BrowseDb_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Title = "選擇或建立 SQLite 檔", Filter = "SQLite (*.db)|*.db|所有檔案 (*.*)|*.*" };
            if (dlg.ShowDialog() == true) TxtDbPath.Text = dlg.FileName;
        }
    }
}
