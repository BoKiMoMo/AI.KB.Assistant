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
        private readonly string _dbPath;
        private readonly string _connStr;

        public DbService(string dbPath)
        {
            _dbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir!);

            _connStr = $"Data Source={_dbPath}";
            EnsureTables();
            RunLightMigrations();
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
    confidence   REAL,
    created_ts   INTEGER,
    summary      TEXT,
    reasoning    TEXT,
    status       TEXT,
    tags         TEXT,
    project      TEXT
);
CREATE INDEX IF NOT EXISTS idx_items_filename  ON items(filename);
CREATE INDEX IF NOT EXISTS idx_items_category  ON items(category);
CREATE INDEX IF NOT EXISTS idx_items_status    ON items(status);
CREATE INDEX IF NOT EXISTS idx_items_created   ON items(created_ts);
CREATE INDEX IF NOT EXISTS idx_items_project   ON items(project);
");
        }

        private void RunLightMigrations()
        {
            // 這裡可視需要做欄位補齊，現在表已完整
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

        /// <summary>批次更新 status（我的最愛 = favorite 也走這裡）</summary>
        public int UpdateStatusByPath(IEnumerable<string> paths, string status)
        {
            var list = (paths ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray();
            if (list.Length == 0) return 0;

            using var cn = Open();
            const string sql = @"UPDATE items SET status=@status WHERE path IN @paths;";
            return cn.Execute(sql, new { status, paths = list });
        }

        /// <summary>批次更新 tags（逗號分隔字串）</summary>
        public int UpdateTagsByPath(IEnumerable<string> paths, string tags)
        {
            var list = (paths ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray();
            if (list.Length == 0) return 0;

            using var cn = Open();
            const string sql = @"UPDATE items SET tags=@tags WHERE path IN @paths;";
            return cn.Execute(sql, new { tags, paths = list });
        }

        public void Dispose()
        {
            // using 連線池，無需特別釋放
        }
    }
}
