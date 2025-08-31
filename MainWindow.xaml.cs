using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using AI.KB.Assistant.Helpers;

namespace AI.KB.Assistant;

public partial class MainWindow : Window
{
	private readonly AppConfig _cfg;
	private readonly DbService _db;
	private readonly RoutingService _router;
	private readonly LlmService _llm;

	// 目前清單的實體資料，索引對應 ListView（第 0 筆是 header，實際資料從 1 起）
	private List<Item> _currentItems = new();
	private string _currentView = "recent"; // recent/pending/status/search

	public MainWindow()
	{
		InitializeComponent();

		_cfg = ConfigService.Load("config.json");
		_db = new DbService(_cfg.App.DbPath);
		_router = new RoutingService(_cfg);
		_llm = new LlmService(_cfg);

		ChkDryRun.IsChecked = _cfg.App.DryRun;
		LoadRecent(7);
	}

	/* ---------------- UI 事件 ---------------- */

	private async void DropInbox(object sender, DragEventArgs e)
	{
		if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
		var files = (string[])e.Data.GetData(DataFormats.FileDrop);

		foreach (var f in files)
			await ProcessOneAsync(f);

		MessageBox.Show("處理完成！");
		LoadRecent(7);
	}

	private void BtnSearch_Click(object sender, RoutedEventArgs e) => DoSearch();
	private void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) DoSearch(); }

	private void BtnRecent_Click(object sender, RoutedEventArgs e) => LoadRecent(7);
	private void BtnPending_Click(object sender, RoutedEventArgs e) => LoadPending();
	private void BtnProgress_Click(object sender, RoutedEventArgs e) => LoadByStatus("In-Progress");
	private void BtnTodo_Click(object sender, RoutedEventArgs e) => LoadByStatus("To-Do");
	private void BtnSettings_Click(object sender, RoutedEventArgs e) => MessageBox.Show("設定頁待實作～");

	// ✅ 一鍵確認並搬檔（支援多選）
	private void BtnConfirmMove_Click(object? sender, RoutedEventArgs e)
	{
		if (_currentItems.Count == 0)
		{
			MessageBox.Show("沒有可處理的清單項目。先到『📝 待確認』或搜尋出檔案再選擇。");
			return;
		}

		// 把 ListView 的 SelectedItems 轉成對應的 _currentItems
		var targets = new List<Item>();
		foreach (var obj in ListFiles.SelectedItems)
		{
			int idx = ListFiles.Items.IndexOf(obj);
			if (idx <= 0) continue; // 跳過 header
			if (idx - 1 < _currentItems.Count) targets.Add(_currentItems[idx - 1]);
		}

		if (targets.Count == 0)
		{
			MessageBox.Show("請先在清單中選擇要確認的檔案（可按 Ctrl 或 Shift 多選）。");
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
		// 重新整理目前視圖
		if (_currentView == "pending") LoadPending();
		else if (_currentView.StartsWith("status:")) LoadByStatus(_currentView.Split(':')[1]);
		else if (_currentView.StartsWith("search:")) DoSearch(); // 直接重跑
		else LoadRecent(7);
	}

	/* ---------------- 核心流程：處理一個檔案（拖放時） ---------------- */

	private async Task ProcessOneAsync(string srcPath)
	{
		try
		{
			var text = Path.GetFileNameWithoutExtension(srcPath);

			// 1) AI 分類
			var res = await _llm.ClassifyAsync(text);

			// 低信心：不搬檔，記錄為待確認（保持原路徑）
			var threshold = _cfg.Classification.ConfidenceThreshold; // config 0~1
			if (res.confidence < threshold * 100) // res 0~100
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

			// 2) 解析日期（檔名→年月）
			var when = DateResolver.FromFilenameOrNow(srcPath);

			// 3) 目的地（檔名不動）
			var dest = _router.BuildDestination(srcPath, res.primary_category, when);
			dest = _router.ResolveCollision(dest);

			// 4) 乾跑模式
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

	/* ---------------- 資料載入 / 搜尋 / 呈現 ---------------- */

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
		ListFiles.Items.Clear();
		ListFiles.Items.Add($"── {header} ──（可多選，按『🟢 一鍵確認並搬檔』）");
		for (int i = 0; i < _currentItems.Count; i++)
		{
			var it = _currentItems[i];
			var date = DateTimeOffset.FromUnixTimeSeconds(it.CreatedTs).ToLocalTime()
					   .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			ListFiles.Items.Add($"[{i + 1}] {it.Filename}  —  [{it.Category}]  —  {date}");
		}
	}

	/* ---------------- 小工具 ---------------- */

	private void AddLog(string line)
	{
		if (!Dispatcher.CheckAccess())
		{
			Dispatcher.Invoke(() => ListFiles.Items.Add(line));
			return;
		}
		ListFiles.Items.Add(line);
	}
}
