using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// V20.2 (無變更)
    /// 1. [V20.1] 新增 'FileIntakeInfo' 模型，用於接收 'IsBlacklisted' 旗標。
    /// 2. [V20.1] 'IntakeFileAsync' 已更新，現在會接收 'FileIntakeInfo'。
    /// 3. [V20.1] 'IntakeFileAsync' 現在會將黑名單檔案 的 'Status' 設為 "blacklisted"。
    /// 4. [V20.1] 'IntakeFilesAsync' 已更新，現在會接收 'IEnumerable<FileIntakeInfo>'。
    /// </summary>
    public sealed class IntakeService
    {
        private readonly DbService _db;

        public IntakeService(DbService db) { _db = db; }

        /// <summary>
        /// [V20.1] 用於從 HotFolder 傳遞檔案及其黑名單狀態
        /// </summary>
        public class FileIntakeInfo
        {
            public string FullPath { get; set; } = string.Empty;
            public bool IsBlacklisted { get; set; } = false;
        }

        /// <summary>確保資料層初始化（SQLite 建表或建立 JSONL）。</summary>
        public Task InitializeAsync(CancellationToken ct = default) => _db.InitializeAsync();

        /// <summary>
        /// [V20.1]
        /// 把檔案路徑轉成 Item，寫入 DB。
        /// 如果 'info.IsBlacklisted' 為 true，則將 'Status' 設為 "blacklisted"。
        /// </summary>
        public async Task<Item?> IntakeFileAsync(FileIntakeInfo info, CancellationToken ct = default)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.FullPath) || !File.Exists(info.FullPath))
                return null;

            var fi = new FileInfo(info.FullPath);
            var item = new Item
            {
                Path = fi.FullName,
                ProposedPath = string.Empty,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Tags = new List<string>(),

                // [V20.1] 黑名單優化
                Status = info.IsBlacklisted ? "blacklisted" : "intaked",

                Project = null,
                Note = null,
            };

            await _db.InsertAsync(item).ConfigureAwait(false);
            return item;
        }

        /// <summary>
        /// [V20.1] 批次匯入
        /// </summary>
        public async Task<List<Item>> IntakeFilesAsync(IEnumerable<FileIntakeInfo> intakeInfos, CancellationToken ct = default)
        {
            var list = new List<Item>();
            foreach (var info in intakeInfos)
            {
                // 呼叫 V20.1 (Line 39) IntakeFileAsync (含 IsBlacklisted 旗標)
                var one = await IntakeFileAsync(info, ct).ConfigureAwait(false);
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