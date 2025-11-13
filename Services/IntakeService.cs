using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// V19.0 (V18.0 回滾 P2)
    /// 1. (V11.0) 確保 'using System.Threading' 存在。
    /// 2. [V19.0 回滾 P2] 移除 V18.0 [cite: `Services/IntakeService.cs (V18.0)` Line 42] 'IntakeFileAsync' [Line 39] 的 'isFolder' 參數。
    /// 3. [V19.0 回滾 P2] V18.0 [cite: `Services/IntakeService.cs (V18.0)` Line 63] 'IntakeItemsAsync' 
    ///    回滾為 V17.0 [cite: `Services/HotFolderService.cs (V17.1)` Line 175] 'IntakeFilesAsync' [Line 63] (只接收 'IEnumerable<string>')。
    /// </summary>
    public sealed class IntakeService
    {
        private readonly DbService _db;

        public IntakeService(DbService db) { _db = db; }

        /// <summary>確保資料層初始化（SQLite 建表或建立 JSONL）。</summary>
        public Task InitializeAsync(CancellationToken ct = default) => _db.InitializeAsync();

        /// <summary>
        /// [V19.0 回滾 P2] 
        /// 把檔案路徑轉成 Item，寫入 DB。
        /// </summary>
        public async Task<Item?> IntakeFileAsync(string srcFullPath, CancellationToken ct = default)
        {
            // (V11.0)
            if (string.IsNullOrWhiteSpace(srcFullPath) || !File.Exists(srcFullPath))
                return null;

            var fi = new FileInfo(srcFullPath);
            var item = new Item
            {
                // (V11.0)
                Path = fi.FullName,
                ProposedPath = string.Empty,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Tags = new List<string>(),
                Status = "intaked",
                Project = null,
                Note = null,
                // [V19.0 回滾 P2] V18.0 [cite: `Services/IntakeService.cs (V18.0)` Line 56] 的 IsFolder [cite: `Models/Item.cs (V18.0)` Line 105] 已移除
            };

            // (V11.0)
            await _db.InsertAsync(item).ConfigureAwait(false);
            return item;
        }

        /// <summary>
        /// [V19.0 回滾 P2] 批次匯入 (V18.0 [cite: `Services/IntakeService.cs (V18.0)` Line 63] IntakeItemsAsync)
        /// </summary>
        public async Task<List<Item>> IntakeFilesAsync(IEnumerable<string> srcPaths, CancellationToken ct = default)
        {
            var list = new List<Item>();
            foreach (var p in srcPaths)
            {
                // 呼叫 V19.0 (Line 39) IntakeFileAsync (無 isFolder)
                var one = await IntakeFileAsync(p, ct).ConfigureAwait(false);
                if (one != null) list.Add(one);
            }
            return list;
        }

        // (V11.0)
        public Task<List<Item>> QueryAllAsync(CancellationToken ct = default) => _db.QueryAllAsync();
        public Task<int> InsertItemsAsync(IEnumerable<Item> items, CancellationToken ct = default) => _db.InsertItemsAsync(items);
        public Task<int> UpdateItemsAsync(IEnumerable<Item> items, CancellationToken ct = default) => _db.UpdateItemsAsync(items);

        // (V11.0)
        public Task<int> DeleteItemsAsync(IEnumerable<string> ids, CancellationToken ct = default)
        {
            return _db.DeleteItemsAsync(ids);
        }

        public Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage, CancellationToken ct = default) => _db.StageOnlyAsync(items, stage);
    }
}