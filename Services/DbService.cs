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
        }

        private IDbConnection Open() => new SqliteConnection(_connStr);

        private void EnsureTables()
        {
            using var cn = Open();
            cn.Execute(@"
CREATE TABLE IF NOT EXISTS items(
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    path         TEXT NOT NULL,
    filename     TEXT NOT NULL,
    category     TEXT,
    filetype     TEXT,
    confidence   REAL,
    created_ts   INTEGER,
    year         INTEGER,
    summary      TEXT,
    reasoning    TEXT,
    status       TEXT,
    tags         TEXT,
    project      TEXT,
    scope_locked INTEGER DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_items_filename  ON items(filename);
CREATE INDEX IF NOT EXISTS idx_items_category  ON items(category);
CREATE INDEX IF NOT EXISTS idx_items_status    ON items(status);
CREATE INDEX IF NOT EXISTS idx_items_created   ON items(created_ts);
CREATE INDEX IF NOT EXISTS idx_items_project   ON items(project);
");

            // 兼容舊版：嘗試補欄位（忽略失敗）
            TryAddColumn(cn, "items", "filetype", "TEXT");
            TryAddColumn(cn, "items", "year", "INTEGER");
            TryAddColumn(cn, "items", "scope_locked", "INTEGER DEFAULT 0");
        }

        private static void TryAddColumn(IDbConnection cn, string table, string col, string defType)
        {
            try
            {
                var exists = cn.Query<string>($"PRAGMA table_info({table});")
                               .Any(r => string.Equals(r?.Split('|').ElementAtOrDefault(1) ?? "", col, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                    cn.Execute($"ALTER TABLE {table} ADD COLUMN {col} {defType};");
            }
            catch { /* ignore */ }
        }

        public long Add(Item it)
        {
            using var cn = Open();
            const string sql = @"
INSERT INTO items(path, filename, category, filetype, confidence, created_ts, year, summary, reasoning, status, tags, project, scope_locked)
VALUES(@Path, @Filename, @Category, @FileType, @Confidence, @CreatedTs, @Year, @Summary, @Reasoning, @Status, @Tags, @Project, @ScopeLocked);
SELECT last_insert_rowid();";
            return cn.ExecuteScalar<long>(sql, it);
        }

        public IEnumerable<Item> Recent(int days = 14)
        {
            var since = DateTimeOffset.Now.AddDays(-Math.Abs(days)).ToUnixTimeSeconds();
            using var cn = Open();
            const string sql = @"
SELECT path, filename, category, filetype AS FileType, confidence,
       created_ts AS CreatedTs, year, summary, reasoning, status, tags, project, scope_locked AS ScopeLocked
FROM items
WHERE created_ts >= @since
ORDER BY created_ts DESC;";
            return cn.Query<Item>(sql, new { since });
        }

        public IEnumerable<Item> ByStatus(string status)
        {
            using var cn = Open();
            const string sql = @"
SELECT path, filename, category, filetype AS FileType, confidence,
       created_ts AS CreatedTs, year, summary, reasoning, status, tags, project, scope_locked AS ScopeLocked
FROM items
WHERE LOWER(status) = LOWER(@status)
ORDER BY created_ts DESC;";
            return cn.Query<Item>(sql, new { status });
        }

        public IEnumerable<Item> Search(string keyword)
        {
            using var cn = Open();
            const string sql = @"
SELECT path, filename, category, filetype AS FileType, confidence,
       created_ts AS CreatedTs, year, summary, reasoning, status, tags, project, scope_locked AS ScopeLocked
FROM items
WHERE filename LIKE @q OR category LIKE @q OR summary LIKE @q OR tags LIKE @q OR project LIKE @q
ORDER BY created_ts DESC;";
            var q = "%" + (keyword ?? "").Trim() + "%";
            return cn.Query<Item>(sql, new { q });
        }

        public IEnumerable<Item> SearchAdvanced(string? filenameLike, string? category, string? status, string? tag)
        {
            using var cn = Open();
            var where = new List<string>();
            var p = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(filenameLike))
            {
                var like = filenameLike.Replace('*', '%');
                where.Add("filename LIKE @f");
                p.Add("f", like);
            }
            if (!string.IsNullOrWhiteSpace(category))
            {
                where.Add("LOWER(category) = LOWER(@c)");
                p.Add("c", category);
            }
            if (!string.IsNullOrWhiteSpace(status))
            {
                where.Add("LOWER(status) = LOWER(@s)");
                p.Add("s", status);
            }
            if (!string.IsNullOrWhiteSpace(tag))
            {
                where.Add("tags LIKE @t");
                p.Add("t", "%" + tag + "%");
            }

            var sql = $@"
SELECT path, filename, category, filetype AS FileType, confidence,
       created_ts AS CreatedTs, year, summary, reasoning, status, tags, project, scope_locked AS ScopeLocked
FROM items
{(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")}
ORDER BY created_ts DESC;";

            return cn.Query<Item>(sql, p);
        }

        public int UpdateStatusByPath(IEnumerable<string> paths, string status)
        {
            var list = paths?.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray() ?? Array.Empty<string>();
            if (list.Length == 0) return 0;
            using var cn = Open();
            const string sql = @"UPDATE items SET status=@status WHERE path IN @paths;";
            return cn.Execute(sql, new { status, paths = list });
        }

        public int UpdateTagsByPath(IEnumerable<string> paths, string tags)
        {
            var list = paths?.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray() ?? Array.Empty<string>();
            if (list.Length == 0) return 0;
            using var cn = Open();
            const string sql = @"UPDATE items SET tags=@tags WHERE path IN @paths;";
            return cn.Execute(sql, new { tags, paths = list });
        }

        public int UpdateCategoryByPath(IEnumerable<string> paths, string category, double confidence)
        {
            var list = paths?.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray() ?? Array.Empty<string>();
            if (list.Length == 0) return 0;
            using var cn = Open();
            const string sql = @"UPDATE items SET category=@category, confidence=@confidence WHERE path IN @paths;";
            return cn.Execute(sql, new { category, confidence, paths = list });
        }

        public void Dispose() { }
    }
}
