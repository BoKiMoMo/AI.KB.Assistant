using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// 資料層統一入口。
    /// - 若偵測到 Microsoft.Data.Sqlite 存在：採用 SQLite Provider（反射呼叫，避免編譯期相依）。
    /// - 否則回退為 FileDb（JSONL 檔案，無鎖輕量儲存）。
    ///
    /// 你可以隨時安裝 NuGet：Microsoft.Data.Sqlite 後，重啟 App 即自動改用 SQLite。
    /// </summary>
    public sealed class DbService : IDisposable
    {
        private readonly IDbProvider _provider;

        public DbService()
        {
            // 兼容 Db.DbPath / Db.Path 兩種命名
            string? pick(string? a, string? b, string fallback)
                => !string.IsNullOrWhiteSpace(a) ? a : (!string.IsNullOrWhiteSpace(b) ? b : fallback);

            var cfg = ConfigService.Cfg;
            var baseDefault = Path.Combine(AppContext.BaseDirectory, "ai_kb.db");
            var chosen = pick(cfg?.Db?.DbPath, cfg?.Db?.Path, baseDefault);

            _provider = TryCreateSqlite(chosen) ?? new FileDbProvider(chosen + ".jsonl");
        }

        public Task InitializeAsync(CancellationToken ct = default) => _provider.InitializeAsync(ct);

        public Task<List<Item>> QueryAllAsync(CancellationToken ct = default) => _provider.QueryAllAsync(ct);

        public Task<int> InsertItemsAsync(IEnumerable<Item> items, CancellationToken ct = default)
            => _provider.InsertItemsAsync(items, ct);

        public Task<int> UpdateItemsAsync(IEnumerable<Item> items, CancellationToken ct = default)
            => _provider.UpdateItemsAsync(items, ct);

        /// <summary>僅更新狀態（資料層標記），不進行實際搬檔。</summary>
        public Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage, CancellationToken ct = default)
            => _provider.StageOnlyAsync(items, stage, ct);

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
                // 沒有套件或載入失敗 → 回退 FileDb
                return null;
            }
        }

        // ========== Provider Abstraction ==========

        private interface IDbProvider : IDisposable
        {
            Task InitializeAsync(CancellationToken ct);
            Task<List<Item>> QueryAllAsync(CancellationToken ct);
            Task<int> InsertItemsAsync(IEnumerable<Item> items, CancellationToken ct);
            Task<int> UpdateItemsAsync(IEnumerable<Item> items, CancellationToken ct);
            Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage, CancellationToken ct);
        }

        #region FileDbProvider（無需任何外部套件）
        private sealed class FileDbProvider : IDbProvider
        {
            private readonly string _path;
            private static readonly SemaphoreSlim _gate = new(1, 1);
            private readonly JsonSerializerOptions _opts = new()
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            public FileDbProvider(string path) { _path = path; }

            public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;

            public async Task<List<Item>> QueryAllAsync(CancellationToken ct)
            {
                var list = new List<Item>();
                if (!File.Exists(_path)) return list;

                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                while (!sr.EndOfStream)
                {
                    ct.ThrowIfCancellationRequested();
                    var line = await sr.ReadLineAsync().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var item = JsonSerializer.Deserialize<Item>(line);
                        if (item != null) list.Add(item);
                    }
                    catch { /* 忽略單行錯誤 */ }
                }
                return list;
            }

            public async Task<int> InsertItemsAsync(IEnumerable<Item> items, CancellationToken ct)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                await _gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    using var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
                    using var sw = new StreamWriter(fs);
                    var now = DateTime.UtcNow;
                    var count = 0;
                    foreach (var it in items)
                    {
                        ct.ThrowIfCancellationRequested();
                        it.UpdatedAt = now;
                        it.CreatedAt = it.CreatedAt == default ? now : it.CreatedAt;
                        await sw.WriteLineAsync(JsonSerializer.Serialize(it, _opts)).ConfigureAwait(false);
                        count++;
                    }
                    return count;
                }
                finally { _gate.Release(); }
            }

            public async Task<int> UpdateItemsAsync(IEnumerable<Item> items, CancellationToken ct)
            {
                // 簡化：讀全檔→以 Id 為鍵覆蓋→重寫（V7 可接受；資料量大時建議切 SQLite）
                var all = await QueryAllAsync(ct).ConfigureAwait(false);
                var map = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in all) if (!string.IsNullOrEmpty(a.Id)) map[a.Id] = a;

                var now = DateTime.UtcNow;
                int updated = 0;
                foreach (var it in items)
                {
                    if (string.IsNullOrWhiteSpace(it.Id)) continue;
                    it.UpdatedAt = now;
                    map[it.Id] = it;
                    updated++;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                using var fs = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.None);
                using var sw = new StreamWriter(fs);
                foreach (var kv in map.Values)
                    await sw.WriteLineAsync(JsonSerializer.Serialize(kv, _opts)).ConfigureAwait(false);

                return updated;
            }

            public Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage, CancellationToken ct)
            {
                foreach (var it in items)
                    it.Status = $"stage:{stage}";
                return UpdateItemsAsync(items, ct);
            }

            public void Dispose() { /* no-op */ }
        }
        #endregion

        #region SqliteProvider（需要 Microsoft.Data.Sqlite；以反射呼叫）
        private sealed class SqliteProvider : IDbProvider
        {
            private readonly Type _connType;
            private readonly string _dbPath;   // 純檔案路徑
            private readonly string _connStr;  // 正確連線字串

            public SqliteProvider(Type connType, string dbPath)
            {
                _connType = connType;

                // 轉為絕對路徑並確保資料夾存在
                _dbPath = string.IsNullOrWhiteSpace(dbPath)
                    ? Path.Combine(AppContext.BaseDirectory, "ai_kb.db")
                    : (Path.IsPathRooted(dbPath) ? dbPath : Path.Combine(AppContext.BaseDirectory, dbPath));

                var dir = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                // 正確的連線字串
                _connStr = $"Data Source={_dbPath};Cache=Shared;Mode=ReadWriteCreate;";
            }

            public async Task InitializeAsync(CancellationToken ct)
            {
                var sql = @"
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
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
CREATE INDEX IF NOT EXISTS IX_Items_Path ON Items(Path);
";
                await ExecuteNonQueryAsync(sql, null, ct).ConfigureAwait(false);
            }

            public async Task<List<Item>> QueryAllAsync(CancellationToken ct)
            {
                var rows = await ExecuteQueryAsync("SELECT * FROM Items ORDER BY UpdatedAt DESC;", null, ct).ConfigureAwait(false);
                var list = new List<Item>();
                foreach (var r in rows)
                {
                    var item = new Item
                    {
                        Id = r.GetString("Id") ?? Guid.NewGuid().ToString("N"),
                        Path = r.GetString("Path") ?? string.Empty,
                        ProposedPath = r.GetString("ProposedPath") ?? string.Empty,
                        CreatedAt = r.GetDateTime("CreatedAt") ?? DateTime.UtcNow,
                        UpdatedAt = r.GetDateTime("UpdatedAt") ?? DateTime.UtcNow,
                        Tags = r.GetTags("Tags"),
                        Status = r.GetString("Status"),
                        Project = r.GetString("Project"),
                        Note = r.GetString("Note")
                    };
                    list.Add(item);
                }
                return list;
            }

            public async Task<int> InsertItemsAsync(IEnumerable<Item> items, CancellationToken ct)
            {
                int n = 0;
                foreach (var it in items)
                {
                    ct.ThrowIfCancellationRequested();
                    var id = string.IsNullOrWhiteSpace(it.Id) ? Guid.NewGuid().ToString("N") : it.Id;
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
                        ["$Id"] = id,
                        ["$Path"] = it.Path,
                        ["$ProposedPath"] = it.ProposedPath,
                        ["$CreatedAt"] = ToIso(it.CreatedAt == default ? DateTime.UtcNow : it.CreatedAt),
                        ["$UpdatedAt"] = ToIso(DateTime.UtcNow),
                        ["$Tags"] = JsonSerializer.Serialize(it.Tags ?? new List<string>()),
                        ["$Status"] = it.Status,
                        ["$Project"] = it.Project,
                        ["$Note"] = it.Note
                    };
                    n += await ExecuteNonQueryAsync(sql, p, ct).ConfigureAwait(false);
                }
                return n;
            }

            public async Task<int> UpdateItemsAsync(IEnumerable<Item> items, CancellationToken ct)
            {
                int n = 0;
                foreach (var it in items)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(it.Id)) continue;

                    var sql = @"
UPDATE Items SET
    Path=$Path,
    ProposedPath=$ProposedPath,
    CreatedAt=$CreatedAt,
    UpdatedAt=$UpdatedAt,
    Tags=$Tags,
    Status=$Status,
    Project=$Project,
    Note=$Note
WHERE Id=$Id;";
                    var p = new Dictionary<string, object?>
                    {
                        ["$Id"] = it.Id,
                        ["$Path"] = it.Path,
                        ["$ProposedPath"] = it.ProposedPath,
                        ["$CreatedAt"] = ToIso(it.CreatedAt == default ? DateTime.UtcNow : it.CreatedAt),
                        ["$UpdatedAt"] = ToIso(DateTime.UtcNow),
                        ["$Tags"] = JsonSerializer.Serialize(it.Tags ?? new List<string>()),
                        ["$Status"] = it.Status,
                        ["$Project"] = it.Project,
                        ["$Note"] = it.Note
                    };
                    n += await ExecuteNonQueryAsync(sql, p, ct).ConfigureAwait(false);
                }
                return n;
            }

            public Task<int> StageOnlyAsync(IEnumerable<Item> items, int stage, CancellationToken ct)
            {
                foreach (var it in items)
                    it.Status = $"stage:{stage}";
                return UpdateItemsAsync(items, ct);
            }

            public void Dispose() { /* 連線皆為短生命週期，無需釋放 */ }

            // ---------- Reflection helpers ----------
            private async Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object?>? prms, CancellationToken ct)
            {
                var conn = CreateConnection();
                try
                {
                    await OpenAsync(conn, ct).ConfigureAwait(false);
                    var cmd = CreateCommand(conn, sql, prms);
                    try
                    {
                        return await ExecuteNonQueryCoreAsync(cmd, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        (cmd as IDisposable)?.Dispose();
                    }
                }
                finally
                {
                    (conn as IDisposable)?.Dispose();
                }
            }

            private async Task<List<Row>> ExecuteQueryAsync(string sql, Dictionary<string, object?>? prms, CancellationToken ct)
            {
                var conn = CreateConnection();
                try
                {
                    await OpenAsync(conn, ct).ConfigureAwait(false);
                    var cmd = CreateCommand(conn, sql, prms);
                    try
                    {
                        return await ExecuteReaderRowsAsync(cmd, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        (cmd as IDisposable)?.Dispose();
                    }
                }
                finally
                {
                    (conn as IDisposable)?.Dispose();
                }
            }

            private object CreateConnection()
            {
                // 直接使用 SqliteConnection(string connectionString)
                var conn = Activator.CreateInstance(_connType, new object[] { _connStr })!;
                return conn;
            }

            private static async Task OpenAsync(object conn, CancellationToken ct)
            {
                var m = conn.GetType().GetMethod("OpenAsync", new[] { typeof(CancellationToken) })!;
                await (Task)m.Invoke(conn, new object[] { ct })!;
            }

            private static object CreateCommand(object conn, string sql, Dictionary<string, object?>? prms)
            {
                var cmd = conn.GetType().Assembly.GetType("Microsoft.Data.Sqlite.SqliteCommand")!
                    .GetConstructor(new[] { typeof(string), conn.GetType() })!
                    .Invoke(new object[] { sql, conn });
                if (prms != null)
                {
                    var parmsProp = cmd.GetType().GetProperty("Parameters")!;
                    var parms = parmsProp.GetValue(cmd)!;
                    var addWithValue = parms.GetType().GetMethod("AddWithValue", new[] { typeof(string), typeof(object) })!;
                    foreach (var kv in prms)
                        addWithValue.Invoke(parms, new object[] { kv.Key, kv.Value ?? DBNull.Value });
                }
                return cmd;
            }

            private static async Task<int> ExecuteNonQueryCoreAsync(object cmd, CancellationToken ct)
            {
                var m = cmd.GetType().GetMethod("ExecuteNonQueryAsync", new[] { typeof(CancellationToken) })!;
                return await (Task<int>)m.Invoke(cmd, new object[] { ct })!;
            }

            private static async Task<List<Row>> ExecuteReaderRowsAsync(object cmd, CancellationToken ct)
            {
                var list = new List<Row>();
                var execReader = cmd.GetType().GetMethod("ExecuteReaderAsync", new[] { typeof(CancellationToken) })!;
                var reader = await (dynamic)execReader.Invoke(cmd, new object[] { ct })!;
                try
                {
                    while (await reader.ReadAsync(ct))
                    {
                        var row = new Row(reader);
                        list.Add(row);
                    }
                }
                finally
                {
                    try { (reader as IDisposable)?.Dispose(); } catch { /* ignore */ }
                }
                return list;
            }

            private static string ToIso(DateTime dt) => dt.ToString("o", CultureInfo.InvariantCulture);

            private sealed class Row
            {
                private readonly dynamic _r;
                public Row(dynamic reader) { _r = reader; }
                public string? GetString(string name) => TryGet(name, v => v?.ToString());
                public DateTime? GetDateTime(string name)
                    => TryGet(name, v => v is null ? (DateTime?)null : DateTime.Parse(v.ToString()!));
                public List<string> GetTags(string name)
                {
                    var s = GetString(name);
                    if (string.IsNullOrWhiteSpace(s)) return new();
                    try { return JsonSerializer.Deserialize<List<string>>(s!) ?? new(); } catch { return new(); }
                }
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
        }
        #endregion
    }
}
