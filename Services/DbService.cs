using System;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services
{
	public sealed class DbService : IDisposable
	{
		private readonly string _dbPath;
		private readonly string _connStr;

		public DbService(string dbPath)
		{
			_dbPath = dbPath;
			Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
			_connStr = $"Data Source={_dbPath}";
			EnsureTables();
		}

		private SqliteConnection Open() => new SqliteConnection(_connStr);

		private void EnsureTables()
		{
			using var cn = Open();
			cn.Execute("""
                CREATE TABLE IF NOT EXISTS items(
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    path         TEXT NOT NULL,
                    filename     TEXT NOT NULL,
                    category     TEXT,
                    confidence   REAL,
                    created_ts   INTEGER,
                    summary      TEXT,
                    reasoning    TEXT,
                    status       TEXT,
                    tags         TEXT
                );
            """);
		}

		/// <summary>插入一筆 Item，回傳 rowid</summary>
		public long Add(Item it)
		{
			using var cn = Open();
			var sql = @"
INSERT INTO items(path, filename, category, confidence, created_ts, summary, reasoning, status, tags)
VALUES(@Path, @Filename, @Category, @Confidence, @CreatedTs, @Summary, @Reasoning, @Status, @Tags);
SELECT last_insert_rowid();";
			return cn.ExecuteScalar<long>(sql, it);
		}

		public void Dispose() { /* 目前無需釋放 */ }
	}
}
