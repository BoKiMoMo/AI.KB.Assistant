using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 先用 In-Memory 完成所有呼叫點。之後要接 SQLite 再替換內部實作即可。
    /// </summary>
    public class DbService : IDisposable
    {
        private readonly List<Item> _items = new();

        public DbService(string? dbPath) { /* TODO: 之後接 SQLite 可用 */ }

        public void Dispose() { /* TODO: 釋放 SQLite 連線 */ }

        // ---------- 查詢 ----------
        public IEnumerable<Item> QueryAll() => _items;

        public Task<IEnumerable<Item>> QueryAllAsync()
            => Task.FromResult<IEnumerable<Item>>(_items.ToList());

        public IEnumerable<Item> QuerySince(DateTime sinceUtc)
            => _items.Where(x => x.CreatedAt >= sinceUtc);

        public Item? TryGetByPath(string fullPath)
            => _items.FirstOrDefault(x => string.Equals(x.SourcePath, fullPath, StringComparison.OrdinalIgnoreCase));

        public Task<Item?> TryGetByPathAsync(string fullPath)
            => Task.FromResult(TryGetByPath(fullPath));

        public IEnumerable<Item> QueryByPath(string fullPath)
        {
            var one = TryGetByPath(fullPath);
            return one is null ? Enumerable.Empty<Item>() : new[] { one };
        }

        public IEnumerable<Item> QueryByStatus(string status)
            => _items.Where(x => string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase));

        public IEnumerable<Item> QueryByTag(string tag)
            => _items.Where(x => (x.Tags ?? "")
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)));

        public IEnumerable<string> QueryDistinctProjects()
            => _items.Select(x => x.Project ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).Distinct();

        // ---------- 寫入 ----------
        public Item Upsert(Item it)
        {
            if (it.Id == 0) it.Id = DateTime.UtcNow.Ticks;
            var idx = _items.FindIndex(x => x.Id == it.Id);
            if (idx >= 0) _items[idx] = it; else _items.Add(it);
            return it;
        }

        public Task<Item> UpsertAsync(Item it) => Task.FromResult(Upsert(it));
        public void UpsertRange(IEnumerable<Item> items) { foreach (var it in items) Upsert(it); }
        public Task UpsertRangeAsync(IEnumerable<Item> items) { UpsertRange(items); return Task.CompletedTask; }

        // ---------- 其他 ----------
        public void RemoveById(long id)
        {
            var idx = _items.FindIndex(x => x.Id == id);
            if (idx >= 0) _items.RemoveAt(idx);
        }

        public void Truncate() => _items.Clear();
    }
}
