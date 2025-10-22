using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public sealed class IntakeService : IDisposable
    {
        private readonly DbService _db;
        private readonly RoutingService _routing;
        private readonly LlmService _llm;
        private AppConfig _cfg;

        public IntakeService(DbService db, RoutingService routing, LlmService llm, AppConfig cfg)
        {
            _db = db;
            _routing = routing;
            _llm = llm;
            _cfg = cfg;
        }

        public void UpdateConfig(AppConfig cfg)
        {
            _cfg = cfg;
        }

        public void Dispose() { }

        // 只進入 Inbox（Stage）
        public async Task StageOnlyAsync(string path, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            var fi = new FileInfo(path);
            var item = new Item
            {
                Filename = fi.Name,
                Ext = (fi.Extension ?? "").Trim('.').ToLowerInvariant(),
                Project = _cfg.App.ProjectLock ?? "",
                Category = "",
                Confidence = 0,
                CreatedTs = DateTimeOffset.FromFileTime(fi.CreationTimeUtc.ToFileTimeUtc()).ToUnixTimeSeconds(),
                Status = "inbox",
                Path = fi.FullName,
                Tags = ""
            };

            _db.UpsertItem(item);
            await Task.CompletedTask;
        }

        // 只做分類（不搬檔）
        public async Task ClassifyOnlyAsync(string path, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            var fi = new FileInfo(path);
            var item = _db.QueryByPath(path).FirstOrDefault() ?? new Item
            {
                Filename = fi.Name,
                Ext = (fi.Extension ?? "").Trim('.').ToLowerInvariant(),
                CreatedTs = DateTimeOffset.FromFileTime(fi.CreationTimeUtc.ToFileTimeUtc()).ToUnixTimeSeconds(),
                Path = fi.FullName
            };

            // ↓↓↓ 你的規則判斷（範例）
            item.Category = GuessCategory(item) ?? "";
            item.Confidence = GuessConfidence(item);

            // NEW: 黑名單偵測（副檔名黑名單 或 路徑含黑名單資料夾名）
            var isExtBlack = (_cfg.Import.BlacklistExts ?? Array.Empty<string>())
                                .Any(x => string.Equals(x.Trim('.'), item.Ext, StringComparison.OrdinalIgnoreCase));

            var isPathBlack = false;
            if ((_cfg.Import.BlacklistFolderNames?.Length ?? 0) > 0)
            {
                var parts = (item.Path ?? "").Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                isPathBlack = parts.Any(p => _cfg.Import.BlacklistFolderNames.Contains(p, StringComparer.OrdinalIgnoreCase));
            }

            if (isExtBlack || isPathBlack)
            {
                item.Status = "blacklist"; // ✅ 標記黑名單
            }
            else
            {
                // NEW: 低信心進自整理暫存
                if (item.Confidence < _cfg.Classification.ConfidenceThreshold)
                    item.Status = "auto-staging";
                else
                    item.Status = "pending";
            }

            _db.UpsertItem(item);
            await Task.CompletedTask;
        }

        // 將 pending / auto-staging / blacklist 搬到目的地
        public async Task<int> CommitPendingAsync(CancellationToken ct)
        {
            int moved = 0;
            var candidates = _db.QueryByStatuses(new[] { "pending", "auto-staging", "blacklist" }).ToList();

            foreach (var item in candidates)
            {
                if (ct.IsCancellationRequested) break;
                if (string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path)) continue;

                var isBlacklist = string.Equals(item.Status, "blacklist", StringComparison.OrdinalIgnoreCase);
                var isLowConf = !isBlacklist &&
                                (string.Equals(item.Status, "auto-staging", StringComparison.OrdinalIgnoreCase) ||
                                 item.Confidence < _cfg.Classification.ConfidenceThreshold);

                // 目的地（ROOT/_blacklist 或 ROOT/自整理 或 一般模板）
                var dest = _routing.BuildDestination(item, isBlacklist, isLowConf);

                // 同名策略
                dest = ApplyOverwritePolicy(dest, _cfg.Import.OverwritePolicy);

                // Move/Copy
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    if (_cfg.Import.MoveMode == MoveMode.Move)
                        File.Move(item.Path!, dest, overwrite: _cfg.Import.OverwritePolicy == OverwritePolicy.Replace);
                    else
                        File.Copy(item.Path!, dest, overwrite: _cfg.Import.OverwritePolicy == OverwritePolicy.Replace);

                    item.Path = dest;
                    item.Status = "auto-sorted";
                    _db.UpsertItem(item);
                    moved++;
                }
                catch
                {
                    // 失敗可考慮記錄 item.Status = "error"
                }
            }

            await Task.CompletedTask;
            return moved;
        }

        // ===== 你原本的規則/信心估計：這裡留簡易版範例 =====
        private static string? GuessCategory(Item it)
        {
            var name = (it.Filename ?? "").ToLowerInvariant();
            if (name.Contains("invoice") || name.Contains("發票")) return "財務";
            if (name.Contains("contract") || name.Contains("合約")) return "合約";
            if (name.Contains("spec") || name.Contains("規格")) return "規格";
            if (name.Contains("proposal") || name.Contains("提案")) return "提案";
            return null;
        }

        private static double GuessConfidence(Item it)
        {
            var cat = it.Category ?? "";
            return string.IsNullOrWhiteSpace(cat) ? 0.4 : 0.85;
        }

        private static string ApplyOverwritePolicy(string dest, OverwritePolicy policy)
        {
            if (policy == OverwritePolicy.Replace) return dest;

            if (!File.Exists(dest)) return dest;

            var dir = Path.GetDirectoryName(dest)!;
            var baseName = Path.GetFileNameWithoutExtension(dest);
            var ext = Path.GetExtension(dest);
            int i = 1;

            if (policy == OverwritePolicy.Rename)
            {
                string candidate;
                do
                {
                    candidate = Path.Combine(dir, $"{baseName} ({i++}){ext}");
                } while (File.Exists(candidate));
                return candidate;
            }

            // Skip
            return Path.Combine(dir, $"{baseName}{ext}"); // 原樣返回（外層會試著寫入，失敗就算略過）
        }
    }
}
