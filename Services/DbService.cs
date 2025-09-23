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
    /// SQLite 輕量資料層：items 表（索引與欄位與第二階段一致）
    /// </summary>
    public sealed class DbService : IDisposable
    {
        private readonly string _connStr;

        public DbService(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentException("dbPath is required.", nameof(dbPath));

            var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir!);

            _connStr = $"Data Source={dbPath}";
            EnsureTables();
        }

        private IDbConnection Open() => new SqliteConnection(_connStr);

        /// <summary>建立/升級資料表與索引（若不存在）</summary>
        private void EnsureTables()
        {
            using var cn = Open();

            // items 表：與第二階段欄位對齊
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
CREATE INDEX IF NOT EXISTS idx_items_filename ON items(filename);
CREATE INDEX IF NOT EXISTS idx_items_category ON items(category);
CREATE INDEX IF NOT EXISTS idx_items_status   ON items(status);
CREATE INDEX IF NOT EXISTS idx_items_created  ON items(created_ts);
CREATE INDEX IF NOT EXISTS idx_items_project  ON items(project);
");
        }

        #region CRUD / Query

        public long Add(Item it)
        {
            const string sql = @"
INSERT INTO items(path, filename, category, confidence, created_ts, summary, reasoning, status, tags, project)
VALUES(@Path, @Filename, @Category, @Confidence, @CreatedTs, @Summary, @Reasoning, @Status, @Tags, @Project);
SELECT last_insert_rowid();";
            using var cn = Open();
            return cn.ExecuteScalar<long>(sql, it);
        }

        public IEnumerable<Item> Recent(int days = 14)
        {
            var since = DateTimeOffset.Now.AddDays(-Math.Abs(days)).ToUnixTimeSeconds();
            const string sql = @"
SELECT path, filename, category, confidence, created_ts AS CreatedTs,
       summary, reasoning, status, tags, project
FROM items
WHERE created_ts >= @since
ORDER BY created_ts DESC;";
            using var cn = Open();
            return cn.Query<Item>(sql, new { since });
        }

        public IEnumerable<Item> ByStatus(string status)
        {
            const string sql = @"
SELECT path, filename, category, confidence, created_ts AS CreatedTs,
       summary, reasoning, status, tags, project
FROM items
WHERE LOWER(status)=LOWER(@status)
ORDER BY created_ts DESC;";
            using var cn = Open();
            return cn.Query<Item>(sql, new { status });
        }

        public IEnumerable<Item> Search(string keyword)
        {
            var q = "%" + (keyword ?? "").Trim() + "%";
            const string sql = @"
SELECT path, filename, category, confidence, created_ts AS CreatedTs,
       summary, reasoning, status, tags, project
FROM items
WHERE filename LIKE @q OR category LIKE @q OR summary LIKE @q OR tags LIKE @q OR project LIKE @q
ORDER BY created_ts DESC;";
            using var cn = Open();
            return cn.Query<Item>(sql, new { q });
        }

        /// <summary>
        /// 進階搜尋（MainWindow 呼叫版本）：
        /// filenameLike：支援 * 萬用字元（自動轉成 %）
        /// category/status/tag：完全比對（不分大小寫）
        /// </summary>
        public IEnumerable<Item> SearchAdvanced(string? filenameLike, string? category, string? status, string? tag)
            => SearchAdvanced(filenameLike, category, status, tag, project: null);

        /// <summary>
        /// 進階搜尋（含 project 過濾的擴充版本，供未來 UI 使用）
        /// </summary>
        public IEnumerable<Item> SearchAdvanced(string? filenameLike, string? category, string? status, string? tag, string? project)
        {
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
            if (!string.IsNullOrWhiteSpace(project))
            {
                where.Add("LOWER(project) = LOWER(@p)");
                p.Add("p", project);
            }

            var sql = $@"
SELECT path, filename, category, confidence, created_ts AS CreatedTs,
       summary, reasoning, status, tags, project
FROM items
{(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")}
ORDER BY created_ts DESC;";

            using var cn = Open();
            return cn.Query<Item>(sql, p);
        }

        #endregion

        #region Batch Updates (by path)

        public int UpdateStatusByPath(IEnumerable<string> paths, string status)
        {
            var list = NormalizePathList(paths);
            if (list.Length == 0) return 0;

            const string sql = @"UPDATE items SET status=@status WHERE path IN @paths;";
            using var cn = Open();
            return cn.Execute(sql, new { status, paths = list });
        }

        public int UpdateTagsByPath(IEnumerable<string> paths, string tags)
        {
            var list = NormalizePathList(paths);
            if (list.Length == 0) return 0;

            const string sql = @"UPDATE items SET tags=@tags WHERE path IN @paths;";
            using var cn = Open();
            return cn.Execute(sql, new { tags, paths = list });
        }

        public int UpdateCategoryByPath(IEnumerable<string> paths, string category, double confidence)
        {
            var list = NormalizePathList(paths);
            if (list.Length == 0) return 0;

            const string sql = @"UPDATE items SET category=@category, confidence=@confidence WHERE path IN @paths;";
            using var cn = Open();
            return cn.Execute(sql, new { category, confidence, paths = list });
        }

        #endregion

        private static string[] NormalizePathList(IEnumerable<string> paths)
            => paths?.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToArray() ?? Array.Empty<string>();

        public void Dispose()
        {
            // using 連線；無需額外釋放
        }
    }
}
