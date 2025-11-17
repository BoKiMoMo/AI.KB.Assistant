using AI.KB.Assistant.Models;
using AI.KB.Assistant.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AI.KB.Assistant.Services.IntakeService;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// V20.2 (手動掃描模式版)
    /// 1. [V20.1] 'ScanAsync' 傳遞 'FileIntakeInfo' (包含 'isBlacklisted' 旗標)。
    /// 2. [V20.2] 'ScanAsync' 現在接受 'overrideMode' 參數，允許 UI 決定掃描深度。
    /// 3. [V20.2] 背景計時器 'Timer' 預設使用 'null' (即 AllDirectories)，維持遞迴掃描。
    /// </summary>
    public sealed class HotFolderService : IDisposable
    {
        private readonly IntakeService _intake;
        private readonly DbService _db;
        private Timer? _timer;
        private bool _isScanning = false;
        private readonly object _lock = new object();

        public event Action? FilesChanged;

        private List<string> _scanPaths = new List<string>();
        private HashSet<string> _blacklistExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _blacklistFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public HotFolderService(IntakeService intake, DbService db)
        {
            _intake = intake;
            _db = db;

            ConfigService.ConfigChanged += OnConfigChanged;
            LoadConfig(ConfigService.Cfg);
        }

        public void StartMonitoring()
        {
            if (_timer == null)
            {
                // [V20.2] 呼叫 ScanAsync() 時不帶參數 (null)，使其使用預設的遞迴模式
                _timer = new Timer(async (_) => await ScanAsync(), null, 2000, 2000);
            }
        }

        /// <summary>
        /// (V20.0 舊版) 觸發手動同步。
        /// [V20.2] 此方法現已過時，UI 應改為直接呼叫 ScanAsync(mode)。
        /// 為了相容性，保留此方法並使其預設為遞迴掃描。
        /// </summary>
        public async Task TriggerManualSync()
        {
            await ScanAsync(SearchOption.AllDirectories);
        }


        private void OnConfigChanged(AppConfig config)
        {
            LoadConfig(config);
        }

        private void LoadConfig(AppConfig cfg)
        {
            lock (_lock)
            {
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(cfg.App?.RootDir))
                {
                    paths.Add(cfg.App.RootDir.Trim());
                }

                if (!string.IsNullOrWhiteSpace(cfg.Import.HotFolder))
                {
                    paths.Add(cfg.Import.HotFolder.Trim());
                }
                if (cfg.App?.TreeViewRootPaths != null)
                {
                    foreach (var p in cfg.App.TreeViewRootPaths)
                    {
                        if (!string.IsNullOrWhiteSpace(p)) paths.Add(p.Trim());
                    }
                }
                _scanPaths = paths.ToList();

                _blacklistExts = new HashSet<string>(
                    cfg.Import.BlacklistExts?.Select(s => s.TrimStart('.')) ?? Enumerable.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase
                );
                _blacklistFolders = new HashSet<string>(
                    cfg.Import.BlacklistFolderNames ?? Enumerable.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase
                );
            }
        }

        /// <summary>
        /// [V20.2] 執行掃描
        /// </summary>
        /// <param name="overrideMode">如果提供 (來自 UI)，則使用此掃描模式。如果為 null (來自 Timer)，則使用預設值。</param>
        public async Task ScanAsync(SearchOption? overrideMode = null)
        {
            List<string> currentScanPaths;
            HashSet<string> currentBlacklistExts;
            HashSet<string> currentBlacklistFolders;

            lock (_lock)
            {
                currentScanPaths = _scanPaths.ToList();
                currentBlacklistExts = _blacklistExts;
                currentBlacklistFolders = _blacklistFolders;
            }

            if (currentScanPaths.Count == 0)
            {
                return;
            }

            if (_isScanning) return;
            lock (_lock) { _isScanning = true; }

            bool dataChanged = false;

            try
            {
                // [V20.2] 
                // 如果 overrideMode 有值 (來自 UI)，則使用它。
                // 如果為 null (來自 Timer)，則預設使用 AllDirectories (解開資料夾)。
                var searchOption = overrideMode ?? SearchOption.AllDirectories;

                // [V20.1] 字典現在儲存 (路徑, 是否為黑名單)
                var allFilesOnDisk = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

                foreach (var scanPath in currentScanPaths)
                {
                    if (string.IsNullOrWhiteSpace(scanPath) || !Directory.Exists(scanPath))
                    {
                        continue;
                    }

                    // 1. 取得檔案
                    foreach (var f in Directory.EnumerateFiles(scanPath, "*.*", searchOption))
                    {
                        var fi = new FileInfo(f);

                        // [V20.1] 檢查是否在黑名單中
                        bool isBlacklisted = false;
                        if (currentBlacklistExts.Contains(fi.Extension.TrimStart('.')))
                        {
                            isBlacklisted = true;
                        }

                        var dir = fi.Directory;
                        while (dir != null && dir.FullName.Length > scanPath.Length)
                        {
                            if (currentBlacklistFolders.Contains(dir.Name))
                            {
                                isBlacklisted = true;
                                break;
                            }
                            dir = dir.Parent;
                        }

                        // [V20.1] 不再 'continue' (跳過)，
                        // 而是將 (路徑, isBlacklisted) 存入字典
                        allFilesOnDisk[fi.FullName] = isBlacklisted;
                    }
                }


                // 3. 取得資料庫中已存在的所有檔案
                var allItemsInDb = await _db.QueryAllAsync();

                var managedPathRoots = new HashSet<string>(currentScanPaths.Select(System.IO.Path.GetFullPath), StringComparer.OrdinalIgnoreCase);

                var dbFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var dbItemsInManagedFolders = new List<Item>();

                foreach (var item in allItemsInDb)
                {
                    if (string.IsNullOrWhiteSpace(item.Path)) continue;

                    bool isManaged = false;
                    try
                    {
                        var fullItemPath = System.IO.Path.GetFullPath(item.Path);
                        if (managedPathRoots.Any(root => fullItemPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
                        {
                            isManaged = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[HotFolderService Error] Invalid item path: {item.Path}. {ex.Message}");
                    }

                    if (isManaged)
                    {
                        dbFiles.Add(item.Path);
                        dbItemsInManagedFolders.Add(item);
                    }
                }


                // 4. [Intake] 找出 (磁碟 O, DB X) 的新檔案
                // [V20.1] 建立新的傳遞模型
                var newFiles = new List<FileIntakeInfo>();
                foreach (var (path, isBlacklisted) in allFilesOnDisk)
                {
                    if (!dbFiles.Contains(path))
                    {
                        newFiles.Add(new FileIntakeInfo { FullPath = path, IsBlacklisted = isBlacklisted });
                    }
                }

                if (newFiles.Count > 0)
                {
                    // [V20.1] 呼叫 IntakeService (V20.1) 的新方法
                    await _intake.IntakeFilesAsync(newFiles);
                    dataChanged = true;
                }

                // 5. [Delete] 找出 (磁碟 X, DB O) 且非 "committed" 的舊項目
                var missingIds = new List<string>();
                foreach (var item in dbItemsInManagedFolders)
                {
                    // [V20.1] 也不刪除 "blacklisted" 的紀錄
                    if (string.IsNullOrWhiteSpace(item.Id) ||
                        item.Status == "committed" ||
                        item.Status == "blacklisted")
                    {
                        continue;
                    }

                    if (!allFilesOnDisk.ContainsKey(item.Path))
                    {
                        missingIds.Add(item.Id);
                    }
                }

                if (missingIds.Count > 0)
                {
                    await _intake.DeleteItemsAsync(missingIds);
                    dataChanged = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HotFolderService Error] {ex.Message}");
                App.LogCrash("HotFolderService.ScanAsync", ex);
            }
            finally
            {
                lock (_lock) { _isScanning = false; }
            }

            if (dataChanged)
            {
                FilesChanged?.Invoke();
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
            ConfigService.ConfigChanged -= OnConfigChanged;
        }
    }
}