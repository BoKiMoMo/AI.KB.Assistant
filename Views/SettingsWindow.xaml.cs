using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AI.KB.Assistant.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly string _cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        private AppConfig _cfg;

        public SettingsWindow(Window owner, AppConfig current)
        {
            InitializeComponent();
            Owner = owner;

            _cfg = current ?? ConfigService.TryLoad(_cfgPath);
            LoadToUI(_cfg);
        }

        // -------------------- 載入/套用 --------------------

        private void LoadToUI(AppConfig cfg)
        {
            // App
            SetText("TxtRoot", cfg.App.RootDir);
            SetText("TxtDb", cfg.App.DbPath);
            SetText("TxtProjectLock", cfg.App.ProjectLock);

            var cbMode = Find<ComboBox>("CbStartupMode");
            if (cbMode != null)
            {
                cbMode.ItemsSource = Enum.GetValues(typeof(StartupMode)).Cast<StartupMode>().ToList();
                cbMode.SelectedItem = cfg.App.StartupUIMode ?? StartupMode.Simple;
            }

            // Import
            SetText("TxtInbox", cfg.Import.HotFolderPath);
            SetCheck("ChkIncludeSubdir", cfg.Import.IncludeSubdir);

            var cbMove = Find<ComboBox>("CbMoveMode");
            if (cbMove != null)
            {
                cbMove.ItemsSource = Enum.GetValues(typeof(MoveMode)).Cast<MoveMode>().ToList();
                cbMove.SelectedItem = cfg.Import.MoveMode;
            }

            var cbOverwrite = Find<ComboBox>("CbOverwrite");
            if (cbOverwrite != null)
            {
                cbOverwrite.ItemsSource = Enum.GetValues(typeof(OverwritePolicy)).Cast<OverwritePolicy>().ToList();
                cbOverwrite.SelectedItem = cfg.Import.OverwritePolicy;
            }

            SetText("TxtBlacklistExts", string.Join(", ", cfg.Import.BlacklistExts ?? Array.Empty<string>()));
            SetText("TxtBlacklistFolders", string.Join(", ", cfg.Import.BlacklistFolderNames ?? Array.Empty<string>()));

            // 分類
            SetText("TxtThreshold", cfg.Classification.ConfidenceThreshold.ToString("0.00"));

            // Theme（顯示現值，編輯框允許 #RRGGBB）
            SetText("TxtThemeBackground", cfg.ThemeColors.Background);
            SetText("TxtThemePanel", cfg.ThemeColors.Panel);
            SetText("TxtThemeBorder", cfg.ThemeColors.Border);
            SetText("TxtThemeText", cfg.ThemeColors.Text);
            SetText("TxtThemeTextMuted", cfg.ThemeColors.TextMuted);
            SetText("TxtThemePrimary", cfg.ThemeColors.Primary);
            SetText("TxtThemePrimaryHover", cfg.ThemeColors.PrimaryHover);
            SetText("TxtThemeSuccess", cfg.ThemeColors.Success);
            SetText("TxtThemeWarning", cfg.ThemeColors.Warning);
            SetText("TxtThemeError", cfg.ThemeColors.Error);
            SetText("TxtBannerInfo", cfg.ThemeColors.BannerInfo);
            SetText("TxtBannerWarn", cfg.ThemeColors.BannerWarn);
            SetText("TxtBannerError", cfg.ThemeColors.BannerError);
        }

        private void ApplyFromUI(AppConfig cfg)
        {
            // App
            cfg.App.RootDir = GetText("TxtRoot");
            cfg.App.DbPath = GetText("TxtDb");
            cfg.App.ProjectLock = GetText("TxtProjectLock");

            var cbMode = Find<ComboBox>("CbStartupMode");
            if (cbMode?.SelectedItem is StartupMode sm) cfg.App.StartupUIMode = sm;

            // Import
            cfg.Import.HotFolderPath = GetText("TxtInbox");
            cfg.Import.IncludeSubdir = GetCheck("ChkIncludeSubdir");
            var cbMove = Find<ComboBox>("CbMoveMode");
            if (cbMove?.SelectedItem is MoveMode mv) cfg.Import.MoveMode = mv;
            var cbOverwrite = Find<ComboBox>("CbOverwrite");
            if (cbOverwrite?.SelectedItem is OverwritePolicy ov) cfg.Import.OverwritePolicy = ov;

            cfg.Import.BlacklistExts = SplitCsv(GetText("TxtBlacklistExts"));
            cfg.Import.BlacklistFolderNames = SplitCsv(GetText("TxtBlacklistFolders"));

            // 分類
            if (double.TryParse(GetText("TxtThreshold"), out var th))
                cfg.Classification.ConfidenceThreshold = Math.Clamp(th, 0, 1);

            // Theme
            cfg.ThemeColors.Background = GetText("TxtThemeBackground");
            cfg.ThemeColors.Panel = GetText("TxtThemePanel");
            cfg.ThemeColors.Border = GetText("TxtThemeBorder");
            cfg.ThemeColors.Text = GetText("TxtThemeText");
            cfg.ThemeColors.TextMuted = GetText("TxtThemeTextMuted");
            cfg.ThemeColors.Primary = GetText("TxtThemePrimary");
            cfg.ThemeColors.PrimaryHover = GetText("TxtThemePrimaryHover");
            cfg.ThemeColors.Success = GetText("TxtThemeSuccess");
            cfg.ThemeColors.Warning = GetText("TxtThemeWarning");
            cfg.ThemeColors.Error = GetText("TxtThemeError");
            cfg.ThemeColors.BannerInfo = GetText("TxtBannerInfo");
            cfg.ThemeColors.BannerWarn = GetText("TxtBannerWarn");
            cfg.ThemeColors.BannerError = GetText("TxtBannerError");

            // 重新整理快取
            cfg.Import.RebuildExtGroupsCache();
        }

        // -------------------- 事件 --------------------

        private void BtnPickRoot_Click(object sender, RoutedEventArgs e) => FolderPickTo("TxtRoot");
        private void BtnPickDb_Click(object sender, RoutedEventArgs e) => FilePickTo("TxtDb", "SQLite DB|*.db|All Files|*.*");
        private void BtnPickInbox_Click(object sender, RoutedEventArgs e) => FolderPickTo("TxtInbox");

        private void BtnApplyTheme_Click(object sender, RoutedEventArgs e)
        {
            ApplyFromUI(_cfg);
            ThemeService.Apply(_cfg);
            MessageBox.Show("主題已套用。", "提示");
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            ApplyFromUI(_cfg);
            ConfigService.Save(_cfg, _cfgPath);
            ThemeService.Apply(_cfg);
            DialogResult = true;
            Close();
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            _cfg = ConfigService.TryLoad(_cfgPath);
            LoadToUI(_cfg);
            ThemeService.Apply(_cfg);
        }

        // -------------------- 小工具 --------------------

        private T? Find<T>(string name) where T : class => this.FindName(name) as T;

        private void SetText(string name, string? val)
        {
            var tb = Find<TextBox>(name);
            if (tb != null) tb.Text = val ?? string.Empty;
        }
        private string GetText(string name)
        {
            var tb = Find<TextBox>(name);
            return tb?.Text?.Trim() ?? string.Empty;
        }

        private void SetCheck(string name, bool v)
        {
            var cb = Find<CheckBox>(name);
            if (cb != null) cb.IsChecked = v;
        }
        private bool GetCheck(string name)
        {
            var cb = Find<CheckBox>(name);
            return cb?.IsChecked == true;
        }

        private static string[] SplitCsv(string s)
            => string.IsNullOrWhiteSpace(s)
               ? Array.Empty<string>()
               : s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                  .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

        private void FolderPickTo(string targetTextBoxName)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            var r = dlg.ShowDialog();
            if (r == System.Windows.Forms.DialogResult.OK)
                SetText(targetTextBoxName, dlg.SelectedPath);
        }

        private void FilePickTo(string targetTextBoxName, string filter)
        {
            var dlg = new OpenFileDialog { Filter = filter, CheckFileExists = false };
            if (dlg.ShowDialog(this) == true)
                SetText(targetTextBoxName, dlg.FileName);
        }
    }
}
