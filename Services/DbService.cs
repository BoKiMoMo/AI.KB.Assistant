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
            _dbPath = string.IsNullOrWhiteSpace(dbPath) ? "data.db" : dbPath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_dbPath)) ?? ".");
            _connStr = $"Data Source={_dbPath};Cache=Shared";
            EnsureSchema();
        }

        public void Dispose() { }

        private IDbConnection Open()
        {
            var c = new SqliteConnection(_connStr);
            c.Open();
            return c;
        }

        private void EnsureSchema()
        {
            using var c = Open();
            c.Execute(@"
CREATE TABLE IF NOT EXISTS Items(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Filename   TEXT,
    Ext        TEXT,
    Project    TEXT,
    Category   TEXT,
    Confidence REAL,
    CreatedTs  INTEGER,
    Status     TEXT,
    Path       TEXT,
    Tags       TEXT
);
CREATE INDEX IF NOT EXISTS IX_Items_Status     ON Items(Status);
CREATE INDEX IF NOT EXISTS IX_Items_CreatedTs  ON Items(CreatedTs);
CREATE INDEX IF NOT EXISTS IX_Items_Project    ON Items(Project);
CREATE INDEX IF NOT EXISTS IX_Items_Path       ON Items(Path);
");
        }

        // ---------- 基礎包裝 ----------
        public IEnumerable<T> Query<T>(string sql, object? args = null)
        {
            using var c = Open();
            return c.Query<T>(sql, args);
        }

        public int Execute(string sql, object? args = null)
        {
            using var c = Open();
            return c.Execute(sql, args);
        }

        public T ExecuteScalar<T>(string sql, object? args = null)
        {
            using var c = Open();
            return c.ExecuteScalar<T>(sql, args);
        }

        // ---------- Upsert / Update ----------
        public void Upsert(Item item) => UpsertItem(item);

        public void UpsertItem(Item item)
        {
            using var c = Open();
            if (item.Id > 0)
            {
                c.Execute(@"UPDATE Items SET
                    Filename=@Filename, Ext=@Ext, Project=@Project, Category=@Category,
                    Confidence=@Confidence, CreatedTs=@CreatedTs, Status=@Status, Path=@Path, Tags=@Tags
                    WHERE Id=@Id", item);
            }
            else
            {
                var id = c.ExecuteScalar<long>(@"
INSERT INTO Items(Filename,Ext,Project,Category,Confidence,CreatedTs,Status,Path,Tags)
VALUES (@Filename,@Ext,@Project,@Category,@Confidence,@CreatedTs,@Status,@Path,@Tags);
SELECT last_insert_rowid();", item);
                item.Id = id;
            }
        }

        public void UpdateProject(long id, string project)
        {
            using var c = Open();
            c.Execute("UPDATE Items SET Project=@project WHERE Id=@id", new { id, project });
        }

        public void UpdateTags(long id, string tags)
        {
            using var c = Open();
            c.Execute("UPDATE Items SET Tags=@tags WHERE Id=@id", new { id, tags });
        }

        // ---------- 查詢 ----------
        public IEnumerable<Item> QueryByStatus(string status)
            => Query<Item>("SELECT * FROM Items WHERE Status=@s ORDER BY CreatedTs DESC", new { s = status });

        public IEnumerable<Item> QueryByStatuses(IEnumerable<string> statuses)
            => Query<Item>("SELECT * FROM Items WHERE Status IN @st ORDER BY CreatedTs DESC", new { st = statuses });

        public IEnumerable<Item> QuerySince(long since)
            => Query<Item>("SELECT * FROM Items WHERE CreatedTs>=@since ORDER BY CreatedTs DESC", new { since });

        public IEnumerable<string> QueryDistinctProjects(string? keyword = null)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return Query<string>("SELECT DISTINCT Project FROM Items WHERE ifnull(Project,'')<>'' ORDER BY Project COLLATE NOCASE");
            var kw = $"%{keyword}%";
            return Query<string>("SELECT DISTINCT Project FROM Items WHERE ifnull(Project,'')<>'' AND Project LIKE @kw ORDER BY Project COLLATE NOCASE", new { kw });
        }

        public IEnumerable<Item> QueryByPath(string path)
            => Query<Item>("SELECT * FROM Items WHERE Path=@path", new { path });

        // ---------- 清理不存在的實體檔案 ----------
        public int PurgeMissing()
        {
            using var c = Open();
            // 只抓必要欄位以提速
            var rows = c.Query<(long Id, string Path)>("SELECT Id, Path FROM Items").ToList();
            var toDelete = rows.Where(r =>
            {
                try { return string.IsNullOrWhiteSpace(r.Path) || !File.Exists(r.Path); }
                catch { return true; }
            }).Select(r => r.Id).ToList();

            if (toDelete.Count == 0) return 0;

            // Dapper IN 支援
            c.Execute("DELETE FROM Items WHERE Id IN @ids", new { ids = toDelete });
            return toDelete.Count;
        }
    }
}
