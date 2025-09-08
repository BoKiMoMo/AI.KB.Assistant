using System;
using System.Linq;
using System.Windows;
using System.Collections.Generic;
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

            SldThreshold.ValueChanged += (_, __) =>
                LblThreshold.Text = $"{SldThreshold.Value:0.00}";
        }

        private void BindToUi()
        {
            // App
            TxtRootDir.Text = _cfg.App.RootDir;
            TxtInboxDir.Text = _cfg.App.InboxDir;
            TxtDbPath.Text = _cfg.App.DbPath;
            ChkDryRun.IsChecked = _cfg.App.DryRun;

            CmbMoveMode.SelectedIndex = _cfg.App.MoveMode.Equals("copy", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            CmbOverwrite.SelectedIndex = _cfg.App.Overwrite.ToLower() switch
            {
                "skip" => 1,
                "rename" => 2,
                _ => 0
            };

            // 分類風格（用 Tag 存實際值）
            CmbClassificationMode.SelectedItem = CmbClassificationMode.Items
                .Cast<System.Windows.Controls.ComboBoxItem>()
                .FirstOrDefault(i => string.Equals((string)i.Tag, _cfg.App.ClassificationMode, StringComparison.OrdinalIgnoreCase));

            // TimePeriod 粒度
            CmbTimeGranularity.SelectedItem = CmbTimeGranularity.Items
                .Cast<System.Windows.Controls.ComboBoxItem>()
                .FirstOrDefault(i => string.Equals((string)i.Tag, _cfg.App.TimeGranularity, StringComparison.OrdinalIgnoreCase));

            // 專案
            TxtProjects.Text = string.Join(", ", _cfg.App.Projects ?? new List<string> { "Default" });
            var src = (_cfg.App.Projects ?? new List<string> { "Default" }).ToList();
            if (!src.Contains(_cfg.App.ProjectName)) src.Insert(0, _cfg.App.ProjectName);
            CmbDefaultProject.ItemsSource = src;
            CmbDefaultProject.Text = _cfg.App.ProjectName ?? "Default";

            // AI 分類
            CmbEngine.SelectedIndex = _cfg.Classification.Engine.ToLower() switch
            {
                "llm" => 1,
                "hybrid" => 2,
                _ => 0
            };
            TxtStyle.Text = _cfg.Classification.Style;
            SldThreshold.Value = _cfg.Classification.ConfidenceThreshold;
            TxtFallback.Text = _cfg.Classification.FallbackCategory;
            LstTaxonomy.ItemsSource = (_cfg.Classification.CustomTaxonomy ?? new()).ToList();

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

            _cfg.App.MoveMode = CmbMoveMode.SelectedIndex == 1 ? "copy" : "move";
            _cfg.App.Overwrite = CmbOverwrite.SelectedIndex switch
            {
                0 => "overwrite",
                1 => "skip",
                2 => "rename",
                _ => "rename"
            };

            // 分類風格與時間粒度
            _cfg.App.ClassificationMode =
                (CmbClassificationMode.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "Category";
            _cfg.App.TimeGranularity =
                (CmbTimeGranularity.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "Month";

            // 專案
            var projects = (TxtProjects.Text ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();
            if (projects.Count == 0) projects.Add("Default");
            _cfg.App.Projects = projects;
            _cfg.App.ProjectName = string.IsNullOrWhiteSpace(CmbDefaultProject.Text) ? "Default" : CmbDefaultProject.Text.Trim();

            // AI 分類
            _cfg.Classification.Engine = CmbEngine.SelectedIndex switch
            {
                1 => "llm",
                2 => "hybrid",
                _ => "dummy"
            };
            _cfg.Classification.Style = TxtStyle.Text.Trim();
            _cfg.Classification.ConfidenceThreshold = SldThreshold.Value;
            _cfg.Classification.FallbackCategory = TxtFallback.Text.Trim();
            _cfg.Classification.CustomTaxonomy = LstTaxonomy.Items.Cast<string>().ToList();

            // OpenAI
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
