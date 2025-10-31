using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 檔案 → Item → DB 的入口。
    /// 注意：不做搬檔；搬檔交由 RoutingService。
    /// </summary>
    public sealed class IntakeService
    {
        private readonly DbService _db;

        public IntakeService(DbService db) { _db = db; }

        /// <summary>確保資料層初始化（SQLite 建表或建立 JSONL）。</summary>
        public Task InitializeAsync(CancellationToken ct = default) => _db.InitializeAsync(ct);

        /// <summary>把檔案路徑轉成 Item，寫入 DB。</summary>
        public async Task<Item?> IntakeFileAsync(string srcFullPath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(srcFullPath) || !File.Exists(srcFullPath))
                return null;

            var fi = new FileInfo(srcFullPath);
            var item = new Item
            {
                // Id 由 Item 預設 GUID 產生
                Path = fi.FullName,
                ProposedPath = string.Empty,
                CreatedAt = DateTime.UtcNow, // NOTE: 先用 UTC；之後可記錄實檔時間
                UpdatedAt = DateTime.UtcNow,
                Tags = new List<string>(),
                Status = "intaked",
                Project = null,
                Note = null
            };

            await _db.InsertAsync(item, ct).ConfigureAwait(false);
            return item;
        }

        /// <summary>批次匯入（傳入多個路徑）。</summary>
        public async Task<List<Item>> IntakeFilesAsync(IEnumerable<string> srcPaths, CancellationToken ct = default)
        {
            var list = new List<Item>();
            foreach (var p in srcPaths)
            {
                var one = await IntakeFileAsync(p, ct).ConfigureAwait(false);
                if (one != null) list.Add(one);
            }
            return list;
        }

        // 與舊版對齊的直通 API（供現有呼叫不改碼）
        public Task<List<Item>> QueryAllAsync(CancellationToken ct = default) => _db.QueryAllAsync(ct);
        public Task<int> InsertItemsAsync(IEnumerable<Item> items, CancellationToken ct = default) => _db.InsertItemsAsync(items, ct);
        public Task<int> UpdateItemsAsync(IEnumerable<Item> items, CancellationToken ct = default) => _db.UpdateItemsAsync(items, ct);
        public Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage, CancellationToken ct = default) => _db.StageOnlyAsync(items, stage, ct);
    }
}
