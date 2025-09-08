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
    /// <summary>
    /// SQLite 存取層：建表、輕量 migration、常用查詢
    /// </summary>
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
                    tags         TEXT
                    -- project 欄位由 migration 補上
                );
            """);

            cn.Execute("""
                CREATE INDEX IF NOT EXISTS idx_items_filename  ON items(filename);
                CREATE INDEX IF NOT EXISTS idx_items_category  ON items(category);
                CREATE INDEX IF NOT EXISTS idx_items_status    ON items(status);
                CREATE INDEX IF NOT EXISTS idx_items_created   ON items(created_ts);
            """);
        }

        private void RunLightMigrations()
        {
            using var cn = Open();
            bool hasProject = ColumnExists(cn, "items", "project");
            if (!hasProject)
            {
                cn.Execute("ALTER TABLE items ADD COLUMN project TEXT;");
                cn.Execute("CREATE INDEX IF NOT EXISTS idx_items_project ON items(project);");
            }
        }

        private static bool ColumnExists(IDbConnection cn, string table, string column)
        {
            var rows = cn.Query("PRAGMA table_info(" + table + ");");
            foreach (var r in rows)
            {
                string name = r.name;
                if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
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

        /// <summary>
        /// 批次更新 selected 項目的狀態（供右鍵選單使用）
        /// </summary>
        public int UpdateStatusByPath(IEnumerable<string> paths, string status)
        {
            var list = (paths ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray();
            if (list.Length == 0) return 0;

            using var cn = Open();
            const string sql = @"UPDATE items SET status=@status WHERE path IN @paths;";
            return cn.Execute(sql, new { status, paths = list });
        }

        public void Dispose()
        {
            // 目前無需釋放，交由連線池處理
        }
    }
}
