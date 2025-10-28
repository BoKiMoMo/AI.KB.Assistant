using AI.KB.Assistant.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

#if USE_SQLITE
using Microsoft.Data.Sqlite;
#endif

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 最終版 DbService
    /// - 以「db 檔案路徑」初始化
    /// - 預設使用 SQLite（需要 Microsoft.Data.Sqlite）；若編譯環境沒有套件，退回記憶體後援
    /// - 提供 Upsert / Remove / TryGetByPath / QueryByProject / QueryByTag / QueryAll
    /// </summary>
    public class DbService
    {
        public string DbPath { get; }
        private readonly bool _useMemory;

        private readonly ConcurrentDictionary<string, Item> _mem = new(StringComparer.OrdinalIgnoreCase);

        public DbService(string dbPath)
        {
            DbPath = string.IsNullOrWhiteSpace(dbPath)
                ? Path.Combine(Path.GetTempPath(), "ai.kb.assistant.fallback.db")
                : dbPath;

#if USE_SQLITE
            _useMemory = false;
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
            EnsureSchema();
#else
            _useMemory = true;
#endif
        }

#if USE_SQLITE
        private string ConnStr => $"Data Source={DbPath};Cache=Shared";

        private void EnsureSchema()
        {
            using var cn = new SqliteConnection(ConnStr);
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS items(
  path TEXT PRIMARY KEY,
  filename TEXT,
  ext TEXT,
  project TEXT,
  tags TEXT,
  proposed_path TEXT,
  created_ts TEXT
);";
            cmd.ExecuteNonQuery();
        }
#endif

        public async Task UpsertAsync(Item it)
        {
            if (it == null) return;
            var key = it.Path ?? it.Filename ?? Guid.NewGuid().ToString("N");

#if USE_SQLITE
            using var cn = new SqliteConnection(ConnStr);
            await cn.OpenAsync();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO items(path, filename, ext, project, tags, proposed_path, created_ts)
VALUES($p,$f,$e,$pr,$t,$pp,$c)
ON CONFLICT(path) DO UPDATE SET
  filename=excluded.filename,
  ext=excluded.ext,
  project=excluded.project,
  tags=excluded.tags,
  proposed_path=excluded.proposed_path,
  created_ts=excluded.created_ts;";
            cmd.Parameters.AddWithValue("$p", key);
            cmd.Parameters.AddWithValue("$f", it.Filename ?? "");
            cmd.Parameters.AddWithValue("$e", it.Ext ?? "");
            cmd.Parameters.AddWithValue("$pr", it.Project ?? "");
            cmd.Parameters.AddWithValue("$t", it.Tags ?? "");
            cmd.Parameters.AddWithValue("$pp", it.ProposedPath ?? "");
            cmd.Parameters.AddWithValue("$c", it.CreatedTs == default ? DateTime.UtcNow : it.CreatedTs);
            await cmd.ExecuteNonQueryAsync();
#else
            _mem[key] = it;
            await Task.CompletedTask;
#endif
        }

        public async Task RemoveAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
#if USE_SQLITE
            using var cn = new SqliteConnection(ConnStr);
            await cn.OpenAsync();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "DELETE FROM items WHERE path=$p;";
            cmd.Parameters.AddWithValue("$p", path);
            await cmd.ExecuteNonQueryAsync();
#else
            _mem.TryRemove(path, out _);
            await Task.CompletedTask;
#endif
        }

        public async Task<Item?> TryGetByPathAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
#if USE_SQLITE
            using var cn = new SqliteConnection(ConnStr);
            await cn.OpenAsync();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT path,filename,ext,project,tags,proposed_path,created_ts FROM items WHERE path=$p;";
            cmd.Parameters.AddWithValue("$p", path);
            using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                return new Item
                {
                    Path = r.GetString(0),
                    Filename = r.GetString(1),
                    Ext = r.GetString(2),
                    Project = r.GetString(3),
                    Tags = r.GetString(4),
                    ProposedPath = r.GetString(5),
                    CreatedTs = r.IsDBNull(6) ? DateTime.MinValue : r.GetDateTime(6)
                };
            }
            return null;
#else
            _mem.TryGetValue(path, out var it);
            return it;
#endif
        }

        public bool TryGetByPath(string path, out Item? item)
        {
#if USE_SQLITE
            item = TryGetByPathAsync(path).GetAwaiter().GetResult();
            return item != null;
#else
            _mem.TryGetValue(path, out item);
            return item != null;
#endif
        }

        public async Task<List<Item>> QueryAllAsync()
        {
#if USE_SQLITE
            var list = new List<Item>();
            using var cn = new SqliteConnection(ConnStr);
            await cn.OpenAsync();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT path,filename,ext,project,tags,proposed_path,created_ts FROM items;";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new Item
                {
                    Path = r.GetString(0),
                    Filename = r.GetString(1),
                    Ext = r.GetString(2),
                    Project = r.GetString(3),
                    Tags = r.GetString(4),
                    ProposedPath = r.GetString(5),
                    CreatedTs = r.IsDBNull(6) ? DateTime.MinValue : r.GetDateTime(6)
                });
            }
            return list;
#else
            return _mem.Values.ToList();
#endif
        }

        public async Task<List<Item>> QueryByProjectAsync(string project)
        {
            project ??= "";
#if USE_SQLITE
            var list = new List<Item>();
            using var cn = new SqliteConnection(ConnStr);
            await cn.OpenAsync();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT path,filename,ext,project,tags,proposed_path,created_ts FROM items WHERE project=$pr;";
            cmd.Parameters.AddWithValue("$pr", project);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new Item
                {
                    Path = r.GetString(0),
                    Filename = r.GetString(1),
                    Ext = r.GetString(2),
                    Project = r.GetString(3),
                    Tags = r.GetString(4),
                    ProposedPath = r.GetString(5),
                    CreatedTs = r.IsDBNull(6) ? DateTime.MinValue : r.GetDateTime(6)
                });
            }
            return list;
#else
            return _mem.Values.Where(x => string.Equals(x.Project ?? "", project, StringComparison.OrdinalIgnoreCase)).ToList();
#endif
        }

        public async Task<List<Item>> QueryByTagAsync(string tag)
        {
            tag ??= "";
#if USE_SQLITE
            // 簡易 LIKE；若要正規化請改關聯表
            var list = new List<Item>();
            using var cn = new SqliteConnection(ConnStr);
            await cn.OpenAsync();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT path,filename,ext,project,tags,proposed_path,created_ts FROM items WHERE tags LIKE $kw;";
            cmd.Parameters.AddWithValue("$kw", $"%{tag}%");
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new Item
                {
                    Path = r.GetString(0),
                    Filename = r.GetString(1),
                    Ext = r.GetString(2),
                    Project = r.GetString(3),
                    Tags = r.GetString(4),
                    ProposedPath = r.GetString(5),
                    CreatedTs = r.IsDBNull(6) ? DateTime.MinValue : r.GetDateTime(6)
                });
            }
            return list;
#else
            return _mem.Values.Where(x =>
                (x.Tags ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
                .ToList();
#endif
        }

        public async Task UpsertManyAsync(IEnumerable<Item> items)
        {
            foreach (var it in items) await UpsertAsync(it);
        }
    }
}