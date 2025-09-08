using System.Linq;
using System.Windows;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

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
            _cfg = ConfigService.TryLoad(configPath);
            BindToUi();
            SldThreshold.ValueChanged += (_, __) => LblThreshold.Text = $"{SldThreshold.Value:0.00}";
        }

        private void BindToUi()
        {
            TxtRootDir.Text = _cfg.App.RootDir;
            TxtInboxDir.Text = _cfg.App.InboxDir;
            TxtDbPath.Text = _cfg.App.DbPath;
            ChkDryRun.IsChecked = _cfg.App.DryRun;

            CmbMoveMode.SelectedIndex = _cfg.App.MoveMode == "copy" ? 1 : 0;
            CmbOverwrite.SelectedIndex = _cfg.App.Overwrite switch
            {
                "skip" => 1,
                "rename" => 2,
                _ => 0
            };

            TxtPathTemplate.Text = _cfg.Routing.PathTemplate;
            ChkSafeCategories.IsChecked = _cfg.Routing.SafeCategories;

            CmbEngine.SelectedIndex = _cfg.Classification.Engine switch
            {
                "llm" => 1,
                "hybrid" => 2,
                _ => 0
            };
            TxtStyle.Text = _cfg.Classification.Style;
            SldThreshold.Value = _cfg.Classification.ConfidenceThreshold;
            TxtFallback.Text = _cfg.Classification.FallbackCategory;
            LstTaxonomy.ItemsSource = (_cfg.Classification.CustomTaxonomy ?? new()).ToList();

            PwdApiKey.Password = _cfg.OpenAI.ApiKey ?? "";
            TxtModel.Text = _cfg.OpenAI.Model ?? "gpt-4o-mini";
        }

        private void CollectFromUi()
        {
            _cfg.App.RootDir = TxtRootDir.Text.Trim();
            _cfg.App.InboxDir = TxtInboxDir.Text.Trim();
            _cfg.App.DbPath = TxtDbPath.Text.Trim();
            _cfg.App.DryRun = ChkDryRun.IsChecked == true;
            _cfg.App.MoveMode = (CmbMoveMode.SelectedItem as System.Windows.Controls.ContentControl)?.Content?.ToString() ?? "move";
            _cfg.App.Overwrite = (CmbOverwrite.SelectedItem as System.Windows.Controls.ContentControl)?.Content?.ToString() ?? "rename";

            _cfg.Routing.PathTemplate = TxtPathTemplate.Text.Trim();
            _cfg.Routing.SafeCategories = ChkSafeCategories.IsChecked == true;

            _cfg.Classification.Engine = (CmbEngine.SelectedItem as System.Windows.Controls.ContentControl)?.Content?.ToString() ?? "rules";
            _cfg.Classification.Style = TxtStyle.Text.Trim();
            _cfg.Classification.ConfidenceThreshold = SldThreshold.Value;
            _cfg.Classification.FallbackCategory = TxtFallback.Text.Trim();
            _cfg.Classification.CustomTaxonomy = LstTaxonomy.Items.Cast<string>().ToList();

            _cfg.OpenAI.ApiKey = PwdApiKey.Password;
            _cfg.OpenAI.Model = TxtModel.Text.Trim();
        }

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
    }
}
