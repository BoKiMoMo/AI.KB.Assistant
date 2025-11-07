using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// V7.32 更新：
    /// 1. 加入 DeleteItemsAsync，以支援 HotFolderService 的鏡像 (Mirroring) 邏輯。
    /// 2. 保持 IntakeFile/FilesAsync 不變，供「手動加入」按鈕的 V7.20 邏輯使用 (雖然 V7.32 已停用)。
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

        // V7.32 新增：允許 HotFolderService 刪除項目
        /// <summary>
        /// (V7.32) 批次刪除項目 (依 ID)
        /// </summary>
        public async Task<int> DeleteItemsAsync(IEnumerable<string> ids, CancellationToken ct = default)
        {
            var n = 0;
            foreach (var id in ids)
            {
                n += await _db.DeleteByIdAsync(id, ct).ConfigureAwait(false);
            }
            return n;
        }

        public Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage, CancellationToken ct = default) => _db.StageOnlyAsync(items, stage, ct);
    }
}