using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;   // ← 讓 TreeViewItem 可用
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant;

public partial class MainWindow : Window
{
	private AppConfig _cfg;
	private DbService _db;
	private readonly RoutingService _router;
	private readonly LlmService _llm;

	public MainWindow()
	{
		InitializeComponent();

		_cfg = ConfigService.TryLoad("config.json");
		_db = new DbService(_cfg.App.DbPath);
		_router = new RoutingService(_cfg);
		_llm = new LlmService(_cfg);

		ChkDryRun.IsChecked = _cfg.App.DryRun;
		LoadRecent(7);
		BuildCategoryTree();
	}

	/* ---------- UI 事件 ---------- */

	private void BtnSearch_Click(object sender, RoutedEventArgs e) => DoSearch();
	private void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) DoSearch(); }
	private void BtnRecent_Click(object sender, RoutedEventArgs e) => LoadRecent(7);
	private void BtnProgress_Click(object sender, RoutedEventArgs e) => LoadByStatus("in-progress");
	private void BtnTodo_Click(object sender, RoutedEventArgs e) => LoadByStatus("todo");
	private void BtnPending_Click(object sender, RoutedEventArgs e) => LoadByStatus("pending");

	private void BtnSettings_Click(object sender, RoutedEventArgs e)
	{
		// 先簡化：只提示。你已有 SettingsWindow 時再接回去即可。
		MessageBox.Show("設定頁待擴充。", "設定", MessageBoxButton.OK, MessageBoxImage.Information);
	}

	private void BtnHelp_Click(object sender, RoutedEventArgs e)
	{
		var help = string.Join("\n", new[]
		{
			"➊ 將檔案直接拖放到視窗即可加入收件匣並自動分類。",
			"➋ 左側可用『總目錄』快速篩選分類。",
			"➌ 乾跑：不搬檔、只模擬流程（上方勾選）。",
			"➍ 搜尋支援關鍵字（檔名/分類/摘要）。",
			"➎ 未來可接 OpenAI 進行更精準分類（目前為保底關鍵字分類）。"
		});
		MessageBox.Show(help, "AI 知識庫助手｜使用說明",
			MessageBoxButton.OK, MessageBoxImage.Information);
	}

	/* ---------- 拖放收件匣 ---------- */
	private async void DropInbox(object sender, DragEventArgs e)
	{
		if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
		var files = (string[])e.Data.GetData(DataFormats.FileDrop);

		int ok = 0, fail = 0;
		foreach (var f in files)
		{
			try { await ProcessOneAsync(f); ok++; }
			catch { fail++; }
		}

		MessageBox.Show($"處理完成：成功 {ok} / 失敗 {fail}", "完成",
			MessageBoxButton.OK, MessageBoxImage.Information);

		LoadRecent(7);
		BuildCategoryTree();
	}

	private async Task ProcessOneAsync(string srcPath)
	{
		// 用檔名當內容做分類（之後可以改為 OCR/全文）
		var text = Path.GetFileNameWithoutExtension(srcPath);

		var res = await _llm.ClassifyAsync(text);
		var when = DateResolver.FromFilenameOrNow(srcPath);

		var dest = _router.BuildDestination(srcPath, res.category, when);
		dest = _router.ResolveCollision(dest);

		// 乾跑
		if (ChkDryRun.IsChecked == true || _cfg.App.DryRun)
		{
			_db.Upsert(new Item
			{
				Path = dest,
				Filename = Path.GetFileName(dest),
				Category = res.category,
				Confidence = res.confidence,
				CreatedTs = DateTimeOffset.Now.ToUnixTimeSeconds(),
				Summary = res.summary,
				Reasoning = res.reasoning,
				Status = "normal"
			});
			return;
		}

		Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
		var overwrite = _cfg.App.Overwrite.Equals("overwrite", StringComparison.OrdinalIgnoreCase);
		if (_cfg.App.MoveMode.Equals("copy", StringComparison.OrdinalIgnoreCase))
			File.Copy(srcPath, dest, overwrite);
		else
			File.Move(srcPath, dest, overwrite);

		_db.Upsert(new Item
		{
			Path = dest,
			Filename = Path.GetFileName(dest),
			Category = res.category,
			Confidence = res.confidence,
			CreatedTs = DateTimeOffset.Now.ToUnixTimeSeconds(),
			Summary = res.summary,
			Reasoning = res.reasoning,
			Status = "normal"
		});
	}

	/* ---------- 查詢 / 呈現 ---------- */

	private void LoadRecent(int days)
	{
		var items = _db.Recent(days).ToList();
		BindList(items);
	}

	private void LoadByStatus(string status)
	{
		var items = _db.ByStatus(status).ToList();
		BindList(items);
	}

	private void DoSearch()
	{
		var kw = (SearchBox.Text ?? string.Empty).Trim();
		var items = kw.Length == 0 ? _db.Recent(7).ToList() : _db.Search(kw).ToList();
		BindList(items);
	}

	private void BindList(IEnumerable<Item> items)
	{
		// 讓日期顯示為 yyyy-MM-dd（前端 GridView 用字串就好）
		var list = items.Select(it => new
		{
			it.Filename,
			it.Category,
			Confidence = it.Confidence.ToString("0.00"),
			CreatedTs = DateTimeOffset.FromUnixTimeSeconds(it.CreatedTs)
								   .ToLocalTime()
								   .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
		}).ToList();

		ListFiles.ItemsSource = list;
	}

	private void BuildCategoryTree()
	{
		TreeCategories.Items.Clear();
		var root = new TreeViewItem { Header = "全部分類" };
		foreach (var c in _db.GetCategories())
			root.Items.Add(new TreeViewItem { Header = c });
		TreeCategories.Items.Add(root);
		root.IsExpanded = true;
	}
}
