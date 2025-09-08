using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
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

        /// <summary>初次建表（若不存在則建立）</summary>
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

            // 基本索引
            cn.Execute("""
                CREATE INDEX IF NOT EXISTS idx_items_filename  ON items(filename);
                CREATE INDEX IF NOT EXISTS idx_items_category  ON items(category);
                CREATE INDEX IF NOT EXISTS idx_items_status    ON items(status);
                CREATE INDEX IF NOT EXISTS idx_items_created   ON items(created_ts);
            """);
        }

        /// <summary>輕量 migration：補上 project 欄位與索引</summary>
        private void RunLightMigrations()
        {
            using var cn = Open();
            // 檢查 project 欄位是否存在
            bool hasProject = ColumnExists(cn, "items", "project");
            if (!hasProject)
            {
                cn.Execute("ALTER TABLE items ADD COLUMN project TEXT;");
                cn.Execute("CREATE INDEX IF NOT EXISTS idx_items_project ON items(project);");
            }
        }

        private static bool ColumnExists(IDbConnection cn, string table, string column)
        {
            // PRAGMA table_info 回傳：cid|name|type|notnull|dflt_value|pk
            var rows = cn.Query("PRAGMA table_info(" + table + ");");
            foreach (var r in rows)
            {
                // dynamic 取值
                string name = r.name;
                if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>插入一筆 Item 並回傳 rowid</summary>
        public long Add(Item it)
        {
            using var cn = Open();
            const string sql = @"
INSERT INTO items(path, filename, category, confidence, created_ts, summary, reasoning, status, tags, project)
VALUES(@Path, @Filename, @Category, @Confidence, @CreatedTs, @Summary, @Reasoning, @Status, @Tags, @Project);
SELECT last_insert_rowid();";
            return cn.ExecuteScalar<long>(sql, it);
        }

        /// <summary>近 N 天新增的項目（依時間新→舊）</summary>
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

        /// <summary>依狀態過濾（normal/todo/in-progress/favorite/pending）</summary>
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

        /// <summary>關鍵字搜尋：檔名 / 類別 / 摘要 / 標籤（LIKE 模糊比對）</summary>
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

        public void Dispose()
        {
            // 目前無需釋放，交由連線池處理
        }
    }
}
