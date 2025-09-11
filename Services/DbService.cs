using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AI.KB.Assistant.Models;
using Newtonsoft.Json;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 簡易 JSON 檔持久化版資料庫（免 SQLite）。
    /// 後續若要換 SQLite，對外方法維持即可。
    /// </summary>
    public sealed class DbService : IDisposable
    {
        private readonly string _dbPath;
        private readonly object _lock = new();
        private List<Item> _mem = new();

        public DbService(string dbPath)
        {
            _dbPath = string.IsNullOrWhiteSpace(dbPath)
                ? Path.Combine(Environment.CurrentDirectory, "data.json")
                : dbPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? dbPath
                  : Path.ChangeExtension(dbPath, ".json");

            Load();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_dbPath))
                {
                    _mem = new List<Item>();
                    return;
                }
                var json = File.ReadAllText(_dbPath, Encoding.UTF8);
                _mem = JsonConvert.DeserializeObject<List<Item>>(json) ?? new List<Item>();
            }
            catch
            {
                _mem = new List<Item>();
            }
        }

        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(_dbPath));
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir!);
                var json = JsonConvert.SerializeObject(_mem, Formatting.Indented);
                File.WriteAllText(_dbPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
            catch
            {
                // 寫檔失敗時先忽略；可加日誌
            }
        }

        public long Add(Item it)
        {
            lock (_lock)
            {
                // 以 path+filename 當唯一性
                if (!_mem.Any(x => string.Equals(x.Path, it.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    _mem.Add(it);
                    Save();
                }
                return _mem.Count;
            }
        }

        public IEnumerable<Item> Recent(int days = 7)
        {
            var since = DateTimeOffset.Now.AddDays(-Math.Abs(days)).ToUnixTimeSeconds();
            lock (_lock)
            {
                return _mem.Where(x => x.CreatedTs >= since)
                           .OrderByDescending(x => x.CreatedTs)
                           .ToList();
            }
        }

        public IEnumerable<Item> ByStatus(string status)
        {
            lock (_lock)
            {
                return _mem.Where(x => string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase))
                           .OrderByDescending(x => x.CreatedTs)
                           .ToList();
            }
        }

        public IEnumerable<Item> Search(string keyword)
        {
            var q = (keyword ?? "").Trim();
            if (q.Length == 0) return Enumerable.Empty<Item>();

            lock (_lock)
            {
                return _mem.Where(x =>
                          (x.Filename ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                       || (x.Category ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                       || (x.Summary ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                       || (x.Tags ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                       .OrderByDescending(x => x.CreatedTs)
                       .ToList();
            }
        }

        public int UpdateStatusByPath(IEnumerable<string> paths, string status)
        {
            if (paths == null) return 0;
            var set = new HashSet<string>(paths.Where(p => !string.IsNullOrWhiteSpace(p)),
                                          StringComparer.OrdinalIgnoreCase);
            if (set.Count == 0) return 0;

            lock (_lock)
            {
                int count = 0;
                foreach (var it in _mem.Where(x => set.Contains(x.Path)))
                {
                    it.Status = status;
                    count++;
                }
                if (count > 0) Save();
                return count;
            }
        }

        public void Dispose()
        {
            // 無額外資源
        }
    }
}
