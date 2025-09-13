using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public sealed class DbService : IDisposable
    {
        private readonly string _connStr;

        public DbService(string dbPath)
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir!);
            _connStr = $"Data Source={dbPath}";
            EnsureTables();
            RunLightMigrations();
        }

        private IDbConnection Open() => new SqliteConnection(_connStr);

        private void EnsureTables()
        {
            using var cn = Open();
            cn.Execute("""
                CREATE TABLE IF NOT EXISTS items(
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    path         TEXT NOT NULL,
                    filename     TEXT NOT NULL,
                    category     TEXT,
                    confidence   REAL,
                    created_ts   INTEGER,
                    summary      TEXT,
                    reasoning    TEXT,
                    status       TEXT,
                    tags         TEXT,
                    project      TEXT
                );
            """);

            cn.Execute("""
                CREATE INDEX IF NOT EXISTS idx_items_filename  ON items(filename);
                CREATE INDEX IF NOT EXISTS idx_items_category  ON items(category);
                CREATE INDEX IF NOT EXISTS idx_items_status    ON items(status);
                CREATE INDEX IF NOT EXISTS idx_items_created   ON items(created_ts);
                CREATE INDEX IF NOT EXISTS idx_items_project   ON items(project);
            """);
        }

        private void RunLightMigrations()
        {
            // 保留：目前欄位已齊
        }

        public long Add(Item it)
        {
            using var cn = Open();
            const string sql = @"
INSERT INTO items(path, filename, category, confidence, created_ts, summary, reasoning, status, tags, project)
VALUES(@Path, @Filename, @Category, @Confidence, @CreatedTs, @Summary, @Reasoning, @Status, @Tags, @Project);
SELECT last_insert_rowid();";
            return cn.ExecuteScalar<long>(sql, it);
        }

        public IEnumerable<Item> Recent(int days = 7)
        {
            var since = DateTimeOffset.Now.AddDays(-Math.Abs(days)).ToUnixTimeSeconds();
            using var cn = Open();
            const string sql = @"
SELECT path, filename, category, confidence, created_ts AS CreatedTs,
       summary, reasoning, status, tags, project
FROM items
WHERE created_ts >= @since
ORDER BY created_ts DESC;";
            return cn.Query<Item>(sql, new { since });
        }

        public IEnumerable<Item> ByStatus(string status)
        {
            using var cn = Open();
            const string sql = @"
SELECT path, filename, category, confidence, created_ts AS CreatedTs,
       summary, reasoning, status, tags, project
FROM items
WHERE LOWER(status) = LOWER(@status)
ORDER BY created_ts DESC;";
            return cn.Query<Item>(sql, new { status });
        }

        public IEnumerable<Item> Search(string keyword)
        {
            using var cn = Open();
            const string sql = @"
SELECT path, filename, category, confidence, created_ts AS CreatedTs,
       summary, reasoning, status, tags, project
FROM items
WHERE filename LIKE @q
   OR category LIKE @q
   OR summary  LIKE @q
   OR tags     LIKE @q
ORDER BY created_ts DESC;";
            var q = "%" + (keyword ?? "").Trim() + "%";
            return cn.Query<Item>(sql, new { q });
        }

        // A3 對話搜尋：依條件查詢
        public IEnumerable<Item> AdvancedSearch(
            string? keyword = null,
            IEnumerable<string>? categories = null,
            IEnumerable<string>? tags = null,
            long? fromUnix = null,
            long? toUnix = null)
        {
            using var cn = Open();

            var where = new List<string>();
            var p = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                where.Add("(filename LIKE @kw OR category LIKE @kw OR summary LIKE @kw OR tags LIKE @kw)");
                p.Add("@kw", "%" + keyword.Trim() + "%");
            }
            if (categories != null)
            {
                var list = categories.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
                if (list.Length > 0) { where.Add("category IN @cats"); p.Add("@cats", list); }
            }
            if (tags != null)
            {
                // SQLite 沒有陣列欄位：用 LIKE 近似（簡化）
                var tlist = tags.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
                if (tlist.Length > 0)
                {
                    var sub = new List<string>();
                    for (int i = 0; i < tlist.Length; i++)
                    {
                        var name = "@t" + i;
                        sub.Add($"tags LIKE {name}");
                        p.Add(name, "%" + tlist[i] + "%");
                    }
                    where.Add("(" + string.Join(" OR ", sub) + ")");
                }
            }
            if (fromUnix.HasValue) { where.Add("created_ts >= @from"); p.Add("@from", fromUnix.Value); }
            if (toUnix.HasValue) { where.Add("created_ts <= @to"); p.Add("@to", toUnix.Value); }

            var sql = $@"
SELECT path, filename, category, confidence, created_ts AS CreatedTs,
       summary, reasoning, status, tags, project
FROM items
{(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")}
ORDER BY created_ts DESC;";

            return cn.Query<Item>(sql, p);
        }

        public int UpdateStatusByPath(IEnumerable<string> paths, string status)
        {
            var list = (paths ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray();
            if (list.Length == 0) return 0;

            using var cn = Open();
            const string sql = @"UPDATE items SET status=@status WHERE path IN @paths;";
            return cn.Execute(sql, new { status, paths = list });
        }

        public void Dispose() { /* 交給連線池 */ }
    }
}
