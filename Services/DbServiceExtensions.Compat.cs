// Services/DbServiceExtensions.Compat.cs
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public static class DbServiceExtensions_Compat
    {
        /// <summary>
        /// 兼容舊版：透過 SourcePath 找單一 Item；若無專用查詢，退回全表掃描。
        /// </summary>
        public static async Task<Item?> TryGetByPathAsync(this DbService db, string sourcePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return null;

            // 若你有 QueryBySourcePathAsync，可改成直接呼叫：
            // return await db.QueryBySourcePathAsync(sourcePath, ct);

            var all = await db.QueryAllAsync(ct).ConfigureAwait(false);
            return all.FirstOrDefault(x => string.Equals(x.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 兼容舊版：Upsert；先 Insert 再 Update（內部若有衝突處理會自行去重）
        /// </summary>
        public static async Task UpsertAsync(this DbService db, IEnumerable<Item> items, CancellationToken ct = default)
        {
            var list = items?.ToList() ?? new List<Item>();
            if (list.Count == 0) return;

            // 如果你的 DbService 有批次 Upsert 可直接呼叫；目前先保險做兩步。
            await db.InsertItemsAsync(list, ct).ConfigureAwait(false);
            await db.UpdateItemsAsync(list, ct).ConfigureAwait(false);
        }
    }
}
