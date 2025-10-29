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
    /// 進件流程：
    /// 1) StageOnly：只建立 DB 紀錄（狀態 inbox），不搬檔，供 UI 預覽
    /// 2) ClassifyOnly：只預估目的地與狀態 preview，不搬檔
    /// 3) CommitPending：實際搬檔（依 OverwritePolicy），並更新狀態與最終路徑
    /// 
    /// 提供多組 Overload 以相容舊呼叫（string / IEnumerable<string> / folder + ct 等）。
    /// </summary>
    public class IntakeService
    {
        private readonly DbService _db;
        private readonly RoutingService _routing;
        private readonly LlmService? _llm;         // 有些舊建構式會傳入，這裡保留但目前不強制使用
        private AppConfig _cfg;

        // ───────────────────────── ctor ─────────────────────────

        public IntakeService(DbService db, RoutingService routing, AppConfig cfg)
        {
            _db = db;
            _routing = routing;
            _cfg = cfg ?? new AppConfig();
        }

        /// <summary>相容舊呼叫：第四個參數 LlmService（可為 null）</summary>
        public IntakeService(DbService db, RoutingService routing, AppConfig cfg, LlmService? llm)
            : this(db, routing, cfg)
        {
            _llm = llm;
        }

        public void UpdateConfig(AppConfig cfg)
        {
            _cfg = cfg ?? _cfg;
            _routing.ApplyConfig(_cfg);
        }

        // ────────────────────── StageOnly（暫存） ──────────────────────

        /// <summary>
        /// 暫存多個檔案：寫入或更新 Item，Status="inbox"，並計算預估目的地（DestPath）。
        /// </summary>
        public async Task<int> StageOnlyAsync(IEnumerable<string> files, string? project = null, CancellationToken ct = default)
        {
            if (files == null) return 0;
            int count = 0;

            foreach (var f in files.Where(File.Exists))
            {
                ct.ThrowIfCancellationRequested();

                var it = _db.TryGetByPath(f) ?? new Item
                {
                    SourcePath = f,
                    FileName = Path.GetFileName(f),
                    Ext = Path.GetExtension(f).TrimStart('.').ToLowerInvariant(),
                    Project = project ?? string.Empty,
                    CreatedAt = DateTime.UtcNow
                };

                it.Status = "inbox";
                // 在暫存階段預先計算預估目的地，UI 可直接顯示
                it.DestPath = _routing.PreviewDestPath(it.SourcePath, string.IsNullOrWhiteSpace(project) ? it.Project : project);

                await _db.UpsertAsync(it);
                count++;
            }

            return count;
        }

        /// <summary>
        /// 相容簽章：第二參數原為 CancellationToken 的舊呼叫。
        /// </summary>
        public Task<int> StageOnlyAsync(IEnumerable<string> files, CancellationToken ct)
            => StageOnlyAsync(files, project: null, ct);

        /// <summary>
        /// 相容簽章：傳入單一路徑（可能是檔案或資料夾）。
        /// </summary>
        public Task<int> StageOnlyAsync(string fileOrFolder, CancellationToken ct = default)
        {
            if (Directory.Exists(fileOrFolder))
                return StageOnlyFromFolderAsync(fileOrFolder, includeSubdir: true, ct);
            return StageOnlyAsync(new[] { fileOrFolder }, project: null, ct);
        }

        /// <summary>
        /// 從資料夾掃描暫存；支援是否遞迴與副檔名黑名單。
        /// </summary>
        public Task<int> StageOnlyFromFolderAsync(string folder, bool includeSubdir, CancellationToken ct = default)
        {
            if (!Directory.Exists(folder)) return Task.FromResult(0);

            var opt = includeSubdir ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var all = Directory.GetFiles(folder, "*.*", opt);

            // 副檔名黑名單（Import.BlacklistExts）
            var blacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_cfg.Import?.BlacklistExts != null)
            {
                foreach (var e in _cfg.Import.BlacklistExts)
                    blacklist.Add((e ?? string.Empty).Trim('.').ToLowerInvariant());
            }

            var validFiles = all.Where(p =>
            {
                var ext = Path.GetExtension(p).TrimStart('.').ToLowerInvariant();
                return !blacklist.Contains(ext);
            });

            return StageOnlyAsync(validFiles, project: null, ct);
        }

        /// <summary>相容簽章：舊呼叫 (folder, ct)</summary>
        public Task<int> StageOnlyFromFolderAsync(string folder, CancellationToken ct)
            => StageOnlyFromFolderAsync(folder, includeSubdir: true, ct);

        /// <summary>相容簽章：舊呼叫 (folder, includeSubdir, project, ct) —— project 目前僅在 StageOnlyAsync 內寫入</summary>
        public async Task<int> StageOnlyFromFolderAsync(string folder, bool includeSubdir, string? project, CancellationToken ct)
        {
            if (!Directory.Exists(folder)) return 0;

            var opt = includeSubdir ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var all = Directory.GetFiles(folder, "*.*", opt);

            // 黑名單
            var blacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_cfg.Import?.BlacklistExts != null)
            {
                foreach (var e in _cfg.Import.BlacklistExts)
                    blacklist.Add((e ?? string.Empty).Trim('.').ToLowerInvariant());
            }

            var validFiles = all.Where(p =>
            {
                var ext = Path.GetExtension(p).TrimStart('.').ToLowerInvariant();
                return !blacklist.Contains(ext);
            });

            return await StageOnlyAsync(validFiles, project, ct);
        }

        // ────────────────────── ClassifyOnly（只分類不搬） ──────────────────────

        /// <summary>
        /// 只做分類與預估目的地（狀態改為 "preview"），不搬檔。
        /// </summary>
        public async Task<int> ClassifyOnlyAsync(IEnumerable<string> files, CancellationToken ct = default)
        {
            if (files == null) return 0;
            int count = 0;

            foreach (var f in files.Where(File.Exists))
            {
                ct.ThrowIfCancellationRequested();

                var it = _db.TryGetByPath(f) ?? new Item
                {
                    SourcePath = f,
                    FileName = Path.GetFileName(f),
                    Ext = Path.GetExtension(f).TrimStart('.').ToLowerInvariant(),
                    CreatedAt = DateTime.UtcNow
                };

                it.Status = "preview";
                it.DestPath = _routing.PreviewDestPath(it.SourcePath, it.Project);

                await _db.UpsertAsync(it);
                count++;
            }

            return count;
        }

        /// <summary>相容簽章：單一路徑（檔案或資料夾）</summary>
        public Task<int> ClassifyOnlyAsync(string fileOrFolder, CancellationToken ct = default)
        {
            if (Directory.Exists(fileOrFolder))
            {
                var files = Directory.GetFiles(fileOrFolder, "*.*", SearchOption.AllDirectories);
                return ClassifyOnlyAsync(files, ct);
            }
            return ClassifyOnlyAsync(new[] { fileOrFolder }, ct);
        }

        // ────────────────────── Commit（實際搬檔） ──────────────────────

        /// <summary>
        /// 新簽章（建議用）：明確指定 rootDir / hotFolder / policy。
        /// 將狀態為 "inbox" 的項目實際搬檔，並依 OverwritePolicy 處理碰撞。
        /// </summary>
        public async Task<int> CommitPendingAsync(string rootDir, string hotFolder, OverwritePolicy policy, CancellationToken ct = default)
        {
            int moved = 0;
            var pending = _db.QueryByStatus("inbox").ToList();

            foreach (var it in pending)
            {
                ct.ThrowIfCancellationRequested();

                var src = it.SourcePath;
                if (string.IsNullOrWhiteSpace(src) || !File.Exists(src)) continue;

                var dest = _routing.BuildDestination(rootDir, it);
                var destDir = Path.GetDirectoryName(dest) ?? string.Empty;
                Directory.CreateDirectory(destDir);

                var finalPath = ResolveCollision(dest, policy);
                if (policy == OverwritePolicy.Skip && File.Exists(dest))
                {
                    // skip：標記略過
                    it.Status = "skipped";
                }
                else
                {
                    File.Copy(src, finalPath, overwrite: policy == OverwritePolicy.Replace);
                    it.Status = "done";
                    it.DestPath = finalPath;

                    // 若你希望來源檔案也更新為新位置（視流程需求）
                    it.SourcePath = finalPath;
                }

                await _db.UpsertAsync(it);
                moved++;
            }

            return moved;
        }

        /// <summary>
        /// 舊呼叫相容：使用 AppConfig 與（可選）覆寫策略覆蓋。
        /// </summary>
        public Task<int> CommitPendingAsync(AppConfig cfg, OverwritePolicy? overwritePolicyOverride = null, CancellationToken ct = default)
        {
            var root = cfg.App?.RootDir ?? string.Empty;
            var hot = cfg.Import?.HotFolderPath ?? string.Empty;
            var policy = overwritePolicyOverride ?? cfg.Import?.OverwritePolicy ?? OverwritePolicy.Rename;
            return CommitPendingAsync(root, hot, policy, ct);
        }

        // ────────────────────── Helpers ──────────────────────

        private static string ResolveCollision(string destFullPath, OverwritePolicy policy)
        {
            if (policy == OverwritePolicy.Replace || policy == OverwritePolicy.Skip)
                return destFullPath;

            // Rename 策略：加 (1)(2)...
            var dir = Path.GetDirectoryName(destFullPath) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(destFullPath);
            var ext = Path.GetExtension(destFullPath);

            var i = 1;
            var candidate = destFullPath;
            while (File.Exists(candidate))
                candidate = Path.Combine(dir, $"{name} ({i++}){ext}");

            return candidate;
        }
    }
}
