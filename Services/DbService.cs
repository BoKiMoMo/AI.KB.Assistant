using System;
using System.Collections.Generic;
using System.Data; // 必須引用 System.Data
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection; // 為了 PropertyInfo
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;
using AI.KB.Assistant.Views; // V7.34 修正：加入 App.LogCrash 依賴

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// V7.31 修正 + V7.35 新功能
    /// </summary>
    public sealed partial class DbService : IDisposable
    {
        private readonly IDbProvider _provider;
        private readonly object _dbLock = new object();

        public DbService()
        {
            Console.WriteLine("[DB INIT V7.31] DbService constructor started.");

            // V7.13 修正：使用反射呼叫 Init()
            try
            {
                Console.WriteLine("[DB INIT V7.31] Attempting reflection init...");
                var assemblyName = "SQLitePCLRaw.batteries_v2";
                var batteriesAssembly = Assembly.Load(assemblyName);
                var batteriesType = batteriesAssembly.GetType("SQLitePCLRaw.Batteries_v2");
                if (batteriesType != null)
                {
                    var initMethod = batteriesType.GetMethod("Init", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                    if (initMethod != null)
                    {
                        initMethod.Invoke(null, null);
                        Console.WriteLine("[DB INIT V7.31] Reflection Init() call successful.");
                    }
                    else
                    {
                        Console.WriteLine("[DB INIT V7.31 ERROR] Reflection: 'Init' method not found on Batteries_v2.");
                    }
                }
                else
                {
                    Console.WriteLine("[DB INIT V7.31 ERROR] Reflection: 'SQLitePCLRaw.Batteries_v2' type not found in assembly.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB INIT V7.31 ERROR] Reflection init failed: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[DB INIT V7.31 INNER_EX] {ex.InnerException.Message}");
                }
                App.LogCrash("DbService.Constructor.ReflectionInit", ex);
            }

            string? pick(string? a, string? b, string fallback)
                => !string.IsNullOrWhiteSpace(a) ? a : (!string.IsNullOrWhiteSpace(b) ? b : fallback);

            // V7.34 重構修正：改用 'ConfigService.Cfg'
            var cfg = ConfigService.Cfg;
            var baseDefault = Path.Combine(AppContext.BaseDirectory, "ai_kb.db");
            var chosen = pick(cfg?.Db?.DbPath, cfg?.Db?.Path, baseDefault);

            Console.WriteLine($"[DB INIT V7.31] DB Path selected: {chosen}");

            // V7.31 修正：如果 TryCreateSqlite 失敗 (返回 null)，_provider 將會是 FileDbProvider
            // (L75) chosen 在此傳入
            _provider = TryCreateSqlite(chosen, _dbLock) ?? new FileDbProvider(chosen + ".jsonl");
            Console.WriteLine($"[DB INIT V7.31] Provider created (Is Sqlite: {_provider is SqliteProvider}).");
        }

        // ====== 介面實作 ======
        public Task InitializeAsync() => _provider.InitializeAsync();
        public Task<List<Item>> QueryAllAsync() => _provider.QueryAllAsync();
        public Task<int> InsertItemsAsync(IEnumerable<Item> items) => _provider.InsertItemsAsync(items);
        public Task<int> InsertAsync(Item item, CancellationToken _ = default)
            => InsertItemsAsync(new[] { item });
        public Task<int> UpdateItemsAsync(IEnumerable<Item> items) => _provider.UpdateItemsAsync(items);
        public Task<int> UpsertAsync(IEnumerable<Item> items) => _provider.UpsertAsync(items);
        public Task<Item?> GetByIdAsync(string id) => _provider.GetByIdAsync(id);
        public Task<int> DeleteByIdAsync(string id) => _provider.DeleteByIdAsync(id);

        // V7.35 新增 (用於 選項 B)：批次刪除
        public Task<int> DeleteItemsAsync(IEnumerable<string> ids) => _provider.DeleteItemsAsync(ids);

        // V7.35 新增 (用於 選項 C)：重置收件夾
        public Task<int> DeleteNonCommittedAsync() => _provider.DeleteNonCommittedAsync();

        public Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage) => _provider.StageOnlyAsync(items, stage);
        public Task<int> StageOnlyAsync(IEnumerable<Item> items) => StageOnlyAsync(items, 0);
        public Task<List<Item>> ListRecentAsync(int take = 200) => _provider.ListRecentAsync(take);
        public Task<List<Item>> SearchAsync(string keyword, int take = 200) => _provider.SearchAsync(keyword, take);

        // (CT 多載省略...)
        public void Dispose() => (_provider as IDisposable)?.Dispose();

        // ========== Provider Factory ==========
        // (L117) CS8604 修正：將 'string dbPath' 改為 'string? dbPath'
        private static IDbProvider? TryCreateSqlite(string? dbPath, object dbLock)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                return null;
            Console.WriteLine("[DB INIT V7.31] TryCreateSqlite started.");
            try
            {
                var dir = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var connType = Type.GetType(
                    "Microsoft.Data.Sqlite.SqliteConnection, Microsoft.Data.Sqlite",
                    throwOnError: true);
                if (connType == null)
                {
                    Console.WriteLine("[DB INIT V7.31 ERROR] Microsoft.Data.Sqlite.dll not found. Falling back to JSONL.");
                    return null;
                }
                Console.WriteLine("[DB INIT V7.31] SqliteConnection type loaded.");

                Func<IDbConnection> createConnection = () =>
                {
                    var connStr = $"Data Source={dbPath};Cache=Shared";
                    var conn = (IDbConnection)Activator.CreateInstance(connType!, connStr)!;
                    return conn;
                };

                Console.WriteLine("[DB INIT V7.31] Performing Smoke Test (Connection.Open)...");
                lock (dbLock)
                {
                    using var test = createConnection();
                    test.Open();
                    test.Close();
                }
                Console.WriteLine("[DB INIT V7.31] Smoke Test OK. SQLite Provider activated.");
                return new SqliteProvider(createConnection, dbPath, dbLock);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB INIT V7.31 ERROR] SQLite Smoke Test Failed: {ex.Message}");
                Exception? inner = ex;
                int depth = 0;
                while (inner != null && depth < 5)
                {
                    Console.WriteLine($"[DB INIT V7.31 INNER_EX {depth}] {inner.Message}");
                    Console.WriteLine(inner.StackTrace);
                    inner = inner.InnerException;
                    depth++;
                }
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
            Task<int> DeleteItemsAsync(IEnumerable<string> ids); // V7.35 新增
            Task<int> DeleteNonCommittedAsync(); // V7.35 新增
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

            public FileDbProvider(string path)
            {
                _path = path;
                Console.WriteLine($"[DB INIT V7.31] FALLBACK: FileDbProvider (JSONL) activated at: {_path}");
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

            // V7.35 新增 (選項 B)
            public Task<int> DeleteItemsAsync(IEnumerable<string> ids)
            {
                var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
                var list = new List<Item>(ReadAll());
                var n = list.RemoveAll(it => !string.IsNullOrWhiteSpace(it.Id) && idSet.Contains(it.Id));
                WriteAll(list);
                return Task.FromResult(n);
            }

            // V7.35 新增 (選項 C)
            public Task<int> DeleteNonCommittedAsync()
            {
                var list = new List<Item>(ReadAll());
                // 刪除所有 "committed" 以外的狀態 (null, "", "intaked", "error", "stage:X")
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

        #region SqliteProvider（反射呼叫 Microsoft.Data.Sqlite）
        private sealed class SqliteProvider : IDbProvider
        {
            private readonly Func<IDbConnection> _createConnection;
            private readonly string _dbPath;
            private readonly string _connStr;
            private readonly object _lock;

            public SqliteProvider(Func<IDbConnection> createConnection, string dbPath, object lockObject)
            {
                _createConnection = createConnection;
                _lock = lockObject;
                _dbPath = dbPath;
                _connStr = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;";
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
                var ItemProps = typeof(Item).GetProperties();
                var coreCols = new HashSet<string>(new[]
                {
                    "Id", "Path", "ProposedPath", "CreatedAt", "UpdatedAt", "Tags", "Status", "Project", "Note",
                    "Category", "Timestamp"
                }, StringComparer.OrdinalIgnoreCase);

                foreach (var p in ItemProps)
                {
                    var name = p.Name;
                    if (!coreCols.Contains(name)) continue;
                    if (has.Contains(name)) continue;
                    if (await ColumnExistsAsync("Items", name).ConfigureAwait(false))
                        continue;

                    var sql = $"ALTER TABLE Items ADD COLUMN {name} TEXT;";
                    try
                    {
                        await ExecuteNonQueryAsync(sql).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message?.ToLowerInvariant() ?? "";
                        if (msg.Contains("duplicate column name"))
                        {
                            Console.WriteLine($"[DB MIGRATION] Ignored duplicate column: {name}");
                        }
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

            // V7.35 新增：批次刪除 (選項 B)
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

            // V7.35 新增：重置收件夾 (選項 C)
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
                    Note = r.GetString("Note")
                    ,
                    Category = r.GetString("Category")
                    ,
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

            private IDbConnection CreateConnection()
                => _createConnection();

            private async Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object?>? prms = null)
            {
                return await Task.Run(() =>
                {
                    lock (_lock)
                    {
                        using var conn = CreateConnection();
                        try
                        {
                            conn.Open();
                            var cmd = CreateCommand(conn, sql, prms);
                            try
                            {
                                var m = cmd.GetType().GetMethod("ExecuteNonQuery", Type.EmptyTypes)!;
                                return (int)m.Invoke(cmd, null)!;
                            }
                            finally { (cmd as IDisposable)?.Dispose(); }
                        }
                        catch (TargetInvocationException tie) when (tie.InnerException is Exception innerEx && innerEx.Message.ToLower().Contains("duplicate column name"))
                        {
                            Console.WriteLine($"[DB MIGRATION/ExecuteNonQueryAsync] Ignored error: {innerEx.Message}");
                            return 0;
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
                        using var conn = CreateConnection();
                        try
                        {
                            conn.Open();
                            var cmd = CreateCommand(conn, sql, prms);
                            try { return ExecuteReaderRows(cmd); }
                            finally { (cmd as IDisposable)?.Dispose(); }
                        }
                        finally { conn.Close(); }
                    }
                }).ConfigureAwait(false);
            }

            private static List<Row> ExecuteReaderRows(object cmd)
            {
                var list = new List<Row>();
                var exec = cmd.GetType().GetMethod("ExecuteReader", Type.EmptyTypes)!;
                dynamic reader = exec.Invoke(cmd, null)!;

                try
                {
                    var mRead = reader.GetType().GetMethod("Read", Type.EmptyTypes)!;
                    while ((bool)mRead.Invoke(reader, null)!)
                        list.Add(new Row(reader));
                }
                finally
                {
                    try { (reader as IDisposable)?.Dispose(); } catch { }
                }
                return list;
            }

            private static object CreateCommand(IDbConnection conn, string sql, Dictionary<string, object?>? prms)
            {
                var asm = conn.GetType().Assembly;
                var cmdType = asm.GetType("Microsoft.Data.Sqlite.SqliteCommand")!;
                var cmd = cmdType
                    .GetConstructor(new[] { typeof(string), conn.GetType() })!
                    .Invoke(new object[] { sql, conn });

                if (prms != null && prms.Count > 0)
                {
                    var parmsProp = cmdType.GetProperty(
                        "Parameters",
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.DeclaredOnly
                    )!;
                    var parms = parmsProp.GetValue(cmd)!;

                    var addWithValue = parms.GetType().GetMethod("AddWithValue", new[] { typeof(string), typeof(object) })!;
                    foreach (var kv in prms)
                    {
                        addWithValue.Invoke(parms, new object[] { kv.Key, kv.Value ?? DBNull.Value });
                    }
                }

                return cmd;
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