using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// V19.0 (V18.1 回滾 P2)
    /// 1. (V18.1) 保留 'TopDirectoryOnly' (V17.1 P1 修正) [Line 116]。
    /// 2. [V19.0 修正 CS0104] (V18.1) 'System.IO.Path' (CS0104 修正) [Line 181]。
    /// 3. [V19.0 回滾 P2] 移除 V18.0 (V17.1 P2 需求) [cite: `Services/HotFolderService.cs (V18.0)` Line 149] 的 'EnumerateDirectories' (掃描資料夾) 邏輯。
    /// 4. [V19.0 回滾 P2] (V18.0) 'IntakeItemsAsync' [cite: `Services/HotFolderService.cs (V18.0)` Line 206] 
    ///    回滾為 (V17.0) 'IntakeFilesAsync' [Line 204]。
    /// 5. [V19.0 修正 CS1061] [Line 185] 移除了對 V19.0 'Item.IsFolder' [cite: `Models/Item.cs (V19.0)` Line 108] (已刪除) 的引用。
    /// </summary>
    public sealed class HotFolderService : IDisposable
    {
        private readonly IntakeService _intake;
        private readonly DbService _db;
        private Timer? _timer;
        private bool _isScanning = false;
        private readonly object _lock = new object();

        // (V17.0)
        public event Action? FilesChanged;

        // (V16.0)
        private List<string> _scanPaths = new List<string>();
        // (V18.0) 
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
                _timer = new Timer(async (_) => await ScanAsync(), null, 2000, 2000);
            }
        }

        public async Task TriggerManualSync()
        {
            await ScanAsync();
        }


        private void OnConfigChanged(AppConfig config)
        {
            LoadConfig(config);
        }

        private void LoadConfig(AppConfig cfg)
        {
            lock (_lock)
            {
                // (V16.0) V13.0 (方案 C) [cite: `Models/AppConfig.cs (V13.0)`]
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

                // (V18.0) 

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

        public async Task ScanAsync()
        {
            // (V16.0)
            List<string> currentScanPaths;
            HashSet<string> currentBlacklistExts;
            HashSet<string> currentBlacklistFolders;

            lock (_lock)
            {
                currentScanPaths = _scanPaths.ToList();
                // (V18.0)
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
                // [V18.1 修正 P1] 強制 'TopDirectoryOnly' (非遞迴)
                var searchOption = SearchOption.TopDirectoryOnly;

                // [V19.0 回滾 P2] 
                var allFilesOnDisk = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var scanPath in currentScanPaths)
                {
                    if (string.IsNullOrWhiteSpace(scanPath) || !Directory.Exists(scanPath))
                    {
                        continue;
                    }

                    // 1. 取得檔案 (V17.0)
                    foreach (var f in Directory.EnumerateFiles(scanPath, "*.*", searchOption))
                    {
                        var fi = new FileInfo(f);
                        if (currentBlacklistExts.Contains(fi.Extension.TrimStart('.'))) continue;

                        bool inBlacklistFolder = false;
                        var dir = fi.Directory;
                        // (V17.0)
                        while (dir != null && dir.FullName.Length > scanPath.Length)
                        {
                            if (currentBlacklistFolders.Contains(dir.Name))
                            {
                                inBlacklistFolder = true;
                                break;
                            }
                            dir = dir.Parent;
                        }
                        if (inBlacklistFolder) continue;

                        allFilesOnDisk[fi.FullName] = fi.FullName; // [V19.0]
                    }

                    // 2. [V19.0 回滾 P2] 移除 V18.0 [cite: `Services/HotFolderService.cs (V18.0)` Line 149] 的 'EnumerateDirectories'
                }


                // 3. 取得資料庫中已存在的所有檔案 (V16.0)
                var allItemsInDb = await _db.QueryAllAsync();

                // [V19.0 修正 CS0104] (V18.1)
                var managedPathRoots = new HashSet<string>(currentScanPaths.Select(System.IO.Path.GetFullPath), StringComparer.OrdinalIgnoreCase);

                var dbFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var dbItemsInManagedFolders = new List<Item>();

                foreach (var item in allItemsInDb)
                {
                    // [V19.0 修正 CS1061] 
                    // 移除 V18.1 [cite: `Services/HotFolderService.cs (V18.1)` Line 185] '|| item.IsFolder'
                    if (string.IsNullOrWhiteSpace(item.Path)) continue;

                    bool isManaged = false;
                    try
                    {
                        // [V19.0 修正 CS0104] (V18.1)
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
                // [V19.0 回滾 P2] 
                var newFiles = new List<string>();
                foreach (var diskItem in allFilesOnDisk)
                {
                    if (!dbFiles.Contains(diskItem.Key))
                    {
                        newFiles.Add(diskItem.Value);
                    }
                }

                if (newFiles.Count > 0)
                {
                    // [V19.0 回滾 P2] 
                    await _intake.IntakeFilesAsync(newFiles);
                    dataChanged = true;
                }

                // 5. [Delete] 找出 (磁碟 X, DB O) 且非 "committed" 的舊項目

                // (V16.0)

                var missingIds = new List<string>();
                foreach (var item in dbItemsInManagedFolders)
                {
                    if (string.IsNullOrWhiteSpace(item.Id) || item.Status == "committed") continue;

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