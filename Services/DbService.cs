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
    /// <summary>SQLite 存取層：建表、查詢、狀態更新</summary>
    public sealed class DbService : IDisposable
    {
        private readonly string _connStr;

        public DbService(string dbPath)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath))!;
            Directory.CreateDirectory(dir);
            _connStr = $"Data Source={dbPath}";
            EnsureTables();
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
                CREATE INDEX IF NOT EXISTS idx_items_created ON items(created_ts);
                CREATE INDEX IF NOT EXISTS idx_items_status  ON items(status);
                CREATE INDEX IF NOT EXISTS idx_items_cat     ON items(category);
                CREATE INDEX IF NOT EXISTS idx_items_proj    ON items(project);
            """);
        }

        public long Add(Item it)
        {
            using var cn = Open();
            return cn.ExecuteScalar<long>(
                @"INSERT INTO items(path,filename,category,confidence,created_ts,summary,reasoning,status,tags,project)
                  VALUES(@Path,@Filename,@Category,@Confidence,@CreatedTs,@Summary,@Reasoning,@Status,@Tags,@Project);
                  SELECT last_insert_rowid();", it);
        }

        public IEnumerable<Item> Recent(int days = 7)
        {
            var since = DateTimeOffset.Now.AddDays(-Math.Abs(days)).ToUnixTimeSeconds();
            using var cn = Open();
            return cn.Query<Item>(
                @"SELECT path,filename,category,confidence,created_ts AS CreatedTs,summary,reasoning,status,tags,project
                  FROM items WHERE created_ts >= @since ORDER BY created_ts DESC;", new { since });
        }

        public IEnumerable<Item> ByStatus(string status)
        {
            using var cn = Open();
            return cn.Query<Item>(
                @"SELECT path,filename,category,confidence,created_ts AS CreatedTs,summary,reasoning,status,tags,project
                  FROM items WHERE LOWER(status)=LOWER(@status) ORDER BY created_ts DESC;", new { status });
        }

        public IEnumerable<Item> Search(string keyword)
        {
            var q = "%" + (keyword ?? "").Trim() + "%";
            using var cn = Open();
            return cn.Query<Item>(
                @"SELECT path,filename,category,confidence,created_ts AS CreatedTs,summary,reasoning,status,tags,project
                  FROM items
                  WHERE filename LIKE @q OR category LIKE @q OR summary LIKE @q OR tags LIKE @q OR project LIKE @q
                  ORDER BY created_ts DESC;", new { q });
        }

        public int UpdateStatusByPath(IEnumerable<string> paths, string status)
        {
            var list = (paths ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray();
            if (list.Length == 0) return 0;
            using var cn = Open();
            return cn.Execute(@"UPDATE items SET status=@status WHERE path IN @paths;", new { status, paths = list });
        }

        public void Dispose() { }
    }
}
