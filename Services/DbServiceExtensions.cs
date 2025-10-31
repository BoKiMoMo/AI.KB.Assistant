using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    // 主要擴充方法放這裡
    public static partial class DbServiceExtensions
    {
        // 直通 DbService（讓呼叫端可用 db.QueryAllAsync() 這種擴充風格）
        public static Task<List<Item>> QueryAllAsync(this DbService db)
            => db.QueryAllAsync();

        public static Task<int> InsertItemsAsync(this DbService db, IEnumerable<Item> items)
            => db.InsertItemsAsync(items);

        public static Task<int> UpdateItemsAsync(this DbService db, IEnumerable<Item> items)
            => db.UpdateItemsAsync(items);

        public static Task<int> StageOnlyAsync(this DbService db, IEnumerable<Item> items, int stage)
            => db.StageOnlyAsync(items, stage);

        // 便捷單筆包裝
        public static Task<int> InsertAsync(this DbService db, Item item)
            => db.InsertItemsAsync(Enumerable.Repeat(item, 1));

        public static Task<int> UpdateAsync(this DbService db, Item item)
            => db.UpdateItemsAsync(Enumerable.Repeat(item, 1));

        public static Task<int> StageOnlyAsync(this DbService db, Item item, int stage)
            => db.StageOnlyAsync(Enumerable.Repeat(item, 1), stage);
    }
}
