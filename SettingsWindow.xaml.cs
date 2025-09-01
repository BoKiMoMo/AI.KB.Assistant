using System.Linq;
using System.Windows;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using WinForms = System.Windows.Forms; 

namespace AI.KB.Assistant.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly string _configPath;
        private AppConfig _cfg;

        public SettingsWindow(string configPath)
        {
            InitializeComponent();
            _configPath = configPath;
            _cfg = ConfigService.Load(configPath);
            BindToUi();
            SldThreshold.ValueChanged += (_, __) => LblThreshold.Text = $"{SldThreshold.Value:0.00}";
        }

        // 讓 XAML Designer 可以開啟（執行時會回退到 config.json）
        public SettingsWindow() : this("config.json") { }

        /* ------------ 綁定 UI ------------- */
        private void BindToUi()
        {
            // App
            TxtRootDir.Text = _cfg.App.RootDir;
            TxtInboxDir.Text = _cfg.App.InboxDir;
            TxtDbPath.Text = _cfg.App.DbPath;
            ChkDryRun.IsChecked = _cfg.App.DryRun;

            CmbMoveMode.SelectedItem = CmbMoveMode.Items.Cast<object>()
                .FirstOrDefault(i => ((System.Windows.Controls.ContentControl)i).Content?.ToString() == _cfg.App.MoveMode);

            CmbOverwrite.SelectedItem = CmbOverwrite.Items.Cast<object>()
                .FirstOrDefault(i => ((System.Windows.Controls.ContentControl)i).Content?.ToString() == _cfg.App.Overwrite);

            // Routing
            TxtPathTemplate.Text = _cfg.Routing.PathTemplate;
            ChkSafeCategories.IsChecked = _cfg.Routing.SafeCategories;

            // Classification
            CmbEngine.SelectedItem = CmbEngine.Items.Cast<object>()
                .FirstOrDefault(i => ((System.Windows.Controls.ContentControl)i).Content?.ToString() == _cfg.Classification.Engine);

            TxtStyle.Text = _cfg.Classification.Style;

            SldThreshold.Value = (_cfg.Classification.ConfidenceThreshold <= 1.0)
                ? _cfg.Classification.ConfidenceThreshold
                : _cfg.Classification.ConfidenceThreshold / 100.0;

            TxtFallback.Text = _cfg.Classification.FallbackCategory;
            LstTaxonomy.ItemsSource = _cfg.Classification.CustomTaxonomy?.ToList() ?? new();

            // OpenAI
            PwdApiKey.Password = _cfg.OpenAI.ApiKey ?? "";
            TxtModel.Text = _cfg.OpenAI.Model ?? "gpt-4o-mini";
        }

        private void CollectFromUi()
        {
            _cfg.App.RootDir = TxtRootDir.Text.Trim();
            _cfg.App.InboxDir = TxtInboxDir.Text.Trim();
            _cfg.App.DbPath = TxtDbPath.Text.Trim();
            _cfg.App.DryRun = ChkDryRun.IsChecked == true;

            if (CmbMoveMode.SelectedItem is System.Windows.Controls.ContentControl m)
                _cfg.App.MoveMode = m.Content?.ToString() ?? "move";
            if (CmbOverwrite.SelectedItem is System.Windows.Controls.ContentControl o)
                _cfg.App.Overwrite = o.Content?.ToString() ?? "rename";

            _cfg.Routing.PathTemplate = TxtPathTemplate.Text.Trim();
            _cfg.Routing.SafeCategories = ChkSafeCategories.IsChecked == true;

            if (CmbEngine.SelectedItem is System.Windows.Controls.ContentControl eng)
                _cfg.Classification.Engine = eng.Content?.ToString() ?? "llm";

            _cfg.Classification.Style = TxtStyle.Text.Trim();
            _cfg.Classification.ConfidenceThreshold = SldThreshold.Value; // 0~1
            _cfg.Classification.FallbackCategory = TxtFallback.Text.Trim();
            _cfg.Classification.CustomTaxonomy = LstTaxonomy.Items.Cast<string>().ToList();

            _cfg.OpenAI.ApiKey = PwdApiKey.Password;
            _cfg.OpenAI.Model = TxtModel.Text.Trim();
        }

        /* ------------ 按鈕事件 ------------- */
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            CollectFromUi();
            ConfigService.Save(_configPath, _cfg);
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void AddTag_Click(object sender, RoutedEventArgs e)
        {
            var t = TxtNewTag.Text.Trim();
            if (t.Length == 0) return;
            var list = LstTaxonomy.Items.Cast<string>().ToList();
            if (!list.Contains(t)) list.Add(t);
            LstTaxonomy.ItemsSource = list;
            TxtNewTag.Clear();
        }

        private void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            if (LstTaxonomy.SelectedItem is string s)
            {
                var list = LstTaxonomy.Items.Cast<string>().ToList();
                list.Remove(s);
                LstTaxonomy.ItemsSource = list;
            }
        }

        /* ------------ 瀏覽（以別名呼叫 WinForms 對話框） ------------- */
        private void BrowseRoot_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.FolderBrowserDialog();
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                TxtRootDir.Text = dlg.SelectedPath;
        }

        private void BrowseInbox_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.FolderBrowserDialog();
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                TxtInboxDir.Text = dlg.SelectedPath;
        }

        private void BrowseDb_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new WpfSaveFileDialog
            {
                Filter = "SQLite (*.db)|*.db|All (*.*)|*.*",
                AddExtension = true
            };
            if (dlg.ShowDialog() == true)
                TxtDbPath.Text = dlg.FileName;
        }
    }
}
