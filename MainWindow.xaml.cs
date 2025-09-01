using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AI.KB.Assistant.Helpers;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant
{
	public partial class MainWindow : Window
	{
		private AppConfig _cfg;
		private DbService _db;
		private RoutingService _router;
		private LlmService _llm;

		// 目前列表實體資料（直接綁定到 ListView.ItemsSource）
		private List<Item> _currentItems = new();
		private string _currentView = "recent"; // recent / pending / status:xxx / search:kw

		public MainWindow()
		{
			InitializeComponent();

			// 讀取設定、建立服務
			_cfg = ConfigService.Load("config.json");
			_db = new DbService(_cfg.App.DbPath);
			_router = new RoutingService(_cfg);
			_llm = new LlmService(_cfg);

			ChkDryRun.IsChecked = _cfg.App.DryRun;

			LoadRecent(7);
			SetStatus("就緒");
		}

		/* ===================== 拖放區（收件匣） ===================== */

		private async void DropInbox(object sender, DragEventArgs e)
		{
			try
			{
				DropZone_DragLeave(sender, e); // 放開後恢復樣式

				if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
				var files = (string[])e.Data.GetData(DataFormats.FileDrop);

				foreach (var f in files)
					await ProcessOneAsync(f);

				MessageBox.Show("處理完成！");
				LoadRecent(7);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"拖放處理失敗：{ex.Message}");
				AddLog($"[錯誤] 拖放：{ex}");
			}
		}

		private void DropZone_DragEnter(object sender, DragEventArgs e)
		{
			DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(34, 176, 125)); // 綠色高亮
			DropZone.Background = new SolidColorBrush(Color.FromRgb(20, 32, 56));
		}

		private void DropZone_DragLeave(object sender, DragEventArgs e)
		{
			DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(46, 124, 246)); // 還原藍色
			DropZone.Background = new SolidColorBrush(Color.FromRgb(15, 22, 40));
		}

		/* ===================== 側欄按鈕 / 搜尋 ===================== */

		private void BtnSearch_Click(object sender, RoutedEventArgs e) => DoSearch();

		private void SearchBox_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter) DoSearch();
		}

		private void BtnRecent_Click(object sender, RoutedEventArgs e) => LoadRecent(7);
		private void BtnPending_Click(object sender, RoutedEventArgs e) => LoadPending();
		private void BtnProgress_Click(object sender, RoutedEventArgs e) => LoadByStatus("In-Progress");
		private void BtnTodo_Click(object sender, RoutedEventArgs e) => LoadByStatus("To-Do");

		private void BtnSettings_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				var win = new AI.KB.Assistant.Views.SettingsWindow("config.json") { Owner = this };
				if (win.ShowDialog() != true) return;

				var oldDryRun = _cfg.App.DryRun;
				var oldDbPath = _cfg.App.DbPath;
				var oldModel = _cfg.OpenAI.Model;

				_cfg = ConfigService.Load("config.json");
				ChkDryRun.IsChecked = _cfg.App.DryRun;

				// DB 可能被換路徑 → 重建
				if (!string.Equals(oldDbPath, _cfg.App.DbPath, StringComparison.OrdinalIgnoreCase))
				{
					_db?.Dispose();
					_db = new DbService(_cfg.App.DbPath);
				}

				// Router / LLM 皆與設定相關 → 重建
				_router = new RoutingService(_cfg);
				_llm = new LlmService(_cfg);

				if (oldDryRun != _cfg.App.DryRun) AddLog($"[設定] 乾跑切換為：{_cfg.App.DryRun}");
				if (!string.Equals(oldModel, _cfg.OpenAI.Model, StringComparison.OrdinalIgnoreCase))
					AddLog($"[設定] LLM 模型切換為：{_cfg.OpenAI.Model}");

				MessageBox.Show("設定已儲存並重新載入。");
				LoadRecent(7);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"重新載入設定時發生錯誤：{ex.Message}", "設定", MessageBoxButton.OK, MessageBoxImage.Error);
				AddLog($"[設定錯誤] {ex}");
			}
		}

		/* ===================== 一鍵確認並搬檔（支援多選） ===================== */

		private void BtnConfirmMove_Click(object sender, RoutedEventArgs e)
		{
			if (_currentItems.Count == 0)
			{
				MessageBox.Show("沒有可處理的項目。請先到『📝 待確認』或搜尋清單中選擇。");
				return;
			}

			// 依選取項目取得對應 Item
			var targets = ListFiles.SelectedItems.Cast<Item>().ToList();
			if (targets.Count == 0)
			{
				MessageBox.Show("請先在清單中選擇要確認的檔案（可 Ctrl/Shift 多選）。");
				return;
			}

			int moved = 0;
			foreach (var it in targets)
			{
				try
				{
					var src = it.Path;
					if (!File.Exists(src))
					{
						AddLog($"[缺檔] 找不到：{src}");
						continue;
					}

					var when = DateResolver.FromFilenameOrNow(src);
					var cat = string.IsNullOrWhiteSpace(it.Category) ? _cfg.Classification.FallbackCategory : it.Category!;
					var dest = _router.BuildDestination(src, cat, when);
					dest = _router.ResolveCollision(dest);

					var overwrite = _cfg.App.Overwrite.Equals("overwrite", StringComparison.OrdinalIgnoreCase);
					if (_cfg.App.MoveMode.Equals("copy", StringComparison.OrdinalIgnoreCase))
						File.Copy(src, dest, overwrite);
					else
						File.Move(src, dest, overwrite);

					// DB 更新新路徑
					_db.UpdateAfterMove(it.Id, dest, "To-Do");
					AddLog($"[確認搬檔] {Path.GetFileName(src)} → {dest}");
					moved++;
				}
				catch (Exception ex)
				{
					AddLog($"[錯誤] {it.Filename}: {ex.Message}");
				}
			}

			MessageBox.Show($"已完成 {moved} 筆搬檔。");

			// 重新整理當前視圖
			if (_currentView == "pending") LoadPending();
			else if (_currentView.StartsWith("status:")) LoadByStatus(_currentView.Split(':')[1]);
			else if (_currentView.StartsWith("search:")) DoSearch();
			else LoadRecent(7);
		}

		/* ===================== 核心：處理單一檔案（拖放時） ===================== */

		private async System.Threading.Tasks.Task ProcessOneAsync(string srcPath)
		{
			try
			{
				var text = Path.GetFileNameWithoutExtension(srcPath);

				// 1) AI 分類
				var res = await _llm.ClassifyAsync(text);

				// 低信心：不搬檔，入庫為待確認
				double th = (_cfg.Classification.ConfidenceThreshold <= 1.0)
							? _cfg.Classification.ConfidenceThreshold * 100.0
							: _cfg.Classification.ConfidenceThreshold;

				if (res.confidence < th)
				{
					_db.Upsert(new Item
					{
						Path = srcPath,
						Filename = Path.GetFileName(srcPath),
						Category = res.primary_category,
						Confidence = res.confidence,
						CreatedTs = DateTimeOffset.Now.ToUnixTimeSeconds(),
						Summary = res.summary,
						Reasoning = res.reasoning,
						Status = "To-Do"
					});
					AddLog($"[待確認/低信心 {res.confidence:0}%] {Path.GetFileName(srcPath)} → 建議類別：{res.primary_category}（未搬檔）");
					return;
				}

				// 2) 解析日期（檔名 → 年月）
				var when = DateResolver.FromFilenameOrNow(srcPath);

				// 3) 計算目的地
				var dest = _router.BuildDestination(srcPath, res.primary_category, when);
				dest = _router.ResolveCollision(dest);

				// 4) 乾跑模式：只顯示模擬結果
				if (ChkDryRun.IsChecked == true || _cfg.App.DryRun)
				{
					AddLog($"[DRY RUN] {Path.GetFileName(srcPath)} → {dest}");
					return;
				}

				// 5) 搬檔
				var overwrite = _cfg.App.Overwrite.Equals("overwrite", StringComparison.OrdinalIgnoreCase);
				if (_cfg.App.MoveMode.Equals("copy", StringComparison.OrdinalIgnoreCase))
					File.Copy(srcPath, dest, overwrite);
				else
					File.Move(srcPath, dest, overwrite);

				// 6) 入庫
				_db.Upsert(new Item
				{
					Path = dest,
					Filename = Path.GetFileName(dest),
					Category = res.primary_category,
					Confidence = res.confidence,
					CreatedTs = DateTimeOffset.Now.ToUnixTimeSeconds(),
					Summary = res.summary,
					Reasoning = res.reasoning,
					Status = "To-Do"
				});

				AddLog($"{Path.GetFileName(srcPath)} → {dest}");
			}
			catch (Exception ex)
			{
				AddLog($"[錯誤] {Path.GetFileName(srcPath)}: {ex.Message}");
			}
		}

		/* ===================== 載入 / 搜尋 / 呈現 ===================== */

		private void LoadRecent(int days)
		{
			_currentView = "recent";
			var items = _db.Recent(days);
			RenderItems(items, $"最近 {days} 天");
		}

		private void LoadPending()
		{
			_currentView = "pending";
			double th = (_cfg.Classification.ConfidenceThreshold <= 1.0)
						? _cfg.Classification.ConfidenceThreshold * 100.0
						: _cfg.Classification.ConfidenceThreshold;

			var items = _db.PendingLowConfidence(th);
			RenderItems(items, $"待確認（低於 {th:0}%）");
		}

		private void LoadByStatus(string status)
		{
			_currentView = $"status:{status}";
			var items = _db.ByStatus(status);
			RenderItems(items, $"狀態：{status}");
		}

		private void DoSearch()
		{
			var kw = (SearchBox.Text ?? string.Empty).Trim();
			if (kw.Length == 0) { LoadRecent(7); return; }
			_currentView = $"search:{kw}";
			var items = _db.Search(kw);
			RenderItems(items, $"搜尋「{kw}」");
		}

		private void RenderItems(IEnumerable<Item> items, string header)
		{
			_currentItems = items.ToList();
			TxtHeader.Text = $"── {header} ──（可多選，按『🟢 一鍵確認並搬檔』）";
			ListFiles.ItemsSource = _currentItems;
			SetStatus($"{_currentItems.Count} 筆");
		}

		/* ===================== 右鍵選單：開檔 / 開夾 / 複製路徑 ===================== */

		private Item? GetContextItem()
		{
			if (ListFiles.SelectedItem is Item it) return it;
			return null;
		}

		private void OpenFile_Click(object sender, RoutedEventArgs e)
		{
			var it = GetContextItem();
			if (it == null) return;
			try
			{
				if (File.Exists(it.Path))
					System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(it.Path) { UseShellExecute = true });
				else
					MessageBox.Show("找不到檔案。");
			}
			catch (Exception ex) { MessageBox.Show(ex.Message); }
		}

		private void OpenFolder_Click(object sender, RoutedEventArgs e)
		{
			var it = GetContextItem();
			if (it == null) return;
			try
			{
				var dir = Path.GetDirectoryName(it.Path);
				if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
					System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
				else
					MessageBox.Show("找不到資料夾。");
			}
			catch (Exception ex) { MessageBox.Show(ex.Message); }
		}

		private void CopyPath_Click(object sender, RoutedEventArgs e)
		{
			var it = GetContextItem();
			if (it == null) return;
			try
			{
				Clipboard.SetText(it.Path ?? "");
				SetStatus("已複製路徑");
			}
			catch (Exception ex) { MessageBox.Show(ex.Message); }
		}

		/* ===================== 小工具 ===================== */

		private void AddLog(string line)
		{
			if (!Dispatcher.CheckAccess())
			{
				Dispatcher.Invoke(() => SetStatus(line));
				return;
			}
			SetStatus(line);
		}

		private void SetStatus(string text)
		{
			if (!Dispatcher.CheckAccess())
			{
				Dispatcher.Invoke(() => TxtStatus.Text = text);
				return;
			}
			TxtStatus.Text = text;
		}

	}
}
