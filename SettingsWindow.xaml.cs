using System;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
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
			_cfg = ConfigService.TryLoad(configPath); // 沒檔案也不會丟例外

			BindToUi();

			// slider 即時顯示
			SldThreshold.ValueChanged += (_, __) =>
			{
				LblThreshold.Text = $"{SldThreshold.Value:0.00}";
			};
		}

		/* ------------------- UI 綁定 ------------------- */

		private void BindToUi()
		{
			// App
			TxtRootDir.Text = _cfg.App.RootDir ?? "";
			TxtInboxDir.Text = _cfg.App.InboxDir ?? "";
			TxtDbPath.Text = _cfg.App.DbPath ?? "";
			ChkDryRun.IsChecked = _cfg.App.DryRun;

			EnsureComboItem(CmbMoveMode, _cfg.App.MoveMode ?? "move", "move", "copy");
			EnsureComboItem(CmbOverwrite, _cfg.App.Overwrite ?? "rename", "overwrite", "skip", "rename");

			// Routing
			TxtPathTemplate.Text = _cfg.Routing.PathTemplate ?? "{root}/{category}/{yyyy}/{mm}/";
			ChkSafeCategories.IsChecked = _cfg.Routing.SafeCategories;

			// Classification
			EnsureComboItem(CmbEngine, _cfg.Classification.Engine ?? "llm", "llm", "dummy", "hybrid");
			TxtStyle.Text = _cfg.Classification.Style ?? "topic";
			SldThreshold.Value = Clamp01(_cfg.Classification.ConfidenceThreshold <= 1
				? _cfg.Classification.ConfidenceThreshold
				: _cfg.Classification.ConfidenceThreshold / 100.0);
			TxtFallback.Text = _cfg.Classification.FallbackCategory ?? "unsorted";
			LstTaxonomy.ItemsSource = (_cfg.Classification.CustomTaxonomy ?? new List<string>()).ToList();
			// OpenAI
			TxtModel.Text = _cfg.OpenAI.Model ?? "gpt-4o-mini";
			PwdApiKey.Password = _cfg.OpenAI.ApiKey ?? "";
		}

		private static void EnsureComboItem(ComboBox cmb, string value, params string[] candidates)
		{
			if (cmb == null) return;
			var found = cmb.Items.Cast<ContentControl>()
						   .FirstOrDefault(i => string.Equals(i.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase));

			if (found != null) { cmb.SelectedItem = found; return; }

			// 若候選項沒這個值，動態加一個
			var cc = new ContentControl { Content = value };
			cmb.Items.Add(cc);
			cmb.SelectedItem = cc;
		}

		private static double Clamp01(double d) => d < 0 ? 0 : (d > 1 ? 1 : d);

		private void CollectFromUi()
		{
			_cfg.App.RootDir = TxtRootDir.Text.Trim();
			_cfg.App.InboxDir = TxtInboxDir.Text.Trim();
			_cfg.App.DbPath = TxtDbPath.Text.Trim();
			_cfg.App.DryRun = ChkDryRun.IsChecked == true;

			_cfg.App.MoveMode = (CmbMoveMode.SelectedItem as ContentControl)?.Content?.ToString() ?? "move";
			_cfg.App.Overwrite = (CmbOverwrite.SelectedItem as ContentControl)?.Content?.ToString() ?? "rename";

			_cfg.Routing.PathTemplate = TxtPathTemplate.Text.Trim();
			_cfg.Routing.SafeCategories = ChkSafeCategories.IsChecked == true;

			_cfg.Classification.Engine = (CmbEngine.SelectedItem as ContentControl)?.Content?.ToString() ?? "llm";
			_cfg.Classification.Style = TxtStyle.Text.Trim();
			_cfg.Classification.ConfidenceThreshold = Clamp01(SldThreshold.Value);
			_cfg.Classification.FallbackCategory = TxtFallback.Text.Trim();
			_cfg.Classification.CustomTaxonomy = LstTaxonomy.Items.Cast<string>().ToList();

			_cfg.OpenAI.Model = TxtModel.Text.Trim();
			_cfg.OpenAI.ApiKey = PwdApiKey.Password ?? "";
		}

		/* ------------------- Buttons ------------------- */

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
			var t = (TxtNewTag.Text ?? "").Trim();
			if (t.Length == 0) return;

			var list = LstTaxonomy.Items.Cast<string>().ToList();
			if (!list.Contains(t, StringComparer.OrdinalIgnoreCase))
				list.Add(t);

			LstTaxonomy.ItemsSource = list;
			TxtNewTag.Clear();
		}

		private void RemoveTag_Click(object sender, RoutedEventArgs e)
		{
			if (LstTaxonomy.SelectedItem is string s)
			{
				var list = LstTaxonomy.Items.Cast<string>().ToList();
				list.RemoveAll(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase));
				LstTaxonomy.ItemsSource = list;
			}
		}

		/* ------------------- WPF 檔案/資料夾挑選 ------------------- */

		// 以 OpenFileDialog 假檔名技巧取得資料夾
		private static string PickFolderByOpenFileDialog(string title, string? initialDir = null)
		{
			var dlg = new OpenFileDialog
			{
				Title = string.IsNullOrWhiteSpace(title) ? "選擇資料夾" : title,
				CheckFileExists = false,
				ValidateNames = false,
				FileName = "選取此資料夾"
			};
			if (!string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir))
				dlg.InitialDirectory = initialDir;

			return dlg.ShowDialog() == true ? Path.GetDirectoryName(dlg.FileName) ?? "" : "";
		}

		private void BrowseRoot_Click(object sender, RoutedEventArgs e)
		{
			var folder = PickFolderByOpenFileDialog("選擇根目錄", TxtRootDir.Text);
			if (!string.IsNullOrWhiteSpace(folder)) TxtRootDir.Text = folder;
		}

		private void BrowseInbox_Click(object sender, RoutedEventArgs e)
		{
			var folder = PickFolderByOpenFileDialog("選擇收件匣", TxtInboxDir.Text);
			if (!string.IsNullOrWhiteSpace(folder)) TxtInboxDir.Text = folder;
		}

		private void BrowseDb_Click(object sender, RoutedEventArgs e)
		{
			var dlg = new SaveFileDialog
			{
				Title = "選擇或建立 SQLite 檔案",
				Filter = "SQLite (*.db)|*.db|所有檔案 (*.*)|*.*"
			};

			try
			{
				var p = TxtDbPath.Text?.Trim();
				if (!string.IsNullOrWhiteSpace(p))
				{
					var dir = Path.GetDirectoryName(p);
					if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
						dlg.InitialDirectory = dir;

					var name = Path.GetFileName(p);
					if (!string.IsNullOrWhiteSpace(name))
						dlg.FileName = name;
				}
			}
			catch { /* ignore */ }

			if (dlg.ShowDialog() == true)
				TxtDbPath.Text = dlg.FileName;
		}
	}
}
