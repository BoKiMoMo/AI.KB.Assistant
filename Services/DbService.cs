using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;
using Dapper;
using Microsoft.Data.Sqlite;
using AI.KB.Assistant.Services;

namespace AI.KB.Assistant.Services
{
    public class DbService : IDisposable
    {
        private readonly string _dbPath;
        private readonly Lazy<IDbConnection> _lazy;
        private IDbConnection Conn => _lazy.Value;

        public DbService(string dbPath)
        {
            _dbPath = dbPath;
            Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
            _lazy = new Lazy<IDbConnection>(() =>
            {
                var cn = new SqliteConnection($"Data Source={_dbPath}");
                cn.Open();
                return cn;
            });
            EnsureTables();
        }

        public void Dispose()
        {
            if (_lazy.IsValueCreated) Conn.Dispose();
        }

        private void EnsureTables()
        {
            using var tr = Conn.BeginTransaction();

            Conn.Execute(@"
CREATE TABLE IF NOT EXISTS items(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  path TEXT NOT NULL,
  filename TEXT NOT NULL,
  category TEXT,
  filetype TEXT,
  tags TEXT,
  status TEXT,
  confidence REAL,
  reason TEXT,
  created_ts INTEGER,
  year INTEGER
);", transaction: tr);

            // 向量表：item_id 唯一、vec=CSV
            Conn.Execute(@"
CREATE TABLE IF NOT EXISTS items_v(
  item_id INTEGER PRIMARY KEY,
  vec TEXT,
  dim INTEGER,
  FOREIGN KEY(item_id) REFERENCES items(id) ON DELETE CASCADE
);", transaction: tr);

            // 索引建議
            Conn.Execute("CREATE INDEX IF NOT EXISTS idx_items_path ON items(path);", transaction: tr);
            Conn.Execute("CREATE INDEX IF NOT EXISTS idx_items_cat ON items(category);", transaction: tr);
            Conn.Execute("CREATE INDEX IF NOT EXISTS idx_items_year ON items(year);", transaction: tr);
            Conn.Execute("CREATE INDEX IF NOT EXISTS idx_items_filetype ON items(filetype);", transaction: tr);

            tr.Commit();
        }

        // --------- 基本 CRUD ---------

        public async Task<int> UpsertFromPathAsync(string fullPath)
        {
            var dir = Path.GetDirectoryName(fullPath) ?? "";
            var fn = Path.GetFileName(fullPath);
            var fileType = MapFileType(fn);
            var ts = ToUnix(File.GetCreationTime(fullPath));
            var year = File.GetCreationTime(fullPath).Year;

            var id = await Conn.ExecuteScalarAsync<int?>(@"
SELECT id FROM items WHERE path=@dir AND filename=@fn;", new { dir, fn }) ?? 0;

            if (id == 0)
            {
                await Conn.ExecuteAsync(@"
INSERT INTO items(path, filename, category, filetype, tags, status, confidence, reason, created_ts, year)
VALUES(@dir,@fn,'',@fileType,'','',0,'',@ts,@year);", new { dir, fn, fileType, ts, year });

                id = await Conn.ExecuteScalarAsync<int>("SELECT last_insert_rowid();");
            }
            else
            {
                await Conn.ExecuteAsync(@"
UPDATE items SET filetype=@fileType, created_ts=@ts, year=@year
WHERE id=@id;", new { id, fileType, ts, year });
            }

            return id;
        }

        public async Task<IEnumerable<Item>> GetAllAsync(int take = 5000)
        {
            return await Conn.QueryAsync<Item>($@"
SELECT * FROM items ORDER BY created_ts DESC LIMIT {take};");
        }

        public async Task<IEnumerable<Item>> SearchAsync(string keyword, IEnumerable<string>? cats = null,
            IEnumerable<string>? filetypes = null, IEnumerable<int>? years = null, int take = 5000)
        {
            var sb = new StringBuilder("SELECT * FROM items WHERE 1=1 ");
            var p = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                sb.Append(" AND (filename LIKE @kw OR tags LIKE @kw OR reason LIKE @kw) ");
                p.Add("@kw", $"%{keyword}%");
            }

            if (cats != null && cats.Any())
            {
                sb.Append(" AND category IN @cats ");
                p.Add("@cats", cats.ToArray());
            }

            if (filetypes != null && filetypes.Any())
            {
                sb.Append(" AND filetype IN @fts ");
                p.Add("@fts", filetypes.ToArray());
            }

            if (years != null && years.Any())
            {
                sb.Append(" AND year IN @yrs ");
                p.Add("@yrs", years.ToArray());
            }

            sb.Append($" ORDER BY created_ts DESC LIMIT {take};");

            return await Conn.QueryAsync<Item>(sb.ToString(), p);
        }

        public async Task UpdateCategoryByPathAsync(string dir, string fn, string category, string reason, double confidence)
        {
            await Conn.ExecuteAsync(@"
UPDATE items SET category=@category, reason=@reason, confidence=@confidence
WHERE path=@dir AND filename=@fn;", new { dir, fn, category, reason, confidence });
        }

        public async Task UpdateTagsByPathAsync(string dir, string fn, string tags)
        {
            await Conn.ExecuteAsync(@"
UPDATE items SET tags=@tags WHERE path=@dir AND filename=@fn;", new { dir, fn, tags });
        }

        public async Task UpdateStatusByPathAsync(string dir, string fn, string status)
        {
            await Conn.ExecuteAsync(@"
UPDATE items SET status=@status WHERE path=@dir AND filename=@fn;", new { dir, fn, status });
        }

        public async Task UpdatePathAsync(int id, string newDir, string newFn)
        {
            await Conn.ExecuteAsync(@"
UPDATE items SET path=@newDir, filename=@newFn WHERE id=@id;", new { id, newDir, newFn });
        }

        public static string MapFileType(string filename)
        {
            var ext = (Path.GetExtension(filename) ?? "").Trim('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) return "其他";
            return ext switch
            {
                "png" or "jpg" or "jpeg" or "gif" or "bmp" => "圖片",
                "ppt" or "pptx" => "簡報",
                "pdf" => "報告",
                "xlsx" or "xls" or "csv" => "數據",
                "doc" or "docx" => "文件",
                "txt" or "md" => "文字",
                _ => ext
            };
        }

        private static long ToUnix(DateTime dt) =>
            new DateTimeOffset(dt).ToUnixTimeSeconds();

        // --------- 向量維護 / 語意搜尋 ---------

        private async Task<(float[] vec, int dim)?> TryGetVectorAsync(int itemId)
        {
            var row = await Conn.QueryFirstOrDefaultAsync<(string vec, int dim)?>(@"
SELECT vec, dim FROM items_v WHERE item_id=@itemId;", new { itemId });
            if (row == null) return null;
            var arr = EmbeddingService.DeserializeVector(row.Value.vec);
            return (arr, row.Value.dim);
        }

        private async Task SaveVectorAsync(int itemId, float[] vec)
        {
            await Conn.ExecuteAsync(@"
INSERT INTO items_v(item_id, vec, dim) VALUES(@itemId,@vec,@dim)
ON CONFLICT(item_id) DO UPDATE SET vec=@vec, dim=@dim;",
                new
                {
                    itemId,
                    vec = EmbeddingService.SerializeVector(vec),
                    dim = vec.Length
                });
        }

        private static string BuildEmbedText(Item it)
        {
            // 以 metadata 組合—不用讀檔即可跑（可再擴充讀正文）
            var sb = new StringBuilder();
            sb.AppendLine(it.Filename);
            if (!string.IsNullOrWhiteSpace(it.Category)) sb.AppendLine($"[{it.Category}]");
            if (!string.IsNullOrWhiteSpace(it.Tags)) sb.AppendLine($"tags:{it.Tags}");
            sb.AppendLine($"type:{it.FileType}");
            return sb.ToString();
        }

        public async Task<IEnumerable<(Item item, double score)>> SemanticSearchAsync(
            string query,
            EmbeddingService emb,
            int topK = 1000)
        {
            // 先抓最新 topK 筆做 re-ranking（避免一次全載）
            var candidates = (await Conn.QueryAsync<Item>($@"
SELECT * FROM items ORDER BY created_ts DESC LIMIT {Math.Max(100, topK)};")).ToList();

            var qVec = await emb.EmbedAsync(query);

            var scored = new List<(Item, double)>(candidates.Count);
            foreach (var it in candidates)
            {
                var vec = (await TryGetVectorAsync(it.Id))?.vec;
                if (vec == null)
                {
                    // 補向量
                    var text = BuildEmbedText(it);
                    vec = await emb.EmbedAsync(text);
                    await SaveVectorAsync(it.Id, vec);
                }

                var s = EmbeddingService.Cosine(qVec, vec);
                scored.Add((it, s));
            }

            return scored.OrderByDescending(x => x.Item2).Take(topK);
        }
    }
}
