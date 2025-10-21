using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 監看「收件夾」的檔案變化，當偵測到新檔或搬入檔案，會依設定：
    /// 1) 先寫入資料庫 (stage)
    /// 2) 若啟用 AutoClassifyOnDrop，再進行一次「預分類」
    /// 
    /// 具備：
    /// - Debounce：多次變更事件合併處理
    /// - Wait until stable：等待寫入穩定才處理
    /// - 黑名單資料夾自動忽略
    /// - 可選擇是否監看子資料夾
    /// </summary>
    public sealed class HotFolderService : IDisposable
    {
        private readonly IntakeService _intake;
        private readonly AppConfig _cfg;

        private FileSystemWatcher? _watcher;
        private readonly ConcurrentDictionary<string, DateTime> _pending = new();
        private CancellationTokenSource? _cts;
        private Task? _pumpTask;

        private string _inbox = "";
        private string _black = "";
        private bool _isRunning;

        /// <summary>可選的 UI Log 回呼（不綁定 UI）。</summary>
        public Action<string>? OnLog { get; set; }

        /// <summary>目前監看的收件夾路徑。</summary>
        public string InboxPath => _inbox;

        /// <summary>是否正在執行中。</summary>
        public bool IsRunning => _isRunning;

        public HotFolderService(IntakeService intake, AppConfig cfg)
        {
            _intake = intake ?? throw new ArgumentNullException(nameof(intake));
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            ResolvePaths();
        }

        private void ResolvePaths()
        {
            var root = string.IsNullOrWhiteSpace(_cfg.App.RootDir)
                ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : _cfg.App.RootDir;

            _inbox = string.IsNullOrWhiteSpace(_cfg.Import.HotFolderPath)
                ? Path.Combine(root, "_Inbox")
                : _cfg.Import.HotFolderPath;

            _black = Path.Combine(_inbox,
                string.IsNullOrWhiteSpace(_cfg.Import.BlacklistFolderName) ? "_blacklist" : _cfg.Import.BlacklistFolderName);
        }

        /// <summary>建立必要資料夾。</summary>
        private void EnsureFolders()
        {
            try
            {
                if (!Directory.Exists(_inbox)) Directory.CreateDirectory(_inbox);
                if (!Directory.Exists(_black)) Directory.CreateDirectory(_black);
            }
            catch (Exception ex)
            {
                Log($"建立收件夾失敗：{ex.Message}");
            }
        }

        public void Start()
        {
            if (_isRunning) return;

            EnsureFolders();

            try
            {
                _watcher = new FileSystemWatcher(_inbox, "*.*")
                {
                    IncludeSubdirectories = _cfg.Import.IncludeSubdirectories,
                    EnableRaisingEvents = true,
                    NotifyFilter =
                        NotifyFilters.FileName |
                        NotifyFilters.LastWrite |
                        NotifyFilters.CreationTime |
                        NotifyFilters.Size
                };

                _watcher.Created += Watcher_OnCreatedOrChanged;
                _watcher.Changed += Watcher_OnCreatedOrChanged;
                _watcher.Renamed += Watcher_OnRenamed;

                _cts = new CancellationTokenSource();
                _pumpTask = Task.Run(() => PumpAsync(_cts.Token));

                _isRunning = true;
                Log($"HotFolder 已啟動：{_inbox}（含子資料夾：{_cfg.Import.IncludeSubdirectories}）");
            }
            catch (Exception ex)
            {
                Log($"HotFolder 啟動失敗：{ex.Message}");
                Stop();
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;

            try { _watcher?.Dispose(); } catch { }
            _watcher = null;

            try { _cts?.Cancel(); } catch { }
            try { _pumpTask?.Wait(500); } catch { }
            _cts = null;
            _pumpTask = null;

            _pending.Clear();
            _isRunning = false;
            Log("HotFolder 已停止。");
        }

        private void Watcher_OnCreatedOrChanged(object sender, FileSystemEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.FullPath)) return;
            if (Directory.Exists(e.FullPath)) return; // 只處理檔案
            if (ShouldIgnore(e.FullPath)) return;

            _pending[e.FullPath] = DateTime.UtcNow;
        }

        private void Watcher_OnRenamed(object sender, RenamedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.OldFullPath))
                _pending.TryRemove(e.OldFullPath, out _);

            if (string.IsNullOrWhiteSpace(e.FullPath)) return;
            if (Directory.Exists(e.FullPath)) return;
            if (ShouldIgnore(e.FullPath)) return;

            _pending[e.FullPath] = DateTime.UtcNow;
        }

        private async Task PumpAsync(CancellationToken ct)
        {
            // 多少 ms 內無新變更就視為穩定
            const int idleMs = 800;
            const int sleepMs = 300;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    var batch = _pending
                        .Where(kv => (now - kv.Value).TotalMilliseconds >= idleMs)
                        .Select(kv => kv.Key)
                        .ToList();

                    foreach (var path in batch)
                    {
                        _pending.TryRemove(path, out _);
                        await HandleFileAsync(path, ct);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Log($"HotFolder 處理循環錯誤：{ex.Message}");
                }

                try { await Task.Delay(sleepMs, ct); } catch { }
            }
        }

        private async Task HandleFileAsync(string path, CancellationToken ct)
        {
            try
            {
                // 黑名單內的一律忽略
                if (IsInBlacklist(path))
                {
                    Log($"忽略黑名單：{Path.GetFileName(path)}");
                    return;
                }

                // 等到檔案寫入穩定
                bool stable = await WaitFileStableAsync(path, ct);
                if (!stable)
                {
                    Log($"檔案無法穩定或不存在，略過：{Path.GetFileName(path)}");
                    return;
                }

                // 先 Stage
                await _intake.StageOnlyAsync(path, ct);
                Log($"已加入 Inbox：{Path.GetFileName(path)}");

                // 視設定決定是否立即做預分類
                if (_cfg.Import.AutoClassifyOnDrop)
                {
                    try
                    {
                        await _intake.ClassifyOnlyAsync(path, ct);
                        Log($"已預分類：{Path.GetFileName(path)}");
                    }
                    catch (Exception ex)
                    {
                        Log($"預分類失敗（略過）：{ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log($"處理檔案失敗：{Path.GetFileName(path)}，{ex.Message}");
            }
        }

        private bool IsInBlacklist(string path)
        {
            try
            {
                // 確保用完整路徑比較
                var f = Path.GetFullPath(path);
                var b = Path.GetFullPath(_black);
                return f.StartsWith(b, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static readonly string[] _tempExts =
        {
            ".tmp", ".crdownload", ".part"
        };

        private static bool ShouldIgnore(string fullPath)
        {
            var name = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(name)) return true;

            // Office/設計常見暫存
            if (name.StartsWith("~$", StringComparison.Ordinal)) return true;

            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            if (_tempExts.Contains(ext)) return true;

            // 隱藏檔/系統檔
            try
            {
                var attr = File.GetAttributes(fullPath);
                if ((attr & FileAttributes.System) != 0) return true;
            }
            catch { }

            return false;
        }

        /// <summary>
        /// 等待檔案大小在多次取樣間無變化，才視為可讀取。
        /// </summary>
        private static async Task<bool> WaitFileStableAsync(string path, CancellationToken ct,
                                                            int maxTries = 10, int delayMs = 200)
        {
            try
            {
                if (!File.Exists(path)) return false;

                long prev = -1;
                for (int i = 0; i < maxTries; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    long size = 0;
                    try
                    {
                        var fi = new FileInfo(path);
                        size = fi.Length;
                    }
                    catch { /* ignore */ }

                    if (size > 0 && size == prev)
                        return true;

                    prev = size;
                    await Task.Delay(delayMs, ct);
                }

                return false;
            }
            catch { return false; }
        }

        private void Log(string msg)
        {
            try { OnLog?.Invoke(msg); } catch { }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
