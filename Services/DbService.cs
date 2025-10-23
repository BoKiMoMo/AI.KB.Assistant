using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Data.SQLite;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    public sealed class DbService : IDisposable
    {
        private readonly string _dbPath;
        private readonly SQLiteConnection _conn;

        public DbService(string dbPath)
        {
            _dbPath = string.IsNullOrWhiteSpace(dbPath) ? "data.db" : dbPath;

            var dir = Path.GetDirectoryName(Path.GetFullPath(_dbPath));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var needInit = !File.Exists(_dbPath);

            _conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;Pooling=True;Journal Mode=WAL;");
            _conn.Open();

            if (needInit) InitSchema();
            else EnsureSchema();
        }

        private void InitSchema()
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS items (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    path        TEXT UNIQUE,
    filename    TEXT,
    ext         TEXT,
    project     TEXT,
    category    TEXT,
    tags        TEXT,
    status      TEXT,
    confidence  REAL,
    created_ts  INTEGER
);
CREATE INDEX IF NOT EXISTS idx_items_status     ON items(status);
CREATE INDEX IF NOT EXISTS idx_items_project    ON items(project);
CREATE INDEX IF NOT EXISTS idx_items_tags       ON items(tags);
CREATE INDEX IF NOT EXISTS idx_items_created_ts ON items(created_ts DESC);
";
            cmd.ExecuteNonQuery();
        }

        private void EnsureSchema()
        {
            var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "id","path","filename","ext","project","category","tags","status","confidence","created_ts" };

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(items)";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    existing.Add(Convert.ToString(r["name"]) ?? "");
            }

            foreach (var name in required.Except(existing))
            {
                string alter = name switch
                {
                    "confidence" => "ALTER TABLE items ADD COLUMN confidence REAL;",
                    "created_ts" => "ALTER TABLE items ADD COLUMN created_ts INTEGER;",
                    "project" => "ALTER TABLE items ADD COLUMN project TEXT;",
                    "category" => "ALTER TABLE items ADD COLUMN category TEXT;",
                    "tags" => "ALTER TABLE items ADD COLUMN tags TEXT;",
                    "status" => "ALTER TABLE items ADD COLUMN status TEXT;",
                    "filename" => "ALTER TABLE items ADD COLUMN filename TEXT;",
                    "ext" => "ALTER TABLE items ADD COLUMN ext TEXT;",
                    "path" => "ALTER TABLE items ADD COLUMN path TEXT;",
                    _ => ""
                };
                if (!string.IsNullOrWhiteSpace(alter))
                {
                    using var cmd = _conn.CreateCommand();
                    cmd.CommandText = alter;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static Item Map(IDataRecord r)
        {
            return new Item
            {
                Id = r["id"] is DBNull ? 0 : Convert.ToInt64(r["id"]),
                Path = r["path"] as string,
                Filename = r["filename"] as string,
                Ext = r["ext"] as string,
                Project = r["project"] as string,
                Category = r["category"] as string,
                Tags = r["tags"] as string,
                Status = r["status"] as string,
                Confidence = r["confidence"] is DBNull ? 0 : Convert.ToDouble(r["confidence"]),
                CreatedTs = r["created_ts"] is DBNull ? 0 : Convert.ToInt64(r["created_ts"])
            };
        }

        private static string NormalizeTags(string? raw)
        {
            var list = (raw ?? "")
                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return string.Join(",", list);
        }

        // ---------------- Upsert / Update ----------------

        public void UpsertItem(Item it)
        {
            if (it == null) return;
            it.Tags = NormalizeTags(it.Tags);

            if (string.IsNullOrWhiteSpace(it.Filename) && !string.IsNullOrWhiteSpace(it.Path))
                it.Filename = Path.GetFileName(it.Path);
            if (string.IsNullOrWhiteSpace(it.Ext) && !string.IsNullOrWhiteSpace(it.Path))
                it.Ext = Path.GetExtension(it.Path)?.TrimStart('.').ToLowerInvariant();

            if (it.CreatedTs <= 0)
                it.CreatedTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            using var tx = _conn.BeginTransaction();

            long? existingId = null;

            if (it.Id > 0)
            {
                using var q1 = _conn.CreateCommand();
                q1.CommandText = "SELECT id FROM items WHERE id=@id LIMIT 1";
                q1.Parameters.AddWithValue("@id", it.Id);
                var obj = q1.ExecuteScalar();
                if (obj != null && obj != DBNull.Value) existingId = Convert.ToInt64(obj);
            }

            if (existingId == null && !string.IsNullOrWhiteSpace(it.Path))
            {
                using var q2 = _conn.CreateCommand();
                q2.CommandText = "SELECT id FROM items WHERE path=@path LIMIT 1";
                q2.Parameters.AddWithValue("@path", it.Path);
                var obj = q2.ExecuteScalar();
                if (obj != null && obj != DBNull.Value) existingId = Convert.ToInt64(obj);
            }

            if (existingId == null)
            {
                using var ins = _conn.CreateCommand();
                ins.CommandText = @"
INSERT INTO items (path, filename, ext, project, category, tags, status, confidence, created_ts)
VALUES (@path, @filename, @ext, @project, @category, @tags, @status, @confidence, @created_ts);
SELECT last_insert_rowid();
";
                ins.Parameters.AddWithValue("@path", (object?)it.Path ?? DBNull.Value);
                ins.Parameters.AddWithValue("@filename", (object?)it.Filename ?? DBNull.Value);
                ins.Parameters.AddWithValue("@ext", (object?)it.Ext ?? DBNull.Value);
                ins.Parameters.AddWithValue("@project", (object?)it.Project ?? DBNull.Value);
                ins.Parameters.AddWithValue("@category", (object?)it.Category ?? DBNull.Value);
                ins.Parameters.AddWithValue("@tags", (object?)it.Tags ?? DBNull.Value);
                ins.Parameters.AddWithValue("@status", (object?)it.Status ?? DBNull.Value);
                ins.Parameters.AddWithValue("@confidence", it.Confidence);
                ins.Parameters.AddWithValue("@created_ts", it.CreatedTs);

                var newId = ins.ExecuteScalar();
                it.Id = Convert.ToInt64(newId);
            }
            else
            {
                it.Id = existingId.Value;

                using var upd = _conn.CreateCommand();
                upd.CommandText = @"
UPDATE items SET
    path        = @path,
    filename    = @filename,
    ext         = @ext,
    project     = @project,
    category    = @category,
    tags        = @tags,
    status      = @status,
    confidence  = @confidence,
    created_ts  = CASE WHEN COALESCE(created_ts,0)=0 THEN @created_ts ELSE created_ts END
WHERE id=@id;
";
                upd.Parameters.AddWithValue("@id", it.Id);
                upd.Parameters.AddWithValue("@path", (object?)it.Path ?? DBNull.Value);
                upd.Parameters.AddWithValue("@filename", (object?)it.Filename ?? DBNull.Value);
                upd.Parameters.AddWithValue("@ext", (object?)it.Ext ?? DBNull.Value);
                upd.Parameters.AddWithValue("@project", (object?)it.Project ?? DBNull.Value);
                upd.Parameters.AddWithValue("@category", (object?)it.Category ?? DBNull.Value);
                upd.Parameters.AddWithValue("@tags", (object?)it.Tags ?? DBNull.Value);
                upd.Parameters.AddWithValue("@status", (object?)it.Status ?? DBNull.Value);
                upd.Parameters.AddWithValue("@confidence", it.Confidence);
                upd.Parameters.AddWithValue("@created_ts", it.CreatedTs <= 0 ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : it.CreatedTs);
                upd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        public void UpdateTags(long id, string tags)
        {
            tags = NormalizeTags(tags);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE items SET tags=@tags WHERE id=@id;";
            cmd.Parameters.AddWithValue("@tags", (object?)tags ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void UpdateProject(long id, string project)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE items SET project=@p WHERE id=@id;";
            cmd.Parameters.AddWithValue("@p", (object?)project ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void UpdateStatus(long id, string status)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE items SET status=@s WHERE id=@id;";
            cmd.Parameters.AddWithValue("@s", (object?)status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // ---------------- Query ----------------

        public IEnumerable<Item> QueryByStatus(string status)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
SELECT id, path, filename, ext, project, category, tags, status, confidence, created_ts
FROM items
WHERE COALESCE(status,'') = @s
ORDER BY created_ts DESC;";
            cmd.Parameters.AddWithValue("@s", status ?? "");
            using var r = cmd.ExecuteReader();
            while (r.Read()) yield return Map(r);
        }

        public IEnumerable<Item> QueryByTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                yield break;

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
SELECT id, path, filename, ext, project, category, tags, status, confidence, created_ts
FROM items
WHERE
    tags = @t
 OR tags LIKE @p1
 OR tags LIKE @p2
 OR tags LIKE @p3
ORDER BY created_ts DESC;";
            var t = tag.Trim();
            cmd.Parameters.AddWithValue("@t", t);
            cmd.Parameters.AddWithValue("@p1", t + ",%");
            cmd.Parameters.AddWithValue("@p2", "%," + t);
            cmd.Parameters.AddWithValue("@p3", "%," + t + ",%");

            using var r = cmd.ExecuteReader();
            while (r.Read()) yield return Map(r);
        }

        public IEnumerable<string> QueryDistinctProjects(string? keyword = null)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = @"SELECT DISTINCT project FROM items WHERE TRIM(COALESCE(project,''))<>'' ORDER BY project COLLATE NOCASE;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var p = r["project"] as string;
                    if (!string.IsNullOrWhiteSpace(p)) yield return p!;
                }
            }
            else
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = @"SELECT DISTINCT project FROM items WHERE TRIM(COALESCE(project,''))<>'' AND project LIKE @kw ORDER BY project COLLATE NOCASE;";
                cmd.Parameters.AddWithValue("@kw", $"%{keyword}%");
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var p = r["project"] as string;
                    if (!string.IsNullOrWhiteSpace(p)) yield return p!;
                }
            }
        }

        public Item? TryGetByPath(string path)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
SELECT id, path, filename, ext, project, category, tags, status, confidence, created_ts
FROM items
WHERE path=@p
LIMIT 1;";
            cmd.Parameters.AddWithValue("@p", path);
            using var r = cmd.ExecuteReader();
            if (r.Read()) return Map(r);
            return null;
        }

        public Dictionary<string, Item> TryGetByPaths(IEnumerable<string> paths)
        {
            var result = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
            var list = paths?.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
            if (list.Count == 0) return result;

            const int batchSize = 500;
            for (int i = 0; i < list.Count; i += batchSize)
            {
                var chunk = list.Skip(i).Take(batchSize).ToList();
                var sb = new StringBuilder();
                sb.Append("SELECT id, path, filename, ext, project, category, tags, status, confidence, created_ts FROM items WHERE path IN (");
                for (int k = 0; k < chunk.Count; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append($"@p{k}");
                }
                sb.Append(");");

                using var cmd = _conn.CreateCommand();
                cmd.CommandText = sb.ToString();
                for (int k = 0; k < chunk.Count; k++)
                    cmd.Parameters.AddWithValue($"@p{k}", chunk[k]);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var it = Map(r);
                    if (!string.IsNullOrWhiteSpace(it.Path))
                        result[it.Path] = it;
                }
            }

            return result;
        }

        // 新增：時間條件查詢（給 DbServiceExtensions 使用）
        public IEnumerable<Item> QuerySince(long sinceTs)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
SELECT id, path, filename, ext, project, category, tags, status, confidence, created_ts
FROM items
WHERE created_ts >= @ts
ORDER BY created_ts DESC;";
            cmd.Parameters.AddWithValue("@ts", sinceTs);
            using var r = cmd.ExecuteReader();
            while (r.Read()) yield return Map(r);
        }

        // 新增：多狀態查詢（若 extension 需要）
        public IEnumerable<Item> QueryByStatuses(IEnumerable<string> statuses)
        {
            var list = (statuses ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (list.Count == 0) yield break;

            var sb = new StringBuilder();
            sb.Append(@"SELECT id, path, filename, ext, project, category, tags, status, confidence, created_ts
                        FROM items WHERE COALESCE(status,'') IN (");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"@s{i}");
            }
            sb.Append(") ORDER BY created_ts DESC;");

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sb.ToString();
            for (int i = 0; i < list.Count; i++)
                cmd.Parameters.AddWithValue($"@s{i}", list[i]);

            using var r = cmd.ExecuteReader();
            while (r.Read()) yield return Map(r);
        }

        // ---------------- 維護 ----------------

        public int PurgeMissing()
        {
            var toDelete = new List<long>();
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, path FROM items WHERE TRIM(COALESCE(path,''))<>'';";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var id = Convert.ToInt64(r["id"]);
                    var p = r["path"] as string;
                    if (!string.IsNullOrWhiteSpace(p) && !File.Exists(p))
                        toDelete.Add(id);
                }
            }

            if (toDelete.Count == 0) return 0;

            using var tx = _conn.BeginTransaction();
            using (var del = _conn.CreateCommand())
            {
                del.CommandText = "DELETE FROM items WHERE id=@id;";
                var p = del.CreateParameter();
                p.ParameterName = "@id";
                del.Parameters.Add(p);

                foreach (var id in toDelete)
                {
                    p.Value = id;
                    del.ExecuteNonQuery();
                }
            }
            tx.Commit();
            return toDelete.Count;
        }

        public IEnumerable<Item> QueryInbox() => QueryByStatus("inbox");
        public IEnumerable<Item> QueryAutoSortStaging() => QueryByStatus("autosort-staging");

        public void Dispose()
        {
            try { _conn?.Dispose(); } catch { }
        }
    }
}
