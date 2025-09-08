using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly string _configPath;
        private AppConfig _cfg;

        // 給設計工具／無參數呼叫用
        public SettingsWindow() : this("config.json") { }

        public SettingsWindow(string configPath, AppConfig? cfg = null)
        {
            InitializeComponent();

            _configPath = string.IsNullOrWhiteSpace(configPath) ? "config.json" : configPath;
            _cfg = cfg ?? SafeLoad(_configPath);

            BindToUi(_cfg);
        }

        private static AppConfig SafeLoad(string path)
        {
            try { return ConfigService.TryLoad(path); }
            catch { return new AppConfig(); }
        }

        private void BindToUi(AppConfig cfg)
        {
            // 專案下拉（示範）
            CmbProjects.ItemsSource = cfg.App?.Projects ?? Array.Empty<string>();
            if (!string.IsNullOrWhiteSpace(cfg.App?.ProjectName))
                CmbProjects.Text = cfg.App.ProjectName;

            // 乾跑
            ChkDryRun.IsChecked = cfg.App?.DryRun ?? false;

            // 自訂分類清單
            var tags = (cfg.Classification?.CustomTaxonomy ?? Array.Empty<string>()).ToList();
            LstTaxonomy.ItemsSource = tags;

            // 檔案清單（示範塞一些字串，確保 XAML 名稱存在並能編譯）
            ListFiles.ItemsSource = new[] { "demo-1.txt", "demo-2.pdf" };
        }

        private void HarvestFromUi()
        {
            _cfg ??= new AppConfig();

            // App 區
            _cfg.App ??= new AppSection();
            _cfg.App.ProjectName = CmbProjects.Text?.Trim() ?? "";
            _cfg.App.DryRun = ChkDryRun.IsChecked == true;

            // 將下拉目前所有專案（包含目前輸入）存回去
            var projList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in CmbProjects.Items.OfType<string>())
                if (!string.IsNullOrWhiteSpace(p)) projList.Add(p);
            if (!string.IsNullOrWhiteSpace(CmbProjects.Text)) projList.Add(CmbProjects.Text);
            _cfg.App.Projects = projList.ToList();

            // 自訂分類
            _cfg.Classification ??= new ClassificationSection();
            _cfg.Classification.CustomTaxonomy = LstTaxonomy.Items
                .OfType<string>()
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /* ============  按鈕事件（XAML 綁定到這些方法）  ============ */

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                HarvestFromUi();

                // 確保路徑存在 & 儲存
                var full = Path.GetFullPath(_configPath);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                ConfigService.Save(full, _cfg);

                MessageBox.Show("設定已儲存。", "設定", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"儲存失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddTag_Click(object sender, RoutedEventArgs e)
        {
            var tag = (TxtNewTag.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(tag)) return;

            var list = (LstTaxonomy.ItemsSource as IList<string>)?.ToList()
                       ?? LstTaxonomy.Items.OfType<string>().ToList();

            if (!list.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(tag);
                LstTaxonomy.ItemsSource = list.ToList(); // 重新指定以刷新畫面
            }

            TxtNewTag.Text = "";
            TxtNewTag.Focus();
        }

        private void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            var selected = LstTaxonomy.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(selected)) return;

            var list = (LstTaxonomy.ItemsSource as IList<string>)?.ToList()
                       ?? LstTaxonomy.Items.OfType<string>().ToList();

            list = list.Where(x => !string.Equals(x, selected, StringComparison.OrdinalIgnoreCase)).ToList();
            LstTaxonomy.ItemsSource = list;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 這裡純示範：把輸入關鍵字加到檔案清單的第一列（不影響設定）
            var kw = SearchBox.Text?.Trim();
            if (string.IsNullOrEmpty(kw))
            {
                ListFiles.ItemsSource = new[] { "demo-1.txt", "demo-2.pdf" };
            }
            else
            {
                ListFiles.ItemsSource = new[] { $"搜尋：{kw}", "demo-1.txt", "demo-2.pdf" };
            }
        }
    }
}
