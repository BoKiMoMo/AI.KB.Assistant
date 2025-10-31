using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// DbService 的便捷擴充（語法糖 + 單筆操作）
    /// </summary>
    public static partial class DbServiceExtensions
    {
        public static Task InitializeAsync(this DbService db, CancellationToken ct = default)
            => db.InitializeAsync(ct);

        public static Task<List<Item>> QueryAllAsync(this DbService db, CancellationToken ct = default)
            => db.QueryAllAsync(ct);

        public static Task<int> InsertItemsAsync(this DbService db, IEnumerable<Item> items, CancellationToken ct = default)
            => db.InsertItemsAsync(items, ct);

        public static Task<int> UpdateItemsAsync(this DbService db, IEnumerable<Item> items, CancellationToken ct = default)
            => db.UpdateItemsAsync(items, ct);

        public static Task<int> StageOnlyAsync(this DbService db, IEnumerable<Item> items, int stage, CancellationToken ct = default)
            => db.StageOnlyAsync(items, stage, ct);

        // ---- 單筆便捷 ----
        public static Task<int> InsertAsync(this DbService db, Item item, CancellationToken ct = default)
            => db.InsertItemsAsync(Enumerable.Repeat(item, 1), ct);

        public static Task<int> UpdateAsync(this DbService db, Item item, CancellationToken ct = default)
            => db.UpdateItemsAsync(Enumerable.Repeat(item, 1), ct);

        public static Task<int> StageOnlyAsync(this DbService db, Item item, int stage, CancellationToken ct = default)
            => db.StageOnlyAsync(Enumerable.Repeat(item, 1), stage, ct);
    }
}
