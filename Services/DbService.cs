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
    /// 輕量 SQLite + Dapper 資料層。集中處理 Item CRUD 與常用查詢。
    /// </summary>
    public sealed class DbService : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _conn;

        public DbService(string dbPath)
        {
            _dbPath = string.IsNullOrWhiteSpace(dbPath)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data.db")
                : dbPath;

            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

            _conn = new SqliteConnection($"Data Source={_dbPath};Cache=Shared");
            _conn.Open();
            EnsureSchema();
        }

        private void EnsureSchema()
        {
            const string sql = @"
PRAGMA journal_mode=WAL;

CREATE TABLE IF NOT EXISTS items(
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    path          TEXT UNIQUE NOT NULL,
    filename      TEXT,
    filetype      TEXT,
    category      TEXT,
    project       TEXT,
    confidence    REAL,
    reasoning     TEXT,
    status        TEXT,                 -- inbox/pending/auto-sorted/favorite/in-progress/blacklisted
    tags          TEXT,
    created_ts    INTEGER,
    proposed_path TEXT                  -- UI 預覽用
);

CREATE INDEX IF NOT EXISTS idx_items_status    ON items(status);
CREATE INDEX IF NOT EXISTS idx_items_created   ON items(created_ts);
CREATE INDEX IF NOT EXISTS idx_items_project   ON items(project);
CREATE INDEX IF NOT EXISTS idx_items_category  ON items(category);
";
            _conn.Execute(sql);
        }

        public void Dispose()
        {
            try { _conn?.Dispose(); } catch { }
        }

        // ------------------ 基本工具 ------------------

        public bool TryGetByPath(string path, out Item? item)
        {
            const string sql = "SELECT * FROM items WHERE path=@path LIMIT 1;";
            item = _conn.QueryFirstOrDefault<Item>(sql, new { path });
            return item != null;
        }

        public bool TryGetById(int id, out Item? item)
        {
            const string sql = "SELECT * FROM items WHERE id=@id LIMIT 1;";
            item = _conn.QueryFirstOrDefault<Item>(sql, new { id });
            return item != null;
        }

        // ------------------ CUD ------------------

        public int InsertItem(Item it)
        {
            if (it.CreatedTs <= 0) it.CreatedTs = DateTimeOffset.Now.ToUnixTimeSeconds();

            const string sql = @"
INSERT INTO items(path, filename, filetype, category, project, confidence, reasoning, status, tags, created_ts, proposed_path)
VALUES(@Path, @Filename, @FileType, @Category, @Project, @Confidence, @Reasoning, @Status, @Tags, @CreatedTs, @ProposedPath);
SELECT last_insert_rowid();";
            return _conn.ExecuteScalar<int>(sql, it);
        }

        /// <summary>
        /// 以 Path 為唯一鍵做 Upsert。回傳最終 id。
        /// </summary>
        public int UpsertItem(Item it)
        {
            if (string.IsNullOrWhiteSpace(it.Path))
                throw new ArgumentException("Item.Path 不可空白", nameof(it));

            if (TryGetByPath(it.Path, out var exist) && exist != null)
            {
                const string up = @"
UPDATE items
   SET filename=@Filename,
       filetype=@FileType,
       category=@Category,
       project=@Project,
       confidence=@Confidence,
       reasoning=@Reasoning,
       status=@Status,
       tags=@Tags,
       proposed_path=@ProposedPath
 WHERE path=@Path;";
                _conn.Execute(up, it);
                return exist.Id;
            }
            else
            {
                return InsertItem(it);
            }
        }

        public void UpdateProject(int id, string project)
        {
            const string sql = "UPDATE items SET project=@project WHERE id=@id;";
            _conn.Execute(sql, new { id, project });
        }

        public void UpdateTags(int id, string tags)
        {
            const string sql = "UPDATE items SET tags=@tags WHERE id=@id;";
            _conn.Execute(sql, new { id, tags });
        }

        public void UpdateStatus(int id, string status)
        {
            const string sql = "UPDATE items SET status=@status WHERE id=@id;";
            _conn.Execute(sql, new { id, status });
        }

        public void UpdateProposedPath(int id, string? path)
        {
            const string sql = "UPDATE items SET proposed_path=@path WHERE id=@id;";
            _conn.Execute(sql, new { id, path });
        }

        // ------------------ 查詢 ------------------

        public IEnumerable<Item> QueryByStatus(string status)
        {
            const string sql = @"SELECT * FROM items WHERE status=@status ORDER BY created_ts DESC;";
            return _conn.Query<Item>(sql, new { status });
        }

        public IEnumerable<Item> QuerySince(long sinceTs)
        {
            const string sql = @"SELECT * FROM items WHERE created_ts>=@since ORDER BY created_ts DESC;";
            return _conn.Query<Item>(sql, new { since = sinceTs });
        }

        public IEnumerable<string> QueryDistinctProjects(string? keyword = null)
        {
            var sql = @"SELECT DISTINCT COALESCE(project,'') AS project FROM items WHERE COALESCE(project,'')<>''";
            if (!string.IsNullOrWhiteSpace(keyword))
                sql += " AND project LIKE @kw";

            sql += " ORDER BY project COLLATE NOCASE ASC;";

            return _conn.Query<string>(sql, new { kw = $"%{keyword}%" });
        }

        public IEnumerable<Item> QueryAll()
        {
            const string sql = @"SELECT * FROM items ORDER BY created_ts DESC;";
            return _conn.Query<Item>(sql);
        }
    }
}
