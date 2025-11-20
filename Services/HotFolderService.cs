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
    /// V20.6 (掃描範圍修正版)
    /// 1. [V20.3] 'ScanAsync' 接受 'overrideMode' 參數。
    /// 2. [V20.6] 'ScanAsync' 新增 'scanOnlyHotFolder' 參數。
    /// 3. [V20.6] 'ScanAsync' (手動) 現在只會掃描 HotFolder。
    /// 4. [V20.6] 'Timer' (背景) 仍會掃描所有路徑 (scanOnlyHotFolder: false)。
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
        private string _hotFolderPath = string.Empty; // [V20.6] 快取 HotFolder 路徑
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
                // [V20.6] 背景 Timer 應掃描所有路徑 (scanOnlyHotFolder: false)
                _timer = new Timer(async (_) => await ScanAsync(null, false), null, 2000, 2000);
            }
        }

        /// <summary>
        /// (V20.0 舊版) 觸發手動同步。
        /// [V20.6] UI 應改為直接呼叫 ScanAsync(mode, true)。
        /// </summary>
        public async Task TriggerManualSync()
        {
            await ScanAsync(SearchOption.AllDirectories, true);
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
                    var hotPath = cfg.Import.HotFolder.Trim();
                    paths.Add(hotPath);
                    _hotFolderPath = hotPath; // [V20.6] 快取
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
        /// [V20.6] 執行掃描
        /// </summary>
        /// <param name="overrideMode">如果提供 (來自 UI)，則使用此掃描模式。如果為 null (來自 Timer)，則使用預設值。</param>
        /// <param name="scanOnlyHotFolder">如果為 true，則忽略 _scanPaths，只掃描 _hotFolderPath。</param>
        public async Task ScanAsync(SearchOption? overrideMode = null, bool scanOnlyHotFolder = false)
        {
            List<string> pathsToScan;
            HashSet<string> currentBlacklistExts;
            HashSet<string> currentBlacklistFolders;
            List<string> managedRoots; // [V20.6] 用於 DB 清理的根路徑

            lock (_lock)
            {
                // [V20.6] 決定掃描範圍
                if (scanOnlyHotFolder)
                {
                    pathsToScan = new List<string> { _hotFolderPath };
                    // DB 清理時，也只管理 HotFolder
                    managedRoots = new List<string> { System.IO.Path.GetFullPath(_hotFolderPath) };
                }
                else
                {
                    pathsToScan = _scanPaths.ToList();
                    // DB 清理時，管理所有路徑
                    managedRoots = _scanPaths.Select(System.IO.Path.GetFullPath).ToList();
                }

                currentBlacklistExts = _blacklistExts;
                currentBlacklistFolders = _blacklistFolders;
            }

            if (pathsToScan.Count == 0 || pathsToScan.All(string.IsNullOrWhiteSpace))
            {
                return;
            }

            if (_isScanning) return;
            lock (_lock) { _isScanning = true; }

            bool dataChanged = false;

            try
            {
                var searchOption = overrideMode ?? SearchOption.AllDirectories;

                var allFilesOnDisk = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

                foreach (var scanPath in pathsToScan)
                {
                    if (string.IsNullOrWhiteSpace(scanPath) || !Directory.Exists(scanPath))
                    {
                        continue;
                    }

                    // 1. 取得檔案
                    foreach (var f in Directory.EnumerateFiles(scanPath, "*.*", searchOption))
                    {
                        var fi = new FileInfo(f);
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

                        allFilesOnDisk[fi.FullName] = isBlacklisted;
                    }
                }


                // 3. 取得資料庫中已存在的所有檔案
                var allItemsInDb = await _db.QueryAllAsync();

                // [V20.6] 使用 managedRoots
                var managedPathRoots = new HashSet<string>(managedRoots, StringComparer.OrdinalIgnoreCase);

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
                    await _intake.IntakeFilesAsync(newFiles);
                    dataChanged = true;
                }

                // 5. [Delete] 找出 (磁碟 X, DB O) 且非 "committed" 的舊項目
                var missingIds = new List<string>();
                foreach (var item in dbItemsInManagedFolders)
                {
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