using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Dapper;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public sealed class DbService : IDisposable
    {
        private readonly string _dbPath;
        private readonly SQLiteConnection _conn;

        public DbService(string dbPath)
        {
            _dbPath = dbPath;

            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var firstCreate = !File.Exists(_dbPath);

            _conn = new SQLiteConnection(new SQLiteConnectionStringBuilder
            {
                DataSource = _dbPath,
                JournalMode = SQLiteJournalModeEnum.Wal,
                SyncMode = SynchronizationModes.Normal,
                ForeignKeys = true
            }.ToString());
            _conn.Open();

            EnablePragmas();

            // 1) 確保有 table (舊版也會存在)
            EnsureTable();

            // 2) 先做欄位遷移（補 CreatedTs）
            MigrateColumnsIfNeeded();

            // 3) 最後再建索引（避免欄位不存在時出錯）
            EnsureIndexes();
        }

        private void EnablePragmas()
        {
            _conn.Execute("PRAGMA journal_mode=WAL;");
            _conn.Execute("PRAGMA synchronous=NORMAL;");
            _conn.Execute("PRAGMA foreign_keys=ON;");
        }

        private void EnsureTable()
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS Items
(
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Filename    TEXT,
    Ext         TEXT,
    Project     TEXT,
    Category    TEXT,
    Confidence  REAL,
    CreatedTs   INTEGER NOT NULL DEFAULT 0,
    Status      TEXT,
    Path        TEXT UNIQUE,
    Tags        TEXT,
    Reasoning   TEXT
);
";
            _conn.Execute(sql);
        }

        private HashSet<string> GetColumnNames(string table)
        {
            // PRAGMA table_info 回傳：cid | name | type | notnull | dflt_value | pk
            var rows = _conn.Query("PRAGMA table_info(" + table + ");");
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                try
                {
                    var name = (string)r.name;
                    if (!string.IsNullOrWhiteSpace(name)) set.Add(name);
                }
                catch { }
            }
            return set;
        }

        private void MigrateColumnsIfNeeded()
        {
            var cols = GetColumnNames("Items");

            // 補 CreatedTs 欄位
            if (!cols.Contains("CreatedTs"))
            {
                _conn.Execute("ALTER TABLE Items ADD COLUMN CreatedTs INTEGER NOT NULL DEFAULT 0;");
                cols.Add("CreatedTs");
            }

            // 補 Reasoning 欄位（如果你在 UI 會用到說明）
            if (!cols.Contains("Reasoning"))
            {
                _conn.Execute("ALTER TABLE Items ADD COLUMN Reasoning TEXT;");
                cols.Add("Reasoning");
            }

            // 把還是 0 的 CreatedTs 補值（用檔案建立時間，不到就用現在）
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            try
            {
                // 用 Path 讀取檔案時間
                var rows = _conn.Query<(long Id, string Path, long CreatedTs)>(
                    "SELECT Id, Path, IFNULL(CreatedTs,0) as CreatedTs FROM Items WHERE IFNULL(CreatedTs,0)=0;");

                foreach (var r in rows)
                {
                    long ts = 0;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(r.Path) && File.Exists(r.Path))
                            ts = new DateTimeOffset(File.GetCreationTimeUtc(r.Path)).ToUnixTimeSeconds();
                    }
                    catch { /* ignore file errors */ }

                    if (ts == 0) ts = now;
                    _conn.Execute("UPDATE Items SET CreatedTs=@ts WHERE Id=@id;", new { ts, id = r.Id });
                }
            }
            catch
            {
                // 萬一上面 fail，至少把 0 補現在時間
                _conn.Execute("UPDATE Items SET CreatedTs=@now WHERE IFNULL(CreatedTs,0)=0;", new { now });
            }
        }

        private void EnsureIndexes()
        {
            // 只有欄位存在才建索引
            var cols = GetColumnNames("Items");

            _conn.Execute("CREATE INDEX IF NOT EXISTS IX_Items_Status   ON Items(Status);");
            _conn.Execute("CREATE INDEX IF NOT EXISTS IX_Items_Project  ON Items(Project);");

            if (cols.Contains("CreatedTs"))
                _conn.Execute("CREATE INDEX IF NOT EXISTS IX_Items_CreatedTs ON Items(CreatedTs);");
        }

        // ---------------- CRUD / Query ----------------

        public bool TryGetByPath(string path, out Item? item)
        {
            item = _conn.QueryFirstOrDefault<Item>(
                "SELECT * FROM Items WHERE Path=@P LIMIT 1;", new { P = path });
            return item != null;
        }

        public Item Insert(Item it)
        {
            if (it.CreatedTs <= 0)
                it.CreatedTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            const string sql = @"
INSERT INTO Items(Filename, Ext, Project, Category, Confidence, CreatedTs, Status, Path, Tags, Reasoning)
VALUES(@Filename, @Ext, @Project, @Category, @Confidence, @CreatedTs, @Status, @Path, @Tags, @Reasoning);
SELECT last_insert_rowid();";
            it.Id = _conn.ExecuteScalar<long>(sql, it);
            return it;
        }

        /// <summary>存在就更新，否則插入（以 Path 為唯一鍵）。</summary>
        public Item UpsertItem(Item it)
        {
            if (TryGetByPath(it.Path!, out var exist) && exist != null)
            {
                // 沒給值就沿用舊值
                it.Id = exist.Id;
                it.Filename = string.IsNullOrWhiteSpace(it.Filename) ? exist.Filename : it.Filename;
                it.Ext = string.IsNullOrWhiteSpace(it.Ext) ? exist.Ext : it.Ext;
                it.Project = string.IsNullOrWhiteSpace(it.Project) ? exist.Project : it.Project;
                it.Category = string.IsNullOrWhiteSpace(it.Category) ? exist.Category : it.Category;
                it.Tags = string.IsNullOrWhiteSpace(it.Tags) ? exist.Tags : it.Tags;
                it.Status = string.IsNullOrWhiteSpace(it.Status) ? exist.Status : it.Status;
                it.Reasoning = string.IsNullOrWhiteSpace(it.Reasoning) ? exist.Reasoning : it.Reasoning;
                if (it.Confidence <= 0) it.Confidence = exist.Confidence;
                if (it.CreatedTs <= 0) it.CreatedTs = exist.CreatedTs;

                const string upd = @"
UPDATE Items
SET Filename=@Filename, Ext=@Ext, Project=@Project, Category=@Category,
    Confidence=@Confidence, CreatedTs=@CreatedTs, Status=@Status, Tags=@Tags, Reasoning=@Reasoning
WHERE Id=@Id;";
                _conn.Execute(upd, it);
                return it;
            }
            else
            {
                return Insert(it);
            }
        }

        public void UpdateStatus(long id, string status)
            => _conn.Execute("UPDATE Items SET Status=@s WHERE Id=@id;", new { s = status, id });

        public void UpdateProject(long id, string project)
            => _conn.Execute("UPDATE Items SET Project=@p WHERE Id=@id;", new { p = project, id });

        public void UpdateTags(long id, string tags)
            => _conn.Execute("UPDATE Items SET Tags=@t WHERE Id=@id;", new { t = tags, id });

        public IEnumerable<Item> QueryByStatus(string status)
        {
            return _conn.Query<Item>(
                "SELECT * FROM Items WHERE Status=@S ORDER BY IFNULL(CreatedTs,0) DESC;",
                new { S = status });
        }

        public IEnumerable<Item> QuerySince(long sinceEpochSeconds)
        {
            return _conn.Query<Item>(
                "SELECT * FROM Items WHERE IFNULL(CreatedTs,0) >= @S ORDER BY IFNULL(CreatedTs,0) DESC;",
                new { S = sinceEpochSeconds });
        }

        public IEnumerable<string> QueryDistinctProjects(string? keyword = null)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return _conn.Query<string>("SELECT DISTINCT Project FROM Items WHERE IFNULL(Project,'')<>'' ORDER BY Project COLLATE NOCASE;");

            return _conn.Query<string>(
                "SELECT DISTINCT Project FROM Items WHERE IFNULL(Project,'')<>'' AND Project LIKE @K ORDER BY Project COLLATE NOCASE;",
                new { K = $"%{keyword}%" });
        }

        public void Dispose()
        {
            try { _conn?.Close(); } catch { }
            try { _conn?.Dispose(); } catch { }
        }
    }
}
