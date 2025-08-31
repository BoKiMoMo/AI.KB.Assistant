using AI.KB.Assistant.Models;
using Dapper;
using Microsoft.Data.Sqlite;
using System.IO;

namespace AI.KB.Assistant.Services;

public class DbService : IDisposable
{
	private readonly SqliteConnection _conn;

	public DbService(string dbPath)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
		_conn = new SqliteConnection($"Data Source={dbPath}");
		_conn.Open();

		_conn.Execute("""
            CREATE TABLE IF NOT EXISTS items (
              id          INTEGER PRIMARY KEY AUTOINCREMENT,
              path        TEXT NOT NULL,
              filename    TEXT NOT NULL,
              category    TEXT,
              status      TEXT DEFAULT 'To-Do',
              confidence  REAL DEFAULT 0,    -- 0~100
              created_ts  INTEGER,
              summary     TEXT,
              reasoning   TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_items_created  ON items(created_ts);
            CREATE INDEX IF NOT EXISTS idx_items_category ON items(category);
            CREATE INDEX IF NOT EXISTS idx_items_status   ON items(status);
        """);
	}

	public void Upsert(Item it)
	{
		_conn.Execute("""
            INSERT INTO items(path, filename, category, status, confidence, created_ts, summary, reasoning)
            VALUES (@Path, @Filename, @Category, @Status, @Confidence, @CreatedTs, @Summary, @Reasoning)
        """, it);
	}

	public IEnumerable<Item> Recent(int days = 7) =>
		_conn.Query<Item>("SELECT * FROM items WHERE created_ts >= strftime('%s','now','-" + days + " day') ORDER BY created_ts DESC");

	public IEnumerable<Item> ByStatus(string status) =>
		_conn.Query<Item>("SELECT * FROM items WHERE status = @s ORDER BY created_ts DESC", new { s = status });

	public IEnumerable<Item> Search(string keyword)
	{
		var kw = $"%{keyword}%";
		return _conn.Query<Item>(
			@"SELECT * FROM items
              WHERE filename LIKE @kw OR category LIKE @kw OR summary LIKE @kw
              ORDER BY created_ts DESC", new { kw });
	}

	// 低信心（待確認）清單
	public IEnumerable<Item> PendingLowConfidence(double threshold100) =>
		_conn.Query<Item>(
			@"SELECT * FROM items
              WHERE confidence < @th
              ORDER BY created_ts DESC", new { th = threshold100 });

	// ✅ 一鍵確認後的資料更新：寫入新路徑與狀態
	public void UpdateAfterMove(long id, string newPath, string newStatus = "To-Do") =>
		_conn.Execute(
			"UPDATE items SET path=@p, filename=@f, status=@s WHERE id=@id",
			new { id, p = newPath, f = Path.GetFileName(newPath), s = newStatus });

	public void Dispose() => _conn.Dispose();
}
