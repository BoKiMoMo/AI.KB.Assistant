using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;              // ★ 新增：為了 CancellationToken 多載
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// V7.2 DbService（完整版）
    /// - 預設使用 SQLite（若環境無 Microsoft.Data.Sqlite，回退 JSONL 檔案模式）
    /// - 自動建立資料夾與資料庫
    /// - 啟動時執行最小 Migration：依 Item 屬性自動新增缺失欄位（ALTER TABLE）
    /// - 提供 CRUD 與常用查詢
    /// - 所有時間以 UTC ISO-8601 存為 TEXT
    /// </summary>
    public sealed class DbService : IDisposable
    {
        private readonly IDbProvider _provider;

        public DbService()
        {
            // 兼容 Db.DbPath / Db.Path 命名
            string? pick(string? a, string? b, string fallback)
                => !string.IsNullOrWhiteSpace(a) ? a : (!string.IsNullOrWhiteSpace(b) ? b : fallback);

            var cfg = AppConfig.Current;
            var baseDefault = Path.Combine(AppContext.BaseDirectory, "ai_kb.db");
            var chosen = pick(cfg?.Db?.DbPath, cfg?.Db?.Path, baseDefault);

            _provider = TryCreateSqlite(chosen) ?? new FileDbProvider(chosen + ".jsonl");
        }

        // ====== 原有介面（無 CT） ======
        public Task InitializeAsync() => _provider.InitializeAsync();
        public Task<List<Item>> QueryAllAsync() => _provider.QueryAllAsync();
        public Task<int> InsertItemsAsync(IEnumerable<Item> items) => _provider.InsertItemsAsync(items);
        public Task<int> UpdateItemsAsync(IEnumerable<Item> items) => _provider.UpdateItemsAsync(items);
        public Task<int> UpsertAsync(IEnumerable<Item> items) => _provider.UpsertAsync(items);
        public Task<Item?> GetByIdAsync(string id) => _provider.GetByIdAsync(id);
        public Task<int> DeleteByIdAsync(string id) => _provider.DeleteByIdAsync(id);
        public Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage) => _provider.StageOnlyAsync(items, stage);
        public Task<int> StageOnlyAsync(IEnumerable<Item> items) => StageOnlyAsync(items, 0);
        public Task<List<Item>> ListRecentAsync(int take = 200) => _provider.ListRecentAsync(take);
        public Task<List<Item>> SearchAsync(string keyword, int take = 200) => _provider.SearchAsync(keyword, take);

        // ====== 新增：與 IntakeService 對齊的 CT 多載（內部目前不使用 CT，但保留簽名避免編譯錯誤） ======
        public Task InitializeAsync(CancellationToken _)
            => InitializeAsync();

        public Task<List<Item>> QueryAllAsync(CancellationToken _)
            => QueryAllAsync();

        public Task<int> InsertItemsAsync(IEnumerable<Item> items, CancellationToken _)
            => InsertItemsAsync(items);

        public Task<int> UpdateItemsAsync(IEnumerable<Item> items, CancellationToken _)
            => UpdateItemsAsync(items);

        public Task<int> UpsertAsync(IEnumerable<Item> items, CancellationToken _)
            => UpsertAsync(items);

        public Task<Item?> GetByIdAsync(string id, CancellationToken _)
            => GetByIdAsync(id);

        public Task<int> DeleteByIdAsync(string id, CancellationToken _)
            => DeleteByIdAsync(id);

        public Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage, CancellationToken _)
            => StageOnlyAsync(items, stage);

        public Task<List<Item>> ListRecentAsync(int take, CancellationToken _)
            => ListRecentAsync(take);

        public Task<List<Item>> SearchAsync(string keyword, int take, CancellationToken _)
            => SearchAsync(keyword, take);

        public void Dispose() => (_provider as IDisposable)?.Dispose();

        // ========== Provider Factory ==========
        private static IDbProvider? TryCreateSqlite(string dbPath)
        {
            try
            {
                var asm = AppDomain.CurrentDomain.Load("Microsoft.Data.Sqlite");
                var connType = asm.GetType("Microsoft.Data.Sqlite.SqliteConnection", throwOnError: true)!;
                return new SqliteProvider(connType, dbPath);
            }
            catch
            {
                return null; // 無套件或載入失敗 → 回退 JSONL
            }
        }

        // ========== Abstraction ==========
        private interface IDbProvider : IDisposable
        {
            // ※ 內部 provider 目前不吃 CT；若未來要支援，擴充這層即可
            Task InitializeAsync();
            Task<List<Item>> QueryAllAsync();
            Task<int> InsertItemsAsync(IEnumerable<Item> items);
            Task<int> UpdateItemsAsync(IEnumerable<Item> items);
            Task<int> UpsertAsync(IEnumerable<Item> items);
            Task<Item?> GetByIdAsync(string id);
            Task<int> DeleteByIdAsync(string id);
            Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage);
            Task<List<Item>> ListRecentAsync(int take);
            Task<List<Item>> SearchAsync(string keyword, int take);
        }

        #region FileDbProvider（JSONL，零依賴）
        private sealed class FileDbProvider : IDbProvider
        {
            private readonly string _path;
            private readonly JsonSerializerOptions _opts = new()
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            public FileDbProvider(string path) { _path = path; }

            public Task InitializeAsync()
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                if (!File.Exists(_path)) File.WriteAllText(_path, string.Empty);
                return Task.CompletedTask;
            }

            private IEnumerable<Item> ReadAll()
            {
                if (!File.Exists(_path)) yield break;
                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                while (!sr.EndOfStream)
                {
                    var line = sr.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    Item? item = null;
                    try { item = JsonSerializer.Deserialize<Item>(line); } catch { }
                    if (item != null) yield return item;
                }
            }

            private void WriteAll(IEnumerable<Item> items)
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                using var fs = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.None);
                using var sw = new StreamWriter(fs);
                foreach (var it in items)
                    sw.WriteLine(JsonSerializer.Serialize(it, _opts));
            }

            public Task<List<Item>> QueryAllAsync() => Task.FromResult(new List<Item>(ReadAll()));

            public Task<int> InsertItemsAsync(IEnumerable<Item> items)
            {
                var map = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
                foreach (var it in ReadAll()) if (!string.IsNullOrEmpty(it.Id)) map[it.Id] = it;
                var now = DateTime.UtcNow;
                var n = 0;
                foreach (var it in items)
                {
                    it.CreatedAt = it.CreatedAt == default ? now : it.CreatedAt;
                    it.UpdatedAt = now;
                    map[it.Id ??= Guid.NewGuid().ToString("N")] = it;
                    n++;
                }
                WriteAll(map.Values);
                return Task.FromResult(n);
            }

            public Task<int> UpdateItemsAsync(IEnumerable<Item> items)
            {
                var map = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
                foreach (var it in ReadAll()) if (!string.IsNullOrEmpty(it.Id)) map[it.Id] = it;
                var now = DateTime.UtcNow;
                var n = 0;
                foreach (var it in items)
                {
                    if (string.IsNullOrWhiteSpace(it.Id)) continue;
                    it.UpdatedAt = now;
                    map[it.Id] = it;
                    n++;
                }
                WriteAll(map.Values);
                return Task.FromResult(n);
            }

            public Task<int> UpsertAsync(IEnumerable<Item> items) => InsertItemsAsync(items);

            public Task<Item?> GetByIdAsync(string id)
            {
                foreach (var it in ReadAll()) if (string.Equals(it.Id, id, StringComparison.OrdinalIgnoreCase)) return Task.FromResult<Item?>(it);
                return Task.FromResult<Item?>(null);
            }

            public Task<int> DeleteByIdAsync(string id)
            {
                var list = new List<Item>(ReadAll());
                var n = list.RemoveAll(it => string.Equals(it.Id, id, StringComparison.OrdinalIgnoreCase));
                WriteAll(list);
                return Task.FromResult(n);
            }

            public Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage)
            {
                foreach (var it in items) it.Status = $"stage:{stage}";
                return UpdateItemsAsync(items);
            }

            public Task<List<Item>> ListRecentAsync(int take)
                => Task.FromResult(new List<Item>(ReadAll().OrderByDescending(i => i.UpdatedAt).Take(take)));

            public Task<List<Item>> SearchAsync(string keyword, int take)
            {
                keyword ??= string.Empty;
                var q = keyword.Trim();
                var list = ReadAll()
                    .Where(i => (i.Path?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                             || (i.Project?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                             || (i.Note?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                    .Take(take)
                    .ToList();
                return Task.FromResult(list);
            }

            public void Dispose() { }
        }
        #endregion

        #region SqliteProvider（反射呼叫 Microsoft.Data.Sqlite）
        private sealed class SqliteProvider : IDbProvider
        {
            private readonly Type _connType;
            private readonly string _dbPath;   // 純路徑
            private readonly string _connStr;  // Data Source=...

            public SqliteProvider(Type connType, string dbPath)
            {
                _connType = connType;
                _dbPath = string.IsNullOrWhiteSpace(dbPath)
                    ? Path.Combine(AppContext.BaseDirectory, "ai_kb.db")
                    : (Path.IsPathRooted(dbPath) ? dbPath : Path.Combine(AppContext.BaseDirectory, dbPath));

                var dir = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

                _connStr = $"Data Source={_dbPath};Cache=Shared;Mode=ReadWriteCreate;";
            }

            public async Task InitializeAsync()
            {
                // 建表（若無表），接著做最小 migration：補缺欄位
                var createSql = @"
CREATE TABLE IF NOT EXISTS Items (
    Id TEXT PRIMARY KEY,
    Path TEXT NOT NULL,
    ProposedPath TEXT,
    CreatedAt TEXT,
    UpdatedAt TEXT,
    Tags TEXT,
    Status TEXT,
    Project TEXT,
    Note TEXT
);
CREATE INDEX IF NOT EXISTS IX_Items_Path ON Items(Path);";

                await ExecuteNonQueryAsync(createSql).ConfigureAwait(false);
                await EnsureColumnsAsync().ConfigureAwait(false);
            }

            private async Task EnsureColumnsAsync()
            {
                // 先列出現有欄位（一次性）
                var existing = await GetColumnsAsync("Items").ConfigureAwait(false);
                var has = new HashSet<string>(existing.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

                // 建表 SQL 已含的欄位直接跳過（防止重複）
                var baseCols = new HashSet<string>(new[]
                {
        "Id","Path","ProposedPath","CreatedAt","UpdatedAt","Tags","Status","Project","Note"
    }, StringComparer.OrdinalIgnoreCase);

                // 依據 Item 的公開屬性動態補欄位（TEXT）
                foreach (var p in typeof(Item).GetProperties())
                {
                    var name = p.Name;
                    if (baseCols.Contains(name)) continue;   // 已在建表內
                    if (has.Contains(name)) continue;        // PRAGMA 已查到

                    // 再次以 PRAGMA 單筆確認（雙重保險）
                    if (await ColumnExistsAsync("Items", name).ConfigureAwait(false))
                        continue;

                    // 安全嘗試補欄位；若仍遇到 duplicate（極少數情況）就忽略
                    var sql = $"ALTER TABLE Items ADD COLUMN {name} TEXT;";
                    try
                    {
                        await ExecuteNonQueryAsync(sql).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message?.ToLowerInvariant() ?? "";
                        if (msg.Contains("duplicate column name")) { /* 忽略 */ }
                        else throw;
                    }
                }
            }


            private sealed record ColumnInfo(string Name);

            private async Task<List<ColumnInfo>> GetColumnsAsync(string table)
            {
                var rows = await ExecuteQueryAsync($"PRAGMA table_info('{table}');").ConfigureAwait(false);
                var list = new List<ColumnInfo>();
                foreach (var r in rows)
                {
                    var name = r.GetString("name");
                    if (!string.IsNullOrEmpty(name)) list.Add(new ColumnInfo(name));
                }
                return list;
            }

            private async Task<bool> ColumnExistsAsync(string table, string column)
            {
                var rows = await ExecuteQueryAsync(
                    "PRAGMA table_info('" + table + "');").ConfigureAwait(false);

                foreach (var r in rows)
                {
                    var name = r.GetString("name");
                    if (!string.IsNullOrEmpty(name) &&
                        string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }

            // ---- CRUD ----

            public async Task<List<Item>> QueryAllAsync()
            {
                var rows = await ExecuteQueryAsync("SELECT * FROM Items ORDER BY UpdatedAt DESC;").ConfigureAwait(false);
                return rows.Select(RowToItem).ToList();
            }

            public async Task<int> InsertItemsAsync(IEnumerable<Item> items)
            {
                var n = 0;
                foreach (var it in items)
                {
                    it.Id ??= Guid.NewGuid().ToString("N");
                    it.CreatedAt = it.CreatedAt == default ? DateTime.UtcNow : it.CreatedAt;
                    it.UpdatedAt = DateTime.UtcNow;
                    n += await UpsertOneAsync(it).ConfigureAwait(false);
                }
                return n;
            }

            public Task<int> UpdateItemsAsync(IEnumerable<Item> items) => InsertItemsAsync(items);
            public Task<int> UpsertAsync(IEnumerable<Item> items) => InsertItemsAsync(items);

            public async Task<Item?> GetByIdAsync(string id)
            {
                var rows = await ExecuteQueryAsync("SELECT * FROM Items WHERE Id=$Id LIMIT 1;",
                    new Dictionary<string, object?> { ["$Id"] = id }).ConfigureAwait(false);
                return rows.Count > 0 ? RowToItem(rows[0]) : null;
            }

            public async Task<int> DeleteByIdAsync(string id)
            {
                return await ExecuteNonQueryAsync("DELETE FROM Items WHERE Id=$Id;",
                    new Dictionary<string, object?> { ["$Id"] = id }).ConfigureAwait(false);
            }

            public async Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage)
            {
                var n = 0;
                foreach (var it in items)
                {
                    if (string.IsNullOrWhiteSpace(it.Id)) continue;
                    it.Status = $"stage:{stage}";
                    it.UpdatedAt = DateTime.UtcNow;
                    n += await ExecuteNonQueryAsync(@"
UPDATE Items SET Status=$Status, UpdatedAt=$UpdatedAt WHERE Id=$Id;",
                        new Dictionary<string, object?>
                        {
                            ["$Id"] = it.Id,
                            ["$Status"] = it.Status,
                            ["$UpdatedAt"] = ToIso(it.UpdatedAt)
                        }).ConfigureAwait(false);
                }
                return n;
            }

            public async Task<List<Item>> ListRecentAsync(int take)
            {
                var rows = await ExecuteQueryAsync("SELECT * FROM Items ORDER BY UpdatedAt DESC LIMIT $Take;",
                    new Dictionary<string, object?> { ["$Take"] = take }).ConfigureAwait(false);
                return rows.Select(RowToItem).ToList();
            }

            public async Task<List<Item>> SearchAsync(string keyword, int take)
            {
                keyword ??= string.Empty;
                var rows = await ExecuteQueryAsync(@"
SELECT * FROM Items
WHERE (Path LIKE $Q OR Project LIKE $Q OR Note LIKE $Q OR ProposedPath LIKE $Q)
ORDER BY UpdatedAt DESC
LIMIT $Take;",
                    new Dictionary<string, object?>
                    {
                        ["$Q"] = $"%{keyword}%",
                        ["$Take"] = take
                    }).ConfigureAwait(false);
                return rows.Select(RowToItem).ToList();
            }

            // ---- helpers ----
            private static string ToIso(DateTime dt) => dt.ToString("o", CultureInfo.InvariantCulture);

            private Item RowToItem(Row r)
            {
                List<string> tags;
                try
                {
                    var s = r.GetString("Tags");
                    tags = string.IsNullOrWhiteSpace(s) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(s!) ?? new List<string>();
                }
                catch { tags = new List<string>(); }

                return new Item
                {
                    Id = r.GetString("Id") ?? Guid.NewGuid().ToString("N"),
                    Path = r.GetString("Path") ?? string.Empty,
                    ProposedPath = r.GetString("ProposedPath") ?? string.Empty,
                    CreatedAt = r.GetDateTime("CreatedAt") ?? DateTime.UtcNow,
                    UpdatedAt = r.GetDateTime("UpdatedAt") ?? DateTime.UtcNow,
                    Tags = tags,
                    Status = r.GetString("Status"),
                    Project = r.GetString("Project"),
                    Note = r.GetString("Note")
                    // TODO: 如需映射更多欄位（Category/Timestamp...），於此比照取值。
                };
            }

            private async Task<int> UpsertOneAsync(Item it)
            {
                var sql = @"
INSERT INTO Items (Id, Path, ProposedPath, CreatedAt, UpdatedAt, Tags, Status, Project, Note)
VALUES ($Id, $Path, $ProposedPath, $CreatedAt, $UpdatedAt, $Tags, $Status, $Project, $Note)
ON CONFLICT(Id) DO UPDATE SET
    Path=excluded.Path,
    ProposedPath=excluded.ProposedPath,
    CreatedAt=excluded.CreatedAt,
    UpdatedAt=excluded.UpdatedAt,
    Tags=excluded.Tags,
    Status=excluded.Status,
    Project=excluded.Project,
    Note=excluded.Note;";
                var p = new Dictionary<string, object?>
                {
                    ["$Id"] = it.Id,
                    ["$Path"] = it.Path,
                    ["$ProposedPath"] = it.ProposedPath,
                    ["$CreatedAt"] = ToIso(it.CreatedAt == default ? DateTime.UtcNow : it.CreatedAt),
                    ["$UpdatedAt"] = ToIso(it.UpdatedAt == default ? DateTime.UtcNow : it.UpdatedAt),
                    ["$Tags"] = JsonSerializer.Serialize(it.Tags ?? new List<string>()),
                    ["$Status"] = it.Status,
                    ["$Project"] = it.Project,
                    ["$Note"] = it.Note
                };
                return await ExecuteNonQueryAsync(sql, p).ConfigureAwait(false);
            }

            // ---------- Reflection-based low-level calls ----------
            private object CreateConnection()
                => Activator.CreateInstance(_connType, new object[] { _connStr })!;

            private async Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object?>? prms = null)
            {
                var conn = CreateConnection();
                try
                {
                    await OpenAsync(conn).ConfigureAwait(false);
                    var cmd = CreateCommand(conn, sql, prms);
                    try { return await ExecuteNonQueryCoreAsync(cmd).ConfigureAwait(false); }
                    finally { (cmd as IDisposable)?.Dispose(); }
                }
                finally { (conn as IDisposable)?.Dispose(); }
            }

            private async Task<List<Row>> ExecuteQueryAsync(string sql, Dictionary<string, object?>? prms = null)
            {
                var conn = CreateConnection();
                try
                {
                    await OpenAsync(conn).ConfigureAwait(false);
                    var cmd = CreateCommand(conn, sql, prms);
                    try { return await ExecuteReaderRowsAsync(cmd).ConfigureAwait(false); }
                    finally { (cmd as IDisposable)?.Dispose(); }
                }
                finally { (conn as IDisposable)?.Dispose(); }
            }

            private static async Task OpenAsync(object conn)
            {
                var m = conn.GetType().GetMethod("OpenAsync", Type.EmptyTypes);
                if (m != null) { await (Task)m.Invoke(conn, null)!; return; }
                m = conn.GetType().GetMethod("OpenAsync", new[] { typeof(System.Threading.CancellationToken) });
                if (m != null) { await (Task)m.Invoke(conn, new object[] { System.Threading.CancellationToken.None })!; return; }
                var mOpen = conn.GetType().GetMethod("Open", Type.EmptyTypes)!;
                mOpen.Invoke(conn, null);
            }

            // 於 SqliteProvider 類別內
            private static object CreateCommand(object conn, string sql, Dictionary<string, object?>? prms)
            {
                var asm = conn.GetType().Assembly;
                var cmdType = asm.GetType("Microsoft.Data.Sqlite.SqliteCommand")!;
                var cmd = cmdType
                    .GetConstructor(new[] { typeof(string), conn.GetType() })!
                    .Invoke(new object[] { sql, conn });

                if (prms != null && prms.Count > 0)
                {
                    // ⚠️ 關鍵修正：僅取 SqliteCommand 自己宣告的 Parameters（避免和基底 DbCommand 產生歧義）
                    var parmsProp = cmdType.GetProperty(
                        "Parameters",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.DeclaredOnly
                    )!;
                    var parms = parmsProp.GetValue(cmd)!;

                    // SqliteParameterCollection.AddWithValue(string, object)
                    var addWithValue = parms.GetType().GetMethod("AddWithValue", new[] { typeof(string), typeof(object) })!;
                    foreach (var kv in prms)
                    {
                        addWithValue.Invoke(parms, new object[] { kv.Key, kv.Value ?? DBNull.Value });
                    }
                }

                return cmd;
            }

            private static async Task<int> ExecuteNonQueryCoreAsync(object cmd)
            {
                var m = cmd.GetType().GetMethod("ExecuteNonQueryAsync", Type.EmptyTypes);
                if (m != null) return await (Task<int>)m.Invoke(cmd, null)!;
                var m2 = cmd.GetType().GetMethod("ExecuteNonQuery", Type.EmptyTypes)!;
                return (int)m2.Invoke(cmd, null)!;
            }

            private static async Task<List<Row>> ExecuteReaderRowsAsync(object cmd)
            {
                var list = new List<Row>();
                var execReader = cmd.GetType().GetMethod("ExecuteReaderAsync", Type.EmptyTypes);
                dynamic reader;
                if (execReader != null) reader = await (dynamic)execReader.Invoke(cmd, null)!;
                else
                {
                    var exec = cmd.GetType().GetMethod("ExecuteReader", Type.EmptyTypes)!;
                    reader = exec.Invoke(cmd, null)!;
                }
                try
                {
                    while (await reader.ReadAsync())
                        list.Add(new Row(reader));
                }
                finally
                {
                    try { (reader as IDisposable)?.Dispose(); } catch { }
                }
                return list;
            }

            private sealed class Row
            {
                private readonly dynamic _r;
                public Row(dynamic reader) { _r = reader; }
                public string? GetString(string name) => TryGet(name, v => v?.ToString());
                public DateTime? GetDateTime(string name)
                    => TryGet(name, v => v is null ? (DateTime?)null : DateTime.Parse(v.ToString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
                private T? TryGet<T>(string name, Func<object?, T?> cast)
                {
                    try
                    {
                        var i = (int)_r.GetOrdinal(name);
                        if (_r.IsDBNull(i)) return default;
                        var v = _r.GetValue(i);
                        return cast(v);
                    }
                    catch { return default; }
                }
            }

            public void Dispose() { }
        }
        #endregion
    }
}
