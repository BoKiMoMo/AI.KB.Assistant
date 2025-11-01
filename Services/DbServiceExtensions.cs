using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// Db 擴充方法（V7.2 完整版）
    /// - 維持舊介面
    /// - 新增所有 CancellationToken 多載，與 IntakeService 呼叫對齊
    /// </summary>
    public static partial class DbServiceExtensions
    {
        // ===== 舊介面（無 CT） =====
        public static Task<List<Item>> QueryAllAsync(this DbService db)
            => db.QueryAllAsync();

        public static Task<int> InsertItemsAsync(this DbService db, IEnumerable<Item> items)
            => db.InsertItemsAsync(items);

        public static Task<int> UpdateItemsAsync(this DbService db, IEnumerable<Item> items)
            => db.UpdateItemsAsync(items);

        public static Task<int> StageOnlyAsync(this DbService db, IEnumerable<Item> items, int stage)
            => db.StageOnlyAsync(items, stage);

        public static Task<int> InsertAsync(this DbService db, Item item)
            => db.InsertItemsAsync(Enumerable.Repeat(item, 1));

        public static Task<int> UpdateAsync(this DbService db, Item item)
            => db.UpdateItemsAsync(Enumerable.Repeat(item, 1));

        public static Task<int> UpsertAsync(this DbService db, Item item)
            => db.UpsertAsync(Enumerable.Repeat(item, 1));

        public static Task<List<Item>> ListRecentAsync(this DbService db, int take = 200)
            => db.ListRecentAsync(take);

        public static Task<List<Item>> SearchAsync(this DbService db, string keyword, int take = 200)
            => db.SearchAsync(keyword, take);

        public static Task<Item?> GetByIdAsync(this DbService db, string id)
            => db.GetByIdAsync(id);

        public static Task<int> DeleteByIdAsync(this DbService db, string id)
            => db.DeleteByIdAsync(id);

        // ===== 新增：CT 多載 =====
        public static Task InitializeAsync(this DbService db, CancellationToken ct)
            => db.InitializeAsync(ct);

        public static Task<List<Item>> QueryAllAsync(this DbService db, CancellationToken ct)
            => db.QueryAllAsync(ct);

        public static Task<int> InsertItemsAsync(this DbService db, IEnumerable<Item> items, CancellationToken ct)
            => db.InsertItemsAsync(items, ct);

        public static Task<int> UpdateItemsAsync(this DbService db, IEnumerable<Item> items, CancellationToken ct)
            => db.UpdateItemsAsync(items, ct);

        public static Task<int> StageOnlyAsync(this DbService db, IEnumerable<Item> items, int stage, CancellationToken ct)
            => db.StageOnlyAsync(items, stage, ct);

        public static Task<int> InsertAsync(this DbService db, Item item, CancellationToken ct)
            => db.InsertItemsAsync(Enumerable.Repeat(item, 1), ct);

        public static Task<int> UpdateAsync(this DbService db, Item item, CancellationToken ct)
            => db.UpdateItemsAsync(Enumerable.Repeat(item, 1), ct);

        public static Task<int> UpsertAsync(this DbService db, Item item, CancellationToken ct)
            => db.UpsertAsync(Enumerable.Repeat(item, 1), ct);

        public static Task<List<Item>> ListRecentAsync(this DbService db, int take, CancellationToken ct)
            => db.ListRecentAsync(take, ct);

        public static Task<List<Item>> SearchAsync(this DbService db, string keyword, int take, CancellationToken ct)
            => db.SearchAsync(keyword, take, ct);

        public static Task<Item?> GetByIdAsync(this DbService db, string id, CancellationToken ct)
            => db.GetByIdAsync(id, ct);

        public static Task<int> DeleteByIdAsync(this DbService db, string id, CancellationToken ct)
            => db.DeleteByIdAsync(id, ct);
    }
}
