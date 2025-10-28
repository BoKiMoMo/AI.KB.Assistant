using AI.KB.Assistant.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AI.KB.Assistant.Services
{
    public class IntakeService
    {
        private readonly AppConfig _cfg;
        private readonly DbService _db;
        private readonly RoutingService _routing;
        private readonly LlmService _llm;

        public IntakeService(AppConfig cfg, DbService db, RoutingService routing, LlmService llm)
        {
            _cfg = cfg;
            _db = db;
            _routing = routing;
            _llm = llm;
        }

        /// <summary>將檔案加入收件（只建 DB 紀錄，不分類、不搬檔）</summary>
        public async Task StageOnlyAsync(string filePath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

            var it = await _db.TryGetByPathAsync(filePath);
            if (it == null)
            {
                it = new Item
                {
                    Path = filePath,
                    Filename = Path.GetFileName(filePath),
                    Ext = Path.GetExtension(filePath).Trim('.').ToLowerInvariant(),
                    Project = new DirectoryInfo(Path.GetDirectoryName(filePath) ?? string.Empty).Name,
                    Tags = string.Empty,
                    ProposedPath = string.Empty,
                    // ★ 用 ticks
                    CreatedTs = File.GetCreationTime(filePath).Ticks
                };
            }
            else
            {
                it.Filename = Path.GetFileName(filePath);
                it.Ext = Path.GetExtension(filePath).Trim('.').ToLowerInvariant();
                it.Project = new DirectoryInfo(Path.GetDirectoryName(filePath) ?? string.Empty).Name;
                // ★ 用 ticks
                it.CreatedTs = File.GetCreationTime(filePath).Ticks;
            }

            await _db.UpsertAsync(it);
        }

        /// <summary>只計算 ProposedPath（不搬檔）</summary>
        public async Task ClassifyOnlyAsync(string filePath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

            var it = await _db.TryGetByPathAsync(filePath) ?? new Item
            {
                Path = filePath,
                Filename = Path.GetFileName(filePath),
                Ext = Path.GetExtension(filePath).Trim('.').ToLowerInvariant(),
                Project = new DirectoryInfo(Path.GetDirectoryName(filePath) ?? string.Empty).Name,
                Tags = string.Empty,
                // ★ 用 ticks
                CreatedTs = File.GetCreationTime(filePath).Ticks
            };

            it.ProposedPath = _routing.PreviewDestPath(filePath, _cfg.App.ProjectLock, null);
            await _db.UpsertAsync(it);
        }

        /// <summary>依 ProposedPath 搬檔/複製。</summary>
        public async Task<int> CommitPendingAsync(OverwritePolicy policy, bool copyMode, CancellationToken ct)
        {
            var all = await _db.QueryAllAsync();
            var candidates = all.Where(x =>
                !string.IsNullOrWhiteSpace(x.Path) &&
                File.Exists(x.Path) &&
                !string.IsNullOrWhiteSpace(x.ProposedPath)).ToList();

            int moved = 0;

            foreach (var it in candidates)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var dest = it.ProposedPath!;
                    var destDir = Path.GetDirectoryName(dest)!;
                    Directory.CreateDirectory(destDir);

                    var doMove = !copyMode;
                    var destExists = File.Exists(dest);

                    if (destExists)
                    {
                        switch (policy)
                        {
                            case OverwritePolicy.Skip:
                                continue;
                            case OverwritePolicy.Replace:
                                File.Delete(dest);
                                break;
                            case OverwritePolicy.Rename:
                                dest = MakeNonConflictPath(dest);
                                break;
                        }
                    }

                    if (doMove) File.Move(it.Path!, dest);
                    else File.Copy(it.Path!, dest, overwrite: policy == OverwritePolicy.Replace);

                    it.Path = dest;
                    it.Filename = Path.GetFileName(dest);
                    it.Project = new DirectoryInfo(Path.GetDirectoryName(dest) ?? string.Empty).Name;
                    it.ProposedPath = string.Empty;
                    await _db.UpsertAsync(it);

                    moved++;
                }
                catch { /* 單筆錯誤不擋流程 */ }
            }

            return moved;
        }

        /// <summary>回傳建立時間 >= dt 的項目（相容舊介面；Item.CreatedTs 為 ticks）。</summary>
        public async Task<List<Item>> QuerySinceAsync(DateTime dt)
        {
            long ticks = dt.Ticks;
            var all = await _db.QueryAllAsync();
            return all.Where(x => x.CreatedTs >= ticks).ToList();
        }

        private static string MakeNonConflictPath(string destFull)
        {
            var dir = Path.GetDirectoryName(destFull)!;
            var name = Path.GetFileNameWithoutExtension(destFull);
            var ext = Path.GetExtension(destFull);
            var i = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(dir, $"{name} ({i++}){ext}");
            } while (File.Exists(candidate));
            return candidate;
        }
    }
}
