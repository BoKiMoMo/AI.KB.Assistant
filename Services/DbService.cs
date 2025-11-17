using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Views;
using Microsoft.Data.Sqlite; // (V15.0) 硬編碼 using

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// V20.0 (最終修復版)
    /// 1. (V15.1) 移除了「反射」載入，改為「硬編碼」'using Microsoft.Data.Sqlite'。
    /// 2. (V15.1) 修正 Row 類別 [cite:"Services/DbService.cs (V20.0 最終版) (line 651)"]，使其「具現化」(materialize) 資料，解決 "Connection Closed" 錯誤。
    /// 3. [V19.1] 修正 `Row.GetDateTime` [cite:"Services/DbService.cs (V20.0 最終版) (line 661)"] (CS8602 [cite:"image_42b958.png"] 警告)，將 `v is DBNull` [cite:"Services/DbService.cs (V20.0 最終版) (line 661)"] 改為 `v is null` [cite:"Services/DbService.cs (V20.0 最終版) (line 661) (modified)"]。
    /// </summary>
    public sealed partial class DbService : IDisposable
    {
        private readonly IDbProvider _provider;
        private readonly object _dbLock = new object();

        public DbService()
        {
            Console.WriteLine("[DB INIT V15.1] DbService constructor started.");

            // (V14.1 註解) App.xaml.cs (V14.1) [cite:"App.xaml.cs (V20.0 最終版)"] 已呼叫 v3.x 'SQLitePCL.Batteries.Init()' [cite:"App.xaml.cs (V20.0 最終版) (line 39)"]。

            string? pick(string? a, string? b, string fallback)
                => !string.IsNullOrWhiteSpace(a) ? a : (!string.IsNullOrWhiteSpace(b) ? b : fallback);

            var cfg = ConfigService.Cfg;
            var baseDefault = Path.Combine(AppContext.BaseDirectory, "ai_kb.db");
            var chosen = pick(cfg?.Db?.DbPath, cfg?.Db?.Path, baseDefault);

            Console.WriteLine($"[DB INIT V15.1] DB Path selected: {chosen}");

            _provider = TryCreateSqlite(chosen, _dbLock) ?? new FileDbProvider(chosen + ".jsonl");
            Console.WriteLine($"[DB INIT V15.1] Provider created. IsSqlite={_provider is SqliteProvider}");
        }

        // public API (簡潔委派)
        public Task InitializeAsync() => _provider.InitializeAsync();
        public Task<List<Item>> QueryAllAsync() => _provider.QueryAllAsync();
        public Task<int> InsertItemsAsync(IEnumerable<Item> items) => _provider.InsertItemsAsync(items);
        public Task<int> InsertAsync(Item item, CancellationToken _ = default) => InsertItemsAsync(new[] { item });
        public Task<int> UpdateItemsAsync(IEnumerable<Item> items) => _provider.UpdateItemsAsync(items);
        public Task<int> UpsertAsync(IEnumerable<Item> items) => _provider.UpsertAsync(items);
        public Task<Item?> GetByIdAsync(string id) => _provider.GetByIdAsync(id);
        public Task<int> DeleteByIdAsync(string id) => _provider.DeleteByIdAsync(id);
        public Task<int> DeleteItemsAsync(IEnumerable<string> ids) => _provider.DeleteItemsAsync(ids);
        public Task<int> DeleteNonCommittedAsync() => _provider.DeleteNonCommittedAsync();
        public Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage) => _provider.StageOnlyAsync(items, stage);
        public Task<int> StageOnlyAsync(IEnumerable<Item> items) => StageOnlyAsync(items, 0);
        public Task<List<Item>> ListRecentAsync(int take = 200) => _provider.ListRecentAsync(take);
        public Task<List<Item>> SearchAsync(string keyword, int take = 200) => _provider.SearchAsync(keyword, take);

        public void Dispose() => (_provider as IDisposable)?.Dispose();

        // ========== Provider Factory ==========
        private static IDbProvider? TryCreateSqlite(string? dbPath, object dbLock)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                return null;

            Console.WriteLine("[DB INIT V15.1] TryCreateSqlite started.");
            try
            {
                var dir = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // (V15.0) 硬編碼 using

                Func<IDbConnection> createConnection = () =>
                {
                    var connStr = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;";
                    // (V15.0) 硬編碼 'new SqliteConnection()'
                    var conn = (IDbConnection)new SqliteConnection(connStr);
                    return conn;
                };

                // smoke test (open/close)
                lock (dbLock)
                {
                    using var test = createConnection();
                    test.Open();
                    test.Close();
                }

                Console.WriteLine("[DB INIT V15.1] SQLite smoke test passed. Activating SqliteProvider.");
                return new SqliteProvider(createConnection, dbPath, dbLock);
            }
            catch (Exception ex)
            {
                // (V15.0) App.xaml.cs (V14.1) [cite:"App.xaml.cs (V20.0 最終版)"] 必須呼叫 'Batteries.Init()' [cite:"App.xaml.cs (V20.0 最終版) (line 39)"]，否則 'test.Open()' [cite:"Services/DbService.cs (V20.0 最終版) (line 125)"] 會在此處失敗
                Console.WriteLine($"[DB INIT V15.1] SQLite activation failed: {ex.Message}");
                App.LogCrash("DbService.TryCreateSqlite", ex);
                return null;
            }
        }

        // ========== Abstraction ==========
        private interface IDbProvider : IDisposable
        {
            Task InitializeAsync();
            Task<List<Item>> QueryAllAsync();
            Task<int> InsertItemsAsync(IEnumerable<Item> items);
            Task<int> UpdateItemsAsync(IEnumerable<Item> items);
            Task<int> UpsertAsync(IEnumerable<Item> items);
            Task<Item?> GetByIdAsync(string id);
            Task<int> DeleteByIdAsync(string id);
            Task<int> DeleteItemsAsync(IEnumerable<string> ids);
            Task<int> DeleteNonCommittedAsync();
            Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage);
            Task<List<Item>> ListRecentAsync(int take);
            Task<List<Item>> SearchAsync(string keyword, int take);
        }

        #region FileDbProvider (JSONL fallback)
        private sealed class FileDbProvider : IDbProvider
        {
            private readonly string _path;
            private readonly JsonSerializerOptions _opts = new()
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            public FileDbProvider(string path)
            {
                _path = path;
                Console.WriteLine($"[DB] FileDbProvider activated at: {_path}");
            }

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

            public Task<int> DeleteItemsAsync(IEnumerable<string> ids)
            {
                var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
                var list = new List<Item>(ReadAll());
                var n = list.RemoveAll(it => !string.IsNullOrWhiteSpace(it.Id) && idSet.Contains(it.Id));
                WriteAll(list);
                return Task.FromResult(n);
            }

            public Task<int> DeleteNonCommittedAsync()
            {
                var list = new List<Item>(ReadAll());
                var n = list.RemoveAll(it => (it.Status ?? "intaked") != "committed");
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

        #region SqliteProvider (reflection)
        private sealed class SqliteProvider : IDbProvider
        {
            private readonly Func<IDbConnection> _createConnection;
            private readonly string _dbPath;
            private readonly object _lock;

            public SqliteProvider(Func<IDbConnection> createConnection, string dbPath, object lockObject)
            {
                _createConnection = createConnection;
                _lock = lockObject;
                _dbPath = dbPath;
            }

            public async Task InitializeAsync()
            {
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
                var existing = await GetColumnsAsync("Items").ConfigureAwait(false);
                var has = new HashSet<string>(existing.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
                var itemProps = typeof(Item).GetProperties();
                var coreCols = new HashSet<string>(new[]
                {
                    "Id", "Path", "ProposedPath", "CreatedAt", "UpdatedAt", "Tags", "Status", "Project", "Note",
                    "Category", "Timestamp"
                }, StringComparer.OrdinalIgnoreCase);

                foreach (var p in itemProps)
                {
                    var name = p.Name;
                    if (!coreCols.Contains(name)) continue;
                    if (has.Contains(name)) continue;

                    // double-check via PRAGMA to avoid race conditions
                    if (await ColumnExistsAsync("Items", name).ConfigureAwait(false)) continue;

                    var sql = $"ALTER TABLE Items ADD COLUMN {name} TEXT;";
                    try
                    {
                        await ExecuteNonQueryAsync(sql).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // [V15.1 修正] 
                        // ExecuteNonQueryAsync (Line 606) 現在會正確地 'throw' 'SqliteException' (如果它不是 'duplicate column')。
                        // 我們可以在這裡捕捉 'SqliteException'。
                        var msg = ex.Message?.ToLowerInvariant() ?? "";
                        if (msg.Contains("duplicate column name"))
                        {
                            Console.WriteLine($"[DB MIGRATION] Ignored duplicate column: {name}");
                        }
                        else
                        {
                            Console.WriteLine($"[DB MIGRATION] Failed adding column {name}: {ex.Message}");
                            throw;
                        }
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
                var rows = await ExecuteQueryAsync($"PRAGMA table_info('{table}');").ConfigureAwait(false);
                foreach (var r in rows)
                {
                    var name = r.GetString("name");
                    if (!string.IsNullOrEmpty(name) &&
                        string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }

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

            public async Task<int> DeleteItemsAsync(IEnumerable<string> ids)
            {
                if (ids == null || !ids.Any()) return 0;

                var totalDeleted = 0;
                var idList = ids.ToList();
                var batchSize = 900;

                for (int i = 0; i < idList.Count; i += batchSize)
                {
                    var batch = idList.Skip(i).Take(batchSize).ToList();
                    if (batch.Count == 0) continue;

                    var prms = new Dictionary<string, object?>();
                    var placeholders = new List<string>();
                    for (int j = 0; j < batch.Count; j++)
                    {
                        var key = $"$p{j}";
                        prms[key] = batch[j];
                        placeholders.Add(key);
                    }

                    var sql = $"DELETE FROM Items WHERE Id IN ({string.Join(",", placeholders)});";
                    totalDeleted += await ExecuteNonQueryAsync(sql, prms).ConfigureAwait(false);
                }
                return totalDeleted;
            }

            public async Task<int> DeleteNonCommittedAsync()
            {
                var sql = "DELETE FROM Items WHERE Status IS NULL OR Status != 'committed';";
                return await ExecuteNonQueryAsync(sql).ConfigureAwait(false);
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
                    Note = r.GetString("Note"),
                    Category = r.GetString("Category"),
                    Timestamp = r.GetDateTime("Timestamp")
                };
            }

            private async Task<int> UpsertOneAsync(Item it)
            {
                var sql = @"
INSERT INTO Items (Id, Path, ProposedPath, CreatedAt, UpdatedAt, Tags, Status, Project, Note, Category, Timestamp)
VALUES ($Id, $Path, $ProposedPath, $CreatedAt, $UpdatedAt, $Tags, $Status, $Project, $Note, $Category, $Timestamp)
ON CONFLICT(Id) DO UPDATE SET
    Path=excluded.Path,
    ProposedPath=excluded.ProposedPath,
    CreatedAt=excluded.CreatedAt,
    UpdatedAt=excluded.UpdatedAt,
    Tags=excluded.Tags,
    Status=excluded.Status,
    Project=excluded.Project,
    Note=excluded.Note,
    Category=excluded.Category,
    Timestamp=excluded.Timestamp;";

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
                    ["$Note"] = it.Note,
                    ["$Category"] = it.Category,
                    ["$Timestamp"] = it.Timestamp.HasValue ? ToIso(it.Timestamp.Value) : null
                };
                return await ExecuteNonQueryAsync(sql, p).ConfigureAwait(false);
            }

            // (V15.0) 硬編碼
            private IDbConnection CreateConnection()
            {
                return _createConnection();
            }

            private async Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object?>? prms = null)
            {
                return await Task.Run(() =>
                {
                    lock (_lock)
                    {
                        using var conn = (SqliteConnection)CreateConnection();
                        try
                        {
                            conn.Open();
                            using var cmd = conn.CreateCommand();
                            cmd.CommandText = sql;
                            if (prms != null)
                            {
                                foreach (var p in prms)
                                {
                                    cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
                                }
                            }

                            return cmd.ExecuteNonQuery();
                        }
                        // [V15.1 修正 Fix 1] 
                        // 改為捕捉 'SqliteException' (V15.0 'image_24204c.jpg' [cite:"image_e16a88.jpg"] 顯示的錯誤)
                        catch (SqliteException ex)
                        {
                            var msg = ex.Message?.ToLowerInvariant() ?? "";
                            if (msg.Contains("duplicate column name"))
                            {
                                Console.WriteLine($"[DB MIGRATION] Ignored duplicate column: {ex.Message}");
                                return 0; // 忽略錯誤
                            }
                            else
                            {
                                throw; // 拋出其他 SQLite 錯誤
                            }
                        }
                        finally { conn.Close(); }
                    }
                }).ConfigureAwait(false);
            }

            private async Task<List<Row>> ExecuteQueryAsync(string sql, Dictionary<string, object?>? prms = null)
            {
                return await Task.Run(() =>
                {
                    lock (_lock)
                    {
                        using var conn = (SqliteConnection)CreateConnection();
                        try
                        {
                            conn.Open();
                            using var cmd = conn.CreateCommand();
                            cmd.CommandText = sql;
                            if (prms != null)
                            {
                                foreach (var p in prms)
                                {
                                    cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
                                }
                            }

                            var list = new List<Row>();
                            using var reader = cmd.ExecuteReader();
                            while (reader.Read())
                            {
                                // [V15.1 修正 Fix 2] 
                                // V15.0 的 'new Row(reader)' 存在 "Connection Closed" 錯誤
                                // V15.1 改為呼叫 V15.1 新的「具現化」建構函式
                                list.Add(new Row(reader));
                            }
                            return list;
                        }
                        finally { conn.Close(); }
                    }
                }).ConfigureAwait(false);
            }

            // [V15.1 修正 Fix 2]
            // V15.0 的 Row 類別存在 "Connection Closed" 錯誤。
            // V15.1 Row 類別改為在建構函式中「具現化」(Materialize) 資料，
            // 將資料從 reader 複製到 Dictionary，而不是儲存 reader 本身。
            private sealed class Row
            {
                private readonly Dictionary<string, object> _data;

                /// <summary>
                /// (V15.1) 建構函式：在 Reader 仍然開啟時，將所有資料讀取到字典中。
                /// </summary>
                public Row(SqliteDataReader reader)
                {
                    _data = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        // 儲存欄位名和值 (DBNull.Value)
                        _data[reader.GetName(i)] = reader.GetValue(i);
                    }
                }

                public string? GetString(string name) => TryGet(name, v => v?.ToString());

                public DateTime? GetDateTime(string name)
                    // [V19.1 CS8602 修正] v is DBNull ? -> v is null ?
                    => TryGet(name, v => v is null ? (DateTime?)null : DateTime.Parse(v.ToString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

                private T? TryGet<T>(string name, Func<object?, T?> cast)
                {
                    try
                    {
                        // (V15.1) 改為從字典讀取
                        if (!_data.TryGetValue(name, out var v)) return default;
                        if (v is DBNull) return default;
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