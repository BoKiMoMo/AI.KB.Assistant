using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
    /// <summary>
    /// SQLite 資料庫存取服務
    /// </summary>
    public class DbService
    {
        private readonly string _dbPath;

        public DbService(string dbPath)
        {
            _dbPath = dbPath;
            EnsureDb();
        }

        private IDbConnection GetConn()
            => new SqliteConnection($"Data Source={_dbPath}");

        private void EnsureDb()
        {
            if (!File.Exists(_dbPath))
            {
                using var conn = GetConn();
                conn.Execute(@"
                CREATE TABLE IF NOT EXISTS Items (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Path TEXT,
                    Filename TEXT,
                    Category TEXT,
                    Status TEXT,
                    Tags TEXT,
                    Project TEXT,
                    FileType TEXT,
                    Confidence REAL,
                    CreatedTs INTEGER
                )");
            }
        }

        public void Add(Item item)
        {
            using var conn = GetConn();
            conn.Execute(@"
                INSERT INTO Items (Path, Filename, Category, Status, Tags, Project, FileType, Confidence, CreatedTs)
                VALUES (@Path, @Filename, @Category, @Status, @Tags, @Project, @FileType, @Confidence, @CreatedTs)",
                item);
        }

        public IEnumerable<Item> Recent(int limit = 50)
        {
            using var conn = GetConn();
            return conn.Query<Item>($"SELECT * FROM Items ORDER BY Id DESC LIMIT {limit}");
        }

        public IEnumerable<Item> ByStatus(string status)
        {
            using var conn = GetConn();
            return conn.Query<Item>("SELECT * FROM Items WHERE Status=@status", new { status });
        }

        public IEnumerable<Item> Search(string keyword)
        {
            using var conn = GetConn();
            return conn.Query<Item>(
                "SELECT * FROM Items WHERE Filename LIKE @kw OR Tags LIKE @kw",
                new { kw = $"%{keyword}%" });
        }

        public void Update(Item item)
        {
            using var conn = GetConn();
            conn.Execute(@"
                UPDATE Items
                SET Path=@Path, Filename=@Filename, Category=@Category,
                    Status=@Status, Tags=@Tags, Project=@Project, FileType=@FileType,
                    Confidence=@Confidence, CreatedTs=@CreatedTs
                WHERE Id=@Id", item);
        }
    }
}
