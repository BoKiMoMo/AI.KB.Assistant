using System;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AI.KB.Assistant.Models;     // for Item

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// SQLite 相關的擴充與工具：
    /// 1) DbService 的單筆 Insert 擴充（轉呼叫批次版）
    /// 2) 反射偵測 Microsoft.Data.Sqlite 並提供建立連線委派
    /// </summary>
    public static partial class DbServiceExtensions
    {
        /// <summary>
        /// 單筆插入便利擴充；內部轉呼叫批次版 InsertItemsAsync。
        /// </summary>
        public static Task<int> InsertAsync(this DbService db, Item item, CancellationToken _ = default)
            => db.InsertItemsAsync(new[] { item });

        /// <summary>
        /// 嘗試取得一個可建立 SqliteConnection 的委派（不直接參考套件，避免在無套件環境拋例外）。
        /// </summary>
        /// <param name="dbPath">資料庫檔案路徑（會自動建立資料夾）</param>
        /// <param name="createConnection">成功時回傳：() => IDbConnection</param>
        /// <param name="error">失敗時的例外</param>
        public static bool TryCreateSqliteConnection(
            string dbPath,
            out Func<IDbConnection> createConnection,
            out Exception? error)
        {
            createConnection = null!;
            error = null;

            try
            {
                if (string.IsNullOrWhiteSpace(dbPath))
                    throw new ArgumentNullException(nameof(dbPath));

                var dir = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // 以組件限定名反射載入，避免缺套件時類型解析就崩潰
                var connType = Type.GetType(
                    "Microsoft.Data.Sqlite.SqliteConnection, Microsoft.Data.Sqlite",
                    throwOnError: false);

                if (connType == null)
                {
                    error = new InvalidOperationException(
                        "找不到 Microsoft.Data.Sqlite。請安裝套件或改走檔案式儲存。");
                    return false;
                }

                // 工廠委派（不直接 new SqliteConnection）
                createConnection = () =>
                {
                    var connStr = $"Data Source={dbPath};Cache=Shared";
                    var conn = (IDbConnection)Activator.CreateInstance(connType, connStr)!;
                    return conn;
                };

                // smoke test：Open/Close 一次以確認可用
                using var test = createConnection();
                test.Open();
                test.Close();

                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                createConnection = null!;
                return false;
            }
        }
    }
}
