using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Views; // 為了存取 MainWindow

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// V7.33.4 修正 (崩潰修正)：
    /// 1. 修正 OnTimerElapsed 中的 SynchronizationLockException 錯誤。
    ///    將 Monitor (object) 鎖 替換為 SemaphoreSlim(1, 1)。
    ///    SemaphoreSlim 天生支援 async/await，允許 await 跨線程後正確釋放鎖。
    ///    這將解決「一次加入多筆檔案」時的 App 崩潰問題。
    /// </summary>
    public class HotFolderService : IDisposable
    {
        private readonly IntakeService _intake;
        private readonly RoutingService _router;

        private FileSystemWatcher? _watcher;
        private AppConfig _cfg = ConfigService.Cfg; // 快取目前設定
        private readonly object _lock = new object(); // 用於 FSW 初始化

        // V7.33.4 修正：使用 SemaphoreSlim 替代 object _scanLock
        private readonly SemaphoreSlim _scanSemaphore = new SemaphoreSlim(1, 1);

        private Timer? _debounceTimer;

        public HotFolderService(IntakeService intake, RoutingService router)
        {
            _intake = intake;
            _router = router;
        }

        public void StartMonitoring()
        {
            _debounceTimer = new Timer(OnTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);

            ConfigService.ConfigChanged += OnConfigChanged;
            InitializeWatcher(ConfigService.Cfg);
            Log("HotFolderService 已啟動 (V7.33.4 Mirroring-Sync)。"); // V7.33.4
        }

        public void StopMonitoring()
        {
            lock (_lock)
            {
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Created -= OnFileChanged;
                    _watcher.Deleted -= OnFileChanged;
                    _watcher.Renamed -= OnFileChanged;
                    _watcher.Dispose();
                    _watcher = null;
                    Log("HotFolder 監控已停止。");
                }

                _debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _debounceTimer?.Dispose();
                _debounceTimer = null;
            }
        }

        private void OnConfigChanged(object? sender, AppConfig cfg)
        {
            Log("偵測到設定變更，正在重新初始化 HotFolder 監控器...");
            InitializeWatcher(cfg);
        }

        private void InitializeWatcher(AppConfig cfg)
        {
            lock (_lock)
            {
                _cfg = cfg;
                StopMonitoring();

                _debounceTimer = new Timer(OnTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);

                if (cfg.Import?.EnableHotFolder != true)
                {
                    Log("HotFolder 監控未啟用 (EnableHotFolder: false)。");
                    return;
                }

                var hotPath = cfg.Import.HotFolder;
                if (string.IsNullOrWhiteSpace(hotPath))
                {
                    Log("HotFolder 監控失敗：路徑未設定 (HotFolder: '')。");
                    return;
                }

                if (!Directory.Exists(hotPath))
                {
                    try { Directory.CreateDirectory(hotPath); Log($"HotFolder 目錄不存在，已自動建立：{hotPath}"); }
                    catch (Exception ex) { Log($"HotFolder 監控失敗：無法建立目錄 {hotPath}。錯誤: {ex.Message}"); return; }
                }

                try
                {
                    _watcher = new FileSystemWatcher(hotPath)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                        IncludeSubdirectories = cfg.Import?.IncludeSubdir == true
                    };

                    _watcher.Created += OnFileChanged;
                    _watcher.Deleted += OnFileChanged;
                    _watcher.Renamed += OnFileChanged;

                    _watcher.EnableRaisingEvents = true;
                    Log($"HotFolder 監控已啟動，路徑：{hotPath}");

                    Log("執行啟動時的首次掃描...");
                    _debounceTimer.Change(500, Timeout.Infinite); // 0.5 秒後執行
                }
                catch (Exception ex)
                {
                    Log($"HotFolder 監控啟動失敗：{ex.Message}");
                }
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            Log($"[DIAG:WATCHER] Raw Event Fired ({e.ChangeType}): {e.FullPath}");
            if (string.IsNullOrWhiteSpace(e.FullPath)) return;
            _debounceTimer?.Change(dueTime: 2000, period: Timeout.Infinite);
        }

        // V7.33.4 修正：
        // OnTimerElapsed (async void) 現在使用 SemaphoreSlim.WaitAsync(0)
        // 確保 async/await 流程中的線程安全
        private async void OnTimerElapsed(object? state)
        {
            // 1. 嘗試非阻塞地獲取信號燈 (Timeout: 0)
            if (!await _scanSemaphore.WaitAsync(0))
            {
                Log("[DIAG] 掃描被跳過 (上次掃描仍在進行中)。");
                return; // 獲取失敗，上次任務仍在執行
            }

            try
            {
                // 2. 在 Lock 內部執行並等待 Async 任務
                await ScanAndSyncHotFolderAsync();
            }
            catch (Exception ex)
            {
                // 捕獲來自 ScanAndSyncHotFolderAsync 的未處理異常
                Log($" -> 處理 HotFolder 鏡像同步失敗 (OnTimerElapsed)。錯誤: {ex.Message}");
            }
            finally
            {
                // 3. 確保釋放信號燈
                _scanSemaphore.Release();
            }
        }

        /// <summary>
        /// (V7.33.3) 包含 V7.32 鏡像同步 (Mirroring Sync) 邏輯的 Async Task
        /// (此函數內部邏輯在 V7.33.4 中保持不變)
        /// </summary>
        private async Task ScanAndSyncHotFolderAsync()
        {
            // --- 取得鎖後才執行的邏輯 ---
            string hotPath;
            SearchOption searchOption;
            HashSet<string> blacklistExts;

            lock (_lock) // 讀取 _cfg (這個鎖 _lock 和 _scanLock 不同，是安全的)
            {
                if (_watcher == null || !_watcher.EnableRaisingEvents) return; // 監控已停止
                hotPath = _watcher.Path;
                searchOption = _cfg.Import?.IncludeSubdir == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                blacklistExts = _cfg.Routing?.BlacklistExts?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();
            }

            Log("計時器觸發：開始鏡像同步 (Mirror-Sync) HotFolder...");
            bool needsRefresh = false;

            // 1. 取得磁碟上的所有檔案 (Disk Files)
            var diskFiles = await Task.Run(() => Directory.GetFiles(hotPath, "*.*", searchOption));
            var diskFileSet = new HashSet<string>(diskFiles, StringComparer.OrdinalIgnoreCase);
            Log($"[Sync] 磁碟掃描：偵測到 {diskFileSet.Count} 個實體檔案。");

            // 2. 取得資料庫中的所有項目 (DB Items)
            var allDbItems = await Task.Run(() => _intake.QueryAllAsync());

            // 3. 將 DB 項目分類：收件夾 (intaked) vs. 已提交 (committed)
            var inboxDbItems = new List<Item>();
            var committedDbPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in allDbItems)
            {
                if (item.Status == "committed")
                {
                    committedDbPaths.Add(item.Path);
                }
                else
                {
                    inboxDbItems.Add(item);
                }
            }
            var inboxDbPathSet = new HashSet<string>(inboxDbItems.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
            Log($"[Sync] 資料庫掃描： {inboxDbItems.Count} 筆 '收件夾' 項目， {committedDbPaths.Count} 筆 '已提交' 項目。");

            // --- S_R (Sync/Reconciliation) ---

            // 4. [ADD] 找出磁碟上有，但 DB 沒有的檔案
            var filesToAdd = new List<string>();
            foreach (var diskPath in diskFileSet)
            {
                if (!inboxDbPathSet.Contains(diskPath) && !committedDbPaths.Contains(diskPath))
                {
                    var ext = Path.GetExtension(diskPath).TrimStart('.').ToLowerInvariant();
                    if (blacklistExts.Contains(ext)) continue;
                    if (IsFileLocked(diskPath))
                    {
                        Log($" -> [Sync-Add] 檔案 {Path.GetFileName(diskPath)} 仍被鎖定，跳過。");
                        continue;
                    }
                    filesToAdd.Add(diskPath);
                }
            }

            // 5. [DELETE] 找出 DB (intaked) 有，但磁碟上沒有的檔案
            var itemsToDelete = inboxDbItems.Where(item => !diskFileSet.Contains(item.Path)).ToList();
            var idsToDelete = itemsToDelete.Select(i => i.Id!).ToList();

            Log($"[Sync] 比較：待新增 {filesToAdd.Count} 筆，待刪除 {itemsToDelete.Count} 筆。");

            // 6. 執行資料庫操作 (ADD)
            if (filesToAdd.Count > 0)
            {
                var newItems = new List<Item>();
                var now = DateTime.UtcNow;
                foreach (var path in filesToAdd)
                {
                    var fi = new FileInfo(path);
                    newItems.Add(new Item
                    {
                        Path = fi.FullName,
                        ProposedPath = string.Empty,
                        CreatedAt = now,
                        UpdatedAt = now,
                        Tags = new List<string>(),
                        Status = "intaked",
                    });
                }
                await Task.Run(() => _intake.InsertItemsAsync(newItems));
                Log($" -> [Sync-Add] {newItems.Count} 個項目已批次寫入資料庫。");

                foreach (var item in newItems)
                {
                    item.ProposedPath = _router.PreviewDestPath(item.Path);
                }
                await Task.Run(() => _intake.UpdateItemsAsync(newItems));
                Log($" -> [Sync-Add] {newItems.Count} 個項目的 ProposedPath 已更新。");
                needsRefresh = true;
            }

            // 7. 執行資料庫操作 (DELETE)
            if (itemsToDelete.Count > 0)
            {
                await Task.Run(() => _intake.DeleteItemsAsync(idsToDelete));
                Log($" -> [Sync-Delete] {itemsToDelete.Count} 個 '收件夾' 項目已從資料庫移除。");
                needsRefresh = true;
            }

            // 8. 刷新 UI (僅在有變更時執行一次)
            if (needsRefresh)
            {
                await RefreshMainWindowAsync();
                Log($"[Sync] 鏡像同步完成，UI 已刷新。");
            }
            else
            {
                Log($"[Sync] 鏡像同步完成，無需變更。");
            }
        }


        // V7.31: 從 V7.4 複製 IsFileLocked 輔助函數
        private bool IsFileLocked(string filePath)
        {
            try
            {
                using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    stream.Close();
                }
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            return false;
        }

        // === UI 互動輔助 (V7.4 保持不變) ===
        private void Log(string message)
        {
            try
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (Application.Current.MainWindow is Views.MainWindow mw)
                    {
                        mw.Log($"[HotFolder] {message}");
                    }
                });
            }
            catch (TaskCanceledException) { }
        }

        private async Task RefreshMainWindowAsync()
        {
            try
            {
                if (Application.Current?.Dispatcher == null) return;

                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (Application.Current.MainWindow is Views.MainWindow mw)
                    {
                        await mw.RefreshFromDbAsync();
                    }
                });
            }
            catch (TaskCanceledException) { }
        }

        // === 保留舊版方法 (V7.2) ===
        // V7.4 修正：轉呼叫 _intake
        public Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage) => _intake.StageOnlyAsync(items, stage);
        public Task<int> StageOnlyAsync(Item item, int stage) => _intake.StageOnlyAsync(new List<Item> { item }, stage);

        // V7.4 新增：實現 IDisposable
        public void Dispose()
        {
            ConfigService.ConfigChanged -= OnConfigChanged;
            StopMonitoring();
            _scanSemaphore?.Dispose(); // V7.33.4 新增
            _debounceTimer?.Dispose(); // V7.33.4 修正
        }
    }
}