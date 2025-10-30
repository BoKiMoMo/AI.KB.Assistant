using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 本地資料緩存與資料庫服務。
    /// 支援暫存清單操作，並預留 SQLite 儲存功能。
    /// </summary>
    public class DbService
    {
        private readonly List<Item> _buffer = new();
        private readonly string _dbPath;

        public DbService(AppConfig config)
        {
            _dbPath = config.Db?.Path ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(_dbPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
            }
        }

        // === 基礎快取層 ===

        /// <summary>
        /// 新增多筆 Item 資料（暫存在記憶體）
        /// </summary>
        public async Task InsertItemsAsync(IEnumerable<Item> items)
        {
            if (items == null) return;
            await Task.Run(() =>
            {
                lock (_buffer)
                {
                    _buffer.AddRange(items);
                }
            });
        }

        /// <summary>
        /// 更新多筆 Item 資料（暫存模式模擬更新）
        /// </summary>
        public async Task UpdateItemsAsync(IEnumerable<Item> items)
        {
            if (items == null) return;
            await Task.Run(() =>
            {
                lock (_buffer)
                {
                    foreach (var item in items)
                    {
                        var existing = _buffer.FirstOrDefault(x => x.Id == item.Id);
                        if (existing != null)
                        {
                            existing.Status = item.Status;
                            existing.Project = item.Project;
                            existing.Tags = item.Tags;
                            existing.ProposedPath = item.ProposedPath;
                            existing.DestPath = item.DestPath;
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 查詢所有記錄。
        /// </summary>
        public Task<List<Item>> QueryAllAsync()
        {
            lock (_buffer)
            {
                return Task.FromResult(_buffer.ToList());
            }
        }

        /// <summary>
        /// 根據專案名稱查詢。
        /// </summary>
        public Task<List<Item>> QueryByProjectAsync(string project)
        {
            if (string.IsNullOrWhiteSpace(project))
                return Task.FromResult(new List<Item>());

            lock (_buffer)
            {
                var result = _buffer.Where(x =>
                    x.Project?.Equals(project, StringComparison.OrdinalIgnoreCase) == true).ToList();
                return Task.FromResult(result);
            }
        }

        /// <summary>
        /// 根據標籤查詢。
        /// </summary>
        public Task<List<Item>> QueryByTagAsync(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return Task.FromResult(new List<Item>());

            lock (_buffer)
            {
                var result = _buffer.Where(x =>
                    (x.Tags ?? string.Empty).Contains(tag, StringComparison.OrdinalIgnoreCase)).ToList();
                return Task.FromResult(result);
            }
        }

        /// <summary>
        /// 根據檔案路徑查詢。
        /// </summary>
        public Task<Item?> QueryByPathAsync(string path)
        {
            lock (_buffer)
            {
                var result = _buffer.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(result);
            }
        }

        /// <summary>
        /// 移除指定 Id 的資料。
        /// </summary>
        public async Task DeleteByIdAsync(Guid id)
        {
            await Task.Run(() =>
            {
                lock (_buffer)
                {
                    var target = _buffer.FirstOrDefault(x => x.Id == id);
                    if (target != null)
                        _buffer.Remove(target);
                }
            });
        }

        // === 舊呼叫相容層 ===

        /// <summary>
        /// 舊呼叫：非同步插入單筆。
        /// </summary>
        public Task InsertItemAsync(Item item)
            => InsertItemsAsync(new[] { item });

        /// <summary>
        /// 舊呼叫：更新單筆。
        /// </summary>
        public Task UpdateItemAsync(Item item)
            => UpdateItemsAsync(new[] { item });

        /// <summary>
        /// 舊呼叫：立即儲存（模擬 SaveChanges）。
        /// </summary>
        public Task CommitAsync() => Task.CompletedTask;

        // === SQLite 擴充介面（預留） ===

        /// <summary>
        /// 載入資料庫資料（若未啟用 SQLite，則忽略）。
        /// </summary>
        public Task LoadFromDiskAsync()
        {
            // 若未啟用 DB 檔案，略過
            if (string.IsNullOrWhiteSpace(_dbPath))
                return Task.CompletedTask;

            // 預留 SQLite 或 JSON 存取邏輯
            return Task.CompletedTask;
        }

        /// <summary>
        /// 將目前記憶體快取儲存至磁碟。
        /// </summary>
        public Task SaveToDiskAsync()
        {
            if (string.IsNullOrWhiteSpace(_dbPath))
                return Task.CompletedTask;

            // 預留 SQLite 或 JSON 寫入邏輯
            return Task.CompletedTask;
        }

        // === 工具方法 ===

        public IReadOnlyList<Item> GetAllCached()
        {
            lock (_buffer)
                return _buffer.ToList().AsReadOnly();
        }

        public int Count => _buffer.Count;
    }
}
