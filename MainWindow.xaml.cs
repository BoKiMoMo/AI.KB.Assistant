using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant
{
	public partial class MainWindow : Window
	{
		private AppConfig _cfg = new();
		private DbService _db;
		private LlmService _llm;

		public MainWindow()
		{
			InitializeComponent();

			// 載入設定
			_cfg = ConfigService.TryLoad("config.json");
			_db = new DbService(_cfg.App.DbPath);
			_llm = new LlmService(_cfg);

			// UI 初始化
			if (ChkDryRun != null) ChkDryRun.IsChecked = _cfg.App.DryRun;

			AddLog("應用程式就緒。把檔案拖進中央區塊即可分類並寫入資料庫。");
		}

		/* ================== 核心：拖拉收件處理 ================== */
		private async void DropInbox(object sender, DragEventArgs e)
		{
			if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
			var files = (string[])e.Data.GetData(DataFormats.FileDrop);

			int ok = 0, fail = 0;
			foreach (var f in files)
			{
				try
				{
					await ProcessOneAsync(f);
					ok++;
				}
				catch (Exception ex)
				{
					fail++;
					AddLog($"❌ {Path.GetFileName(f)} 失敗：{ex.Message}");
				}
			}
			AddLog($"📦 完成：成功 {ok} 份 / 失敗 {fail} 份");
		}

		/// <summary>處理單一檔案：分類 → 寫 DB → 顯示</summary>
		private async Task ProcessOneAsync(string srcPath)
		{
			using var cts = new CancellationTokenSource();

			// 目前先用檔名（不含副檔名）當成要分類的文字；之後可換成 OCR/ASR/全文
			var text = Path.GetFileNameWithoutExtension(srcPath);

			// 呼叫分類（LlmService 內若未接 OpenAI，會以關鍵字規則回傳）
			var (cat, conf, summary, reason) = await _llm.ClassifyAsync(text, cts.Token);

			var item = new Item
			{
				Path = srcPath,
				Filename = Path.GetFileName(srcPath),
				Category = cat,
				Confidence = conf,
				Summary = summary,
				Reasoning = reason,
				CreatedTs = DateTimeOffset.Now.ToUnixTimeSeconds(),
				Status = "normal",
				Tags = ""
			};

			// 乾跑：僅顯示，不寫 DB
			if (ChkDryRun?.IsChecked == true || _cfg.App.DryRun)
			{
				ListFiles?.Items.Add($"[DRY RUN] {item.Filename} — [{item.Category}] — {item.Confidence:P0}");
				AddLog($"(乾跑) {item.Filename} → {item.Category}");
				return;
			}

			// 寫 DB
			_db.Add(item);

			// 顯示在清單
			ListFiles?.Items.Add($"{item.Filename} — [{item.Category}] — {item.Confidence:P0}");
			AddLog($"✅ 已分類 {item.Filename} → {item.Category}（{item.Confidence:P0}）");
		}

		/* ================== 共用：簡易日誌 ================== */
		private void AddLog(string msg)
		{
			var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
			// 這裡直接把訊息丟到左下清單；若你有 TxtLog 可改寫到文字框
			ListFiles?.Items.Add(line);
		}

		/* ================== 事件：按鈕/搜尋 ================== */
		private void BtnSearch_Click(object sender, RoutedEventArgs e) => DoSearch();

		private void SearchBox_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Enter) DoSearch();
		}

		private void DoSearch()
		{
			var kw = (SearchBox?.Text ?? string.Empty).Trim();
			if (string.IsNullOrEmpty(kw))
			{
				AddLog("搜尋關鍵字為空。");
				return;
			}
			// 目前先做最小實作：只顯示訊息避免編譯錯誤
			AddLog($"🔎（示意）搜尋「{kw}」功能尚未接 DB 檢索。");
		}

		private void BtnRecent_Click(object sender, RoutedEventArgs e)
		{
			// 目前先清空並提示（避免依賴尚未實作的 DbService 查詢）
			ListFiles?.Items.Clear();
			AddLog("🆕（示意）最近新增：尚未實作資料查詢。");
		}

		private void BtnPending_Click(object sender, RoutedEventArgs e)
			=> AddLog("👉 點擊了【待處理】（尚未實作）");

		private void BtnProgress_Click(object sender, RoutedEventArgs e)
			=> AddLog("👉 點擊了【執行中】（尚未實作）");

		private void BtnTodo_Click(object sender, RoutedEventArgs e)
			=> AddLog("👉 點擊了【代辦】（尚未實作）");

		private void BtnConfirmMove_Click(object sender, RoutedEventArgs e)
			=> AddLog("🟢 一鍵確認並搬檔（尚未實作）");

		private void BtnSettings_Click(object sender, RoutedEventArgs e)
		{
			// 開設定視窗（若你尚未加入 SettingsWindow，可以維持此日誌避免編譯錯）
			try
			{
				var win = new AI.KB.Assistant.Views.SettingsWindow("config.json") { Owner = this };
				if (win.ShowDialog() == true)
				{
					// 重新載入設定
					var oldDry = _cfg.App.DryRun;
					_cfg = ConfigService.TryLoad("config.json");
					if (ChkDryRun != null) ChkDryRun.IsChecked = _cfg.App.DryRun;

					// 若 DB 路徑/設定有變，重建服務
					_db?.Dispose();
					_db = new DbService(_cfg.App.DbPath);
					_llm = new LlmService(_cfg);

					AddLog("⚙️ 設定已儲存並重新載入。");
					if (oldDry != _cfg.App.DryRun)
						AddLog($"[設定] 乾跑模式改為：{_cfg.App.DryRun}");
				}
			}
			catch
			{
				AddLog("⚙️（示意）設定視窗尚未加入，本次略過。");
			}
		}
	}
}