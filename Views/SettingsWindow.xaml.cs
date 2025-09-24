using System.Windows;
using Ookii.Dialogs.Wpf;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly string _configPath;
        private readonly AppConfig _cfg;

        public SettingsWindow(string configPath, AppConfig cfg)
        {
            InitializeComponent();
            _configPath = configPath;
            _cfg = cfg;

            TxtRootDir.Text = cfg.RootDir;
            TxtDbPath.Text = cfg.DbPath;
            ChkDryRun.IsChecked = cfg.DryRun;
            CmbMoveMode.Text = cfg.MoveMode;
            CmbOverwrite.Text = cfg.OverwritePolicy;
        }

        private void BtnBrowseRoot_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new VistaFolderBrowserDialog();
            if (dlg.ShowDialog(this) == true)
            {
                TxtRootDir.Text = dlg.SelectedPath;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            _cfg.RootDir = TxtRootDir.Text.Trim();
            _cfg.DbPath = TxtDbPath.Text.Trim();
            _cfg.DryRun = ChkDryRun.IsChecked == true;
            _cfg.MoveMode = CmbMoveMode.Text;
            _cfg.OverwritePolicy = CmbOverwrite.Text;

            ConfigService.Save(_configPath, _cfg);
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;
    }
}
