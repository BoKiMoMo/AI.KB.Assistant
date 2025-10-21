using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public sealed class IntakeService
    {
        private readonly DbService _db;
        private readonly RoutingService _routing;
        private readonly LlmService _llm;
        private readonly AppConfig _cfg;

        public IntakeService(DbService db, RoutingService routing, LlmService llm, AppConfig cfg)
        {
            _db = db;
            _routing = routing;
            _llm = llm;
            _cfg = cfg;
        }

        // 僅收件（放到 DB 的 inbox）
        public async Task<Item> StageOnlyAsync(string filePath, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var info = new FileInfo(filePath);
            var it = new Item
            {
                Filename = info.Name,
                Ext = (info.Extension ?? "").Trim('.').ToLowerInvariant(),
                Project = "",
                Category = "",
                Confidence = 0,
                CreatedTs = new DateTimeOffset(info.CreationTimeUtc).ToUnixTimeSeconds(),
                Status = "inbox",
                Path = info.FullName,
                Tags = ""
            };

            _db.UpsertItem(it);
            await Task.CompletedTask;
            return it;
        }

        // 僅預分類（決定 Category/Project，但不搬檔）
        public async Task<Item> ClassifyOnlyAsync(string filePath, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (!_db.TryGetByPath(filePath, out var item) || item == null)
                item = await StageOnlyAsync(filePath, ct);

            var family = _routing.FamilyFromExt(item.Ext);

            // >>> 這裡用 tuple 解構 <<<
            var (cat, reason) = _routing.GuessCategoryByKeyword(item.Filename ?? "", family);
            item.Category = cat;
            item.Reasoning = reason;

            // Project：鎖定優先，否則用名稱推論
            item.Project = !string.IsNullOrWhiteSpace(_cfg.App.ProjectLock)
                ? _cfg.App.ProjectLock!
                : _routing.GuessProjectByName(item.Filename);

            // 低信心 → LLM 補強（可關閉）
            if (_cfg.OpenAI.EnableWhenLowConfidence &&
                item.Confidence < _cfg.Classification.ConfidenceThreshold)
            {
                try
                {
                    var (p, c, r) = await _llm.RefineAsync(item.Filename ?? "", item.Project ?? "", item.Category ?? "", ct);
                    if (!string.IsNullOrWhiteSpace(p)) item.Project = p;
                    if (!string.IsNullOrWhiteSpace(c)) item.Category = c;
                    if (!string.IsNullOrWhiteSpace(r)) item.Reasoning = r;
                }
                catch { /* 忽略 LLM 失敗 */ }
            }

            item.Status = "pending";
            _db.UpsertItem(item);
            return item;
        }

        // 搬移 pending → 目的地
        public async Task<int> CommitPendingAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var pendings = _db.QueryByStatus("pending").ToList();
            int moved = 0;

            foreach (var it in pendings)
            {
                ct.ThrowIfCancellationRequested();

                var when = DateTimeOffset.FromUnixTimeSeconds(it.CreatedTs).DateTime;
                var dest = _routing.BuildDestination(_cfg, it.Project ?? "", it.Category ?? "", it.Ext ?? "", when);

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                // 設定：Move / Copy、Replace / Rename（皆為 Models 的 enum）
                var moveMode = _cfg.Import.MoveMode;
                var ovw = _cfg.Import.OverwritePolicy;

                var finalDest = _routing.WithAutoRename(dest, ovw);

                if (_routing.IsMove(moveMode))
                    File.Move(it.Path!, finalDest, overwrite: false);
                else
                    File.Copy(it.Path!, finalDest, overwrite: false);

                it.Path = finalDest;
                it.Status = "auto-sorted";
                _db.UpsertItem(it);
                moved++;
            }

            await Task.CompletedTask;
            return moved;
        }
    }
}
