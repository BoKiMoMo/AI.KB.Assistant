using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using AI.KB.Assistant.Models;

namespace AI.KB.Assistant.Services;

public sealed class DbService : IDisposable
{
	private readonly string _dbPath;

	public DbService(string dbPath)
	{
		_dbPath = dbPath;
		Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
		EnsureSchema();
	}

	private SqliteConnection Open()
	{
		var conn = new SqliteConnection($"Data Source={_dbPath}");
		conn.Open();
		return conn;
	}

	private void EnsureSchema()
	{
		using var conn = Open();
		conn.Execute(
			"""
            CREATE TABLE IF NOT EXISTS items(
              Path TEXT PRIMARY KEY,
              Filename TEXT,
              Category TEXT,
              Confidence REAL,
              CreatedTs INTEGER,
              Summary TEXT,
              Reasoning TEXT,
              Status TEXT,
              Tags TEXT
            );
            """);
		conn.Execute("CREATE INDEX IF NOT EXISTS idx_items_created ON items(CreatedTs DESC);");
		conn.Execute("CREATE INDEX IF NOT EXISTS idx_items_cat ON items(Category);");
	}

	public void Upsert(Item it)
	{
		using var conn = Open();
		conn.Execute(
			"""
            INSERT INTO items(Path,Filename,Category,Confidence,CreatedTs,Summary,Reasoning,Status,Tags)
            VALUES(@Path,@Filename,@Category,@Confidence,@CreatedTs,@Summary,@Reasoning,@Status,@Tags)
            ON CONFLICT(Path) DO UPDATE SET
              Filename=excluded.Filename,
              Category=excluded.Category,
              Confidence=excluded.Confidence,
              CreatedTs=excluded.CreatedTs,
              Summary=excluded.Summary,
              Reasoning=excluded.Reasoning,
              Status=excluded.Status,
              Tags=excluded.Tags;
            """, it);
	}

	// 方便相容舊呼叫
	public System.Threading.Tasks.Task AddAsync(Item it) { Upsert(it); return System.Threading.Tasks.Task.CompletedTask; }
	public System.Threading.Tasks.Task Update(Item it) { Upsert(it); return System.Threading.Tasks.Task.CompletedTask; }

	public IEnumerable<Item> Recent(int days)
	{
		using var conn = Open();
		var since = DateTimeOffset.Now.AddDays(-days).ToUnixTimeSeconds();
		return conn.Query<Item>("SELECT * FROM items WHERE CreatedTs>=@since ORDER BY CreatedTs DESC", new { since });
	}

	public IEnumerable<Item> ByStatus(string status)
	{
		if (string.IsNullOrWhiteSpace(status)) return Enumerable.Empty<Item>();
		using var conn = Open();
		return conn.Query<Item>("SELECT * FROM items WHERE Status=@status ORDER BY CreatedTs DESC", new { status });
	}

	public IEnumerable<Item> Search(string kw)
	{
		using var conn = Open();
		var like = $"%{kw}%";
		return conn.Query<Item>(
			"""
            SELECT * FROM items
            WHERE Filename LIKE @like OR Category LIKE @like OR Summary LIKE @like
            ORDER BY CreatedTs DESC
            """, new { like });
	}

	public IEnumerable<string> GetCategories()
	{
		using var conn = Open();
		var rows = conn.Query<string>("SELECT DISTINCT Category FROM items WHERE IFNULL(Category,'')<>'' ORDER BY Category ASC");
		return rows ?? Enumerable.Empty<string>();
	}

	public void Dispose() { }
}
