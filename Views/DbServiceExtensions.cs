using AI.KB.Assistant.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AI.KB.Assistant.Services
{
    public static class DbServiceExtensions
    {
        public static void Upsert(this DbService db, Item item)
            => db.UpsertAsync(item).GetAwaiter().GetResult();

        public static bool QueryByPath(this DbService db, string path, out Item? item)
        {
            item = db.TryGetByPathAsync(path).GetAwaiter().GetResult();
            return item != null;
        }

        public static Item? QueryByPath(this DbService db, string path)
            => db.TryGetByPathAsync(path).GetAwaiter().GetResult();

        // ★ Item.CreatedTs 是 long（ticks）
        public static List<Item> QuerySince(this DbService db, DateTime since)
        {
            long ticks = since.Ticks;
            return db.QueryAllAsync().GetAwaiter().GetResult()
                     .Where(x => x.CreatedTs >= ticks).ToList();
        }

        public static List<Item> QueryByStatus(this DbService db, string status)
        {
            status ??= string.Empty;
            var all = db.QueryAllAsync().GetAwaiter().GetResult();
            return all.Where(x =>
                string.Equals(x.Project ?? "", status, StringComparison.OrdinalIgnoreCase) ||
                (x.Tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                               .Select(t => t.Trim())
                               .Any(t => string.Equals(t, status, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }
    }
}
