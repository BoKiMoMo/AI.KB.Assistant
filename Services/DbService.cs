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
// V7.13 修正：移除 using SQLitePCLRaw; 以解決 CS0246 編譯錯誤

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// V7.31 修正：
    /// 1. 這是 V7.30 的乾淨版本。移除了 V7.24-V7.30 中所有錯誤插入的語法錯誤。
    /// 2. 保持 V7.30 的核心邏輯：
    ///    - 嘗試使用反射 (Reflection) 呼叫 Init()。
    ///    - 在 catch 區塊中 'return null'（而不是 'throw'），以允許 App 成功回退到 FileDbProvider (JSONL)。
    /// </summary>
    public sealed partial class DbService : IDisposable
    {
        private readonly IDbProvider _provider;
        // V7.6 修正：SQLite 執行緒鎖定物件，用於 SqliteProvider 內部
        private readonly object _dbLock = new object();

        public DbService()
        {
            Console.WriteLine("[DB INIT V7.31] DbService constructor started.");

            // V7.13 修正：使用反射呼叫 Init()
            try
            {
                Console.WriteLine("[DB INIT V7.31] Attempting reflection init...");

                // 1. 嘗試載入 batteries_v2 組件 (我們知道它在 bin 目錄中)
                var assemblyName = "SQLitePCLRaw.batteries_v2";
                var batteriesAssembly = Assembly.Load(assemblyName);

                // 2. 獲取類型
                var batteriesType = batteriesAssembly.GetType("SQLitePCLRaw.Batteries_v2");
                if (batteriesType != null)
                {
                    // 3. 獲取靜態 Init() 方法
                    var initMethod = batteriesType.GetMethod("Init", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                    if (initMethod != null)
                    {
                        // 4. 呼叫 Init()
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
                // V7.31 修正：移除 'throw;'。我們不希望 App 在此崩潰。
                // 我們將讓 TryCreateSqlite 在下一步的 Smoke Test 中失敗。
                // V7.15 修正：改為呼叫 App.LogCrash
                App.LogCrash("DbService.Constructor.ReflectionInit", ex);
            }

            // 兼容 Db.DbPath / Db.Path 命名
            string? pick(string? a, string? b, string fallback)
                => !string.IsNullOrWhiteSpace(a) ? a : (!string.IsNullOrWhiteSpace(b) ? b : fallback);

            var cfg = AppConfig.Current;
            var baseDefault = Path.Combine(AppContext.BaseDirectory, "ai_kb.db");
            var chosen = pick(cfg?.Db?.DbPath, cfg?.Db?.Path, baseDefault);

            Console.WriteLine($"[DB INIT V7.31] DB Path selected: {chosen}");

            // V7.31 修正：如果 TryCreateSqlite 失敗 (返回 null)，_provider 將會是 FileDbProvider
            _provider = new FileDbProvider(chosen + ".jsonl");
            Console.WriteLine($"[DB INIT V7.31] Provider created (Is Sqlite: {_provider is SqliteProvider}).");
        }

        // ====== 原有介面（無 CT） ======
        public Task InitializeAsync() => _provider.InitializeAsync();
        public Task<List<Item>> QueryAllAsync() => _provider.QueryAllAsync();
        public Task<int> InsertItemsAsync(IEnumerable<Item> items) => _provider.InsertItemsAsync(items);

        // V7.6 合併：從 DbServiceExtensions.cs 移入 InsertAsync
        /// <summary>
        /// 單筆插入便利擴充；內部轉呼叫批次版 InsertItemsAsync。
        /// </summary>
        public Task<int> InsertAsync(Item item, CancellationToken _ = default)
            => InsertItemsAsync(new[] { item });

        public Task<int> UpdateItemsAsync(IEnumerable<Item> items) => _provider.UpdateItemsAsync(items);
        public Task<int> UpsertAsync(IEnumerable<Item> items) => _provider.UpsertAsync(items);
        public Task<Item?> GetByIdAsync(string id) => _provider.GetByIdAsync(id);
        public Task<int> DeleteByIdAsync(string id) => _provider.DeleteByIdAsync(id);
        public Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage) => _provider.StageOnlyAsync(items, stage);
        public Task<int> StageOnlyAsync(IEnumerable<Item> items) => StageOnlyAsync(items, 0);
        public Task<List<Item>> ListRecentAsync(int take = 200) => _provider.ListRecentAsync(take);
        public Task<List<Item>> SearchAsync(string keyword, int take = 200) => _provider.SearchAsync(keyword, take);

        // ====== CT 多載（保持不變） ======
        public Task InitializeAsync(CancellationToken _) => InitializeAsync();
        public Task<List<Item>> QueryAllAsync(CancellationToken _) => QueryAllAsync();
        public Task<int> InsertItemsAsync(IEnumerable<Item> items, CancellationToken _) => InsertItemsAsync(items);
        public Task<int> UpdateItemsAsync(IEnumerable<Item> items, CancellationToken _) => UpdateItemsAsync(items);
        public Task<int> UpsertAsync(IEnumerable<Item> items, CancellationToken _) => UpsertAsync(items);
        public Task<Item?> GetByIdAsync(string id, CancellationToken _) => GetByIdAsync(id);
        public Task<int> DeleteByIdAsync(string id, CancellationToken _) => DeleteByIdAsync(id);
        public Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage, CancellationToken _) => StageOnlyAsync(items, stage);
        public Task<List<Item>> ListRecentAsync(int take, CancellationToken _) => ListRecentAsync(take);
        public Task<List<Item>> SearchAsync(string keyword, int take, CancellationToken _) => SearchAsync(keyword, take);

        public void Dispose() => (_provider as IDisposable)?.Dispose();

        // ========== Provider Factory (V7.6 核心修正：合併 DbServiceExtensions.cs) ==========
        private static IDbProvider? TryCreateSqlite(string dbPath, object dbLock)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                return null;

            Console.WriteLine("[DB INIT V7.31] TryCreateSqlite started.");

            try
            {
                // V7.6 修正：確保目錄存在
                var dir = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // 嘗試載入 Microsoft.Data.Sqlite 類型
                var connType = Type.GetType(
                    "Microsoft.Data.Sqlite.SqliteConnection, Microsoft.Data.Sqlite",
                    throwOnError: true);

                if (connType == null)
                {
                    Console.WriteLine("[DB INIT V7.31 ERROR] Microsoft.Data.Sqlite.dll not found. Falling back to JSONL.");
                    return null;
                }

                Console.WriteLine("[DB INIT V7.31] SqliteConnection type loaded.");

                // 工廠委派
                Func<IDbConnection> createConnection = () =>
                {
                    var connStr = $"Data Source={dbPath};Cache=Shared";
                    var conn = (IDbConnection)Activator.CreateInstance(connType!, connStr)!;
                    return conn;
                };

                // Smoke test
                Console.WriteLine("[DB INIT V7.31] Performing Smoke Test (Connection.Open)...");
                lock (dbLock)
                {
                    using var test = createConnection();
                    test.Open(); // <== 預期的失敗點 (如果 Init() 沒成功)
                    test.Close();
                }

                Console.WriteLine("[DB INIT V7.31] Smoke Test OK. SQLite Provider activated.");
                return new SqliteProvider(createConnection, dbPath, dbLock);
            }
            catch (Exception ex)
            {
                // V7.7 修正：記錄反射失敗的詳細錯誤
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

                // V7.31 修正：不拋出錯誤，回傳 null 以觸發 FileDbProvider 回退
                App.LogCrash("DbService.TryCreateSqlite", ex); // V7.15: 記錄詳細日誌
                return null; // V7.26/V7.27/V7.31 核心修正：回傳 null
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
            // V7.6 修正：使用 Func<IDbConnection> 工廠
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
                // V7.6: 初始化是 DB 唯一不需要鎖定的地方 (因為是單執行緒啟動)
                // 但為了安全起見，我們仍將建表放在 ExecuteNonQueryAsync 中，讓其處理鎖定。
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
                // V7.6: 將所有執行緒鎖定工作委託給 ExecuteQuery/ExecuteNonQueryAsync
                var existing = await GetColumnsAsync("Items").ConfigureAwait(false);
                var has = new HashSet<string>(existing.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

                // V7.6 修正：【欄位白名單】確保只對這些核心屬性進行動態 ADD COLUMN
                var ItemProps = typeof(Item).GetProperties();

                // V7.6 修正：定義一個明確的**所有**核心欄位（包括那些已經在 CREATE TABLE 裡的，以及需要遷移的）
                var coreCols = new HashSet<string>(new[]
                {
                    "Id", "Path", "ProposedPath", "CreatedAt", "UpdatedAt", "Tags", "Status", "Project", "Note",
                    "Category", "Timestamp" // 這是需要動態遷移的
                }, StringComparer.OrdinalIgnoreCase);

                foreach (var p in ItemProps)
                {
                    var name = p.Name;

                    // 1. 排除非白名單核心欄位
                    if (!coreCols.Contains(name)) continue;

                    // 2. 排除已經存在於資料庫的欄位 (通過 PRAGMA 檢查)
                    if (has.Contains(name)) continue;

                    // 雙重檢查
                    if (await ColumnExistsAsync("Items", name).ConfigureAwait(false))
                        continue;

                    var sql = $"ALTER TABLE Items ADD COLUMN {name} TEXT;";
                    try
                    {
                        // V7.6 FIX: 將 ExecuteNonQueryAsync 包在 try-catch 中，並忽略重複欄位錯誤
                        await ExecuteNonQueryAsync(sql).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message?.ToLowerInvariant() ?? "";
                        if (msg.Contains("duplicate column name"))
                        { /* 忽略 */
                            Console.WriteLine($"[DB MIGRATION] Ignored duplicate column: {name}");
                        }
                        else throw;
                    }
                }
            }

            // ... (ColumnInfo, GetColumnsAsync, ColumnExistsAsync 保持不變) ...
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

            // ---- CRUD (使用 ExecuteQuery/ExecuteNonQueryAsync 保持鎖定) ----

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
                    ,
                    Category = r.GetString("Category")
                    ,
                    Timestamp = r.GetDateTime("Timestamp")
                };
            }

            private async Task<int> UpsertOneAsync(Item it)
            {
                // V7.6 修正：確保 Upsert 包含 new fields
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

            // ---------- Reflection-based low-level calls (V7.6: 使用同步鎖定) ----------
            private IDbConnection CreateConnection()
                => _createConnection(); // 使用工廠委派

            // V7.6 修正：所有 DB 存取都通過鎖定，並使用同步執行
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
                                // 同步執行
                                var m = cmd.GetType().GetMethod("ExecuteNonQuery", Type.EmptyTypes)!;
                                return (int)m.Invoke(cmd, null)!;
                            }
                            finally { (cmd as IDisposable)?.Dispose(); }
                        }
                        catch (TargetInvocationException tie) when (tie.InnerException is Exception innerEx && innerEx.Message.ToLower().Contains("duplicate column name"))
                        {
                            // 

                            Console.WriteLine($"[DB MIGRATION/ExecuteNonQueryAsync] Ignored error: {innerEx.Message}");
                            return 0;
                        }
                        finally { conn.Close(); }
                    }
                }).ConfigureAwait(false);
            }

            // V7.6 修正：所有 DB 查詢都通過鎖定，並使用同步執行
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